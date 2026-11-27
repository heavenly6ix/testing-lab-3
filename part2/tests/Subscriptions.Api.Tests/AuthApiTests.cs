using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;
using Subscriptions.Application;
using Subscriptions.Infrastructure;
using Subscriptions.Domain;
namespace Subscriptions.Api.Tests;
[TestFixture,NonParallelizable] public class AuthApiTests : ApiContext
{
    [Test] public async Task RegistrationNormalizationDuplicateAndValidation()
    {
        await Expect(await Client.PostAsJsonAsync("/api/v1/auth/register",new{email=" A@EXAMPLE.TEST ",password=Password}),201);
        await Expect(await Client.PostAsJsonAsync("/api/v1/auth/register",new{email="a@example.test",password=Password}),409);
        await Expect(await Client.PostAsJsonAsync("/api/v1/auth/register",new{email="bad",password="short"}),400);
        var store=Factory.Services.GetRequiredService<IStore>();Assert.That(store.Read(d=>d.All<UserAccount>().Count),Is.EqualTo(1));
        var persisted=store.Read(d=>d.All<UserAccount>().Single());Assert.That(persisted.PasswordHash,Does.StartWith("argon2id$"));Assert.That(persisted.PasswordHash,Does.Not.Contain(Password));
    }
    [Test] public async Task LoginErrorsAreUniformAndBearerRequired()
    {
        await Login();Client.DefaultRequestHeaders.Authorization=null;
        var a=await Json(await Expect(await Client.PostAsJsonAsync("/api/v1/auth/login",new{email="a@example.test",password="bad"}),401));
        var b=await Json(await Expect(await Client.PostAsJsonAsync("/api/v1/auth/login",new{email="absent@example.test",password="bad"}),401));
        Assert.That(a.GetProperty("code").GetString(),Is.EqualTo(b.GetProperty("code").GetString()));Assert.That(a.GetProperty("message").GetString(),Is.EqualTo(b.GetProperty("message").GetString()));
        await Expect(await Client.GetAsync("/api/v1/subscriptions"),401);
    }
    [Test] public async Task RefreshRaceRotationLogoutRevokesChainNotOtherSessions()
    {
        var original=await Login();var other=await Login(register:false);var raw=original.GetProperty("refreshToken").GetString();
        var responses=await Task.WhenAll(Enumerable.Range(0,2).Select(_=>Client.PostAsJsonAsync("/api/v1/auth/refresh",new{refreshToken=raw})));
        Assert.That(responses.Select(x=>(int)x.StatusCode),Is.EquivalentTo(new[]{200,401}));
        var rotated=await Json(responses.Single(x=>(int)x.StatusCode==200));Assert.That(rotated.GetProperty("refreshExpiresAt").GetString(),Is.EqualTo(original.GetProperty("refreshExpiresAt").GetString()));
        await Expect(await Client.PostAsJsonAsync("/api/v1/auth/logout",new{refreshToken=raw}),204);await Expect(await Client.PostAsJsonAsync("/api/v1/auth/logout",new{refreshToken=raw}),204);
        await Expect(await Client.PostAsJsonAsync("/api/v1/auth/refresh",new{refreshToken=rotated.GetProperty("refreshToken").GetString()}),401);
        await Expect(await Client.PostAsJsonAsync("/api/v1/auth/refresh",new{refreshToken=other.GetProperty("refreshToken").GetString()}),200);
    }
    [Test] public async Task ResetSenderExpiryOneUseAndAllRefreshRevoked()
    {
        var pair=await Login();var known=await Json(await Expect(await Client.PostAsJsonAsync("/api/v1/auth/password/forgot",new{email="a@example.test"}),202));
        var unknown=await Json(await Expect(await Client.PostAsJsonAsync("/api/v1/auth/password/forgot",new{email="missing@example.test"}),202));Assert.That(known.ToString(),Is.EqualTo(unknown.ToString()));
        var sender=Factory.Services.GetRequiredService<MemoryEmailSender>();var token=sender.Snapshot().Single().Token;
        await Expect(await Client.PostAsJsonAsync("/api/v1/auth/password/reset",new{token,password="new-password-123"}),204);
        await Expect(await Client.PostAsJsonAsync("/api/v1/auth/password/reset",new{token,password="other-password"}),400);
        await Expect(await Client.PostAsJsonAsync("/api/v1/auth/refresh",new{refreshToken=pair.GetProperty("refreshToken").GetString()}),401);
        await Expect(await Client.PostAsJsonAsync("/api/v1/auth/login",new{email="a@example.test",password=Password}),401);
        await Expect(await Client.PostAsJsonAsync("/api/v1/auth/login",new{email="a@example.test",password="new-password-123"}),200);
        await Client.PostAsJsonAsync("/api/v1/auth/password/forgot",new{email="a@example.test"});token=sender.Snapshot().Single().Token;Factory.Clock.UtcNow=Factory.Clock.UtcNow.AddMinutes(15);
        await Expect(await Client.PostAsJsonAsync("/api/v1/auth/password/reset",new{token,password="next-password-123"}),400);
    }
    [Test] public async Task AccountLimitFiveFailuresThen429AndRollingExpiry()
    {
        for(var i=0;i<5;i++)await Expect(await Client.PostAsJsonAsync("/api/v1/auth/login",new{email="unknown@example.test",password="bad"}),401);
        var limited=await Expect(await Client.PostAsJsonAsync("/api/v1/auth/login",new{email="unknown@example.test",password=Password}),429);Assert.That(limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds,Is.EqualTo(900));
        Factory.Clock.UtcNow=Factory.Clock.UtcNow.AddMinutes(15);await Expect(await Client.PostAsJsonAsync("/api/v1/auth/login",new{email="unknown@example.test",password="bad"}),401);
    }
    [Test] public async Task IpLimitCountsThirtyAttemptsIncludingBadBodies()
    {
        for(var i=0;i<30;i++)await Expect(await Client.PostAsync("/api/v1/auth/login",new StringContent("{}",Encoding.UTF8,"application/json")),400);
        var limited=await Expect(await Client.PostAsync("/api/v1/auth/login",new StringContent("{}",Encoding.UTF8,"application/json")),429);Assert.That(limited.Headers.RetryAfter,Is.Not.Null);
        Factory.Clock.UtcNow=Factory.Clock.UtcNow.AddMinutes(15);await Expect(await Client.PostAsync("/api/v1/auth/login",new StringContent("{}",Encoding.UTF8,"application/json")),400);
    }
    [TestCase("signature")] [TestCase("issuer")] [TestCase("audience")] [TestCase("expired")] [TestCase("subject")]
    public async Task JwtChecksRejectInvalidTokens(string kind)
    {
        var settings=Factory.Services.GetRequiredService<JwtSettings>();var now=DateTimeOffset.UtcNow;var issuer=new JwtIssuer(settings with{Key=kind=="signature"?Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)):settings.Key,Issuer=kind=="issuer"?"bad":settings.Issuer,Audience=kind=="audience"?"bad":settings.Audience});
        var raw=issuer.Access(Guid.NewGuid(),kind=="expired"?now.AddHours(-1):now,kind=="expired"?now.AddMinutes(-30):now.AddMinutes(15));
        if(kind=="subject")
        {
            var jwt=new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(settings.Issuer,settings.Audience,[],now.UtcDateTime,now.AddMinutes(15).UtcDateTime,new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),SecurityAlgorithms.HmacSha256));raw=new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(jwt);
        }
        Client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",raw);await Expect(await Client.GetAsync("/api/v1/subscriptions"),401);
    }
    [Test] public async Task SensitiveValuesNeverAppearInRequestLogs()
    {
        var tokens=await Login();var all=string.Join('\n',Factory.Logs.Messages);Assert.That(all,Does.Not.Contain(Password));Assert.That(all,Does.Not.Contain(tokens.GetProperty("accessToken").GetString()!));Assert.That(all,Does.Not.Contain(tokens.GetProperty("refreshToken").GetString()!));Assert.That(all,Does.Contain("/api/v1/auth/login"));
    }
    [Test] public async Task ConcurrentNormalizedRegistrationsCreateOneUser()
    {
        var requests=new[]{"race@example.test"," RACE@EXAMPLE.TEST "}.Select(email=>Client.PostAsJsonAsync("/api/v1/auth/register",new{email,password=Password}));
        var results=await Task.WhenAll(requests);Assert.That(results.Select(x=>(int)x.StatusCode),Is.EquivalentTo(new[]{201,409}));
        Assert.That(Factory.Services.GetRequiredService<IStore>().Read(d=>d.All<UserAccount>().Count),Is.EqualTo(1));
    }
    [Test] public async Task AdvertisedAccessExpiryEqualsSignedJwtExpiry()
    {
        var pair=await Login();var jwt=new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(pair.GetProperty("accessToken").GetString());
        Assert.That(pair.GetProperty("accessExpiresAt").GetDateTimeOffset().UtcDateTime,Is.EqualTo(jwt.ValidTo));
    }

}
