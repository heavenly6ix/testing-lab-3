using Moq;
using NUnit.Framework;
using Subscriptions.Application;
using Subscriptions.Domain;
namespace Subscriptions.Application.Tests;
[TestFixture] public class AuthTests : TestContext
{
    [Test] public void Registration_NormalizesAndRejectsDuplicate()
    {
        var result=Auth.Register(Credentials("  STUDENT@EXAMPLE.TEST  "));Assert.That(result.Email,Is.EqualTo("student@example.test"));
        Assert.That(Data.All<UserAccount>().Single().PasswordHash,Is.EqualTo("hash:password-12345"));
        Assert.That(Fault(()=>Auth.Register(Credentials()),FaultKind.Conflict).Code,Is.EqualTo("duplicate_email"));Assert.That(Data.All<UserAccount>(),Has.Count.EqualTo(1));
    }
    [TestCase(null)] [TestCase("")] [TestCase("bad")] [TestCase("Name <a@b.test>")]
    public void Registration_InvalidEmail(string? email) => Fault(()=>Auth.Register(Credentials(email!)),FaultKind.Validation);
    [TestCase(null)] [TestCase("")] [TestCase("short")]
    public void Registration_InvalidPassword(string? password)=>Fault(()=>Auth.Register(Credentials(password:password!)),FaultKind.Validation);
    [Test] public void Registration_OversizedInputs()
    {
        Fault(()=>Auth.Register(Credentials(new string('a',250)+"@x.test")),FaultKind.Validation);
        Fault(()=>Auth.Register(Credentials(password:new string('x',129))),FaultKind.Validation);
    }
    [Test] public void Login_CorrectIssuesPairWithBounds()
    {
        var user=Auth.Register(Credentials());var result=Auth.Login(Credentials());Assert.That(result.AccessToken,Is.EqualTo("jwt"));
        Assert.That(result.AccessExpiresAt-Now,Is.EqualTo(TimeSpan.FromMinutes(15)));Assert.That(result.RefreshExpiresAt-Now,Is.EqualTo(TimeSpan.FromDays(30)));
        Assert.That(Data.All<Session>().Single().Hash,Is.EqualTo("digest:"+result.RefreshToken));Issuer.Verify(x=>x.Access(user.Id,Now,Now.AddMinutes(15)),Times.Once);
    }
    [TestCase("student@example.test","wrong-password")] [TestCase("unknown@example.test","wrong-password")]
    public void Login_InvalidCredentials_AreUniform(string email,string password)
    {
        Auth.Register(Credentials());Assert.That(Fault(()=>Auth.Login(Credentials(email,password)),FaultKind.Unauthorized).Code,Is.EqualTo("unauthorized"));
        Assert.That(Data.All<LoginFailures>().Single().Attempts,Has.Length.EqualTo(1));
    }
    [Test] public void Login_ShortWrongPassword_IsStillUnauthorized() { Auth.Register(Credentials());Fault(()=>Auth.Login(Credentials(password:"bad")),FaultKind.Unauthorized); }
    [Test] public void AccountLimit_ExactRollingWindowPersistsFailures()
    {
        for (var i=0;i<5;i++) Fault(()=>Auth.Login(Credentials(password:"wrong-password")),FaultKind.Unauthorized);
        var limited=Fault(()=>Auth.Login(Credentials()),FaultKind.RateLimited);Assert.That(limited.RetryAfter,Is.EqualTo(900));
        Now=Now.AddMinutes(15);Fault(()=>Auth.Login(Credentials()),FaultKind.Unauthorized);Assert.That(Data.All<LoginFailures>().Single().Attempts,Has.Length.EqualTo(1));
    }
    [Test] public void Login_IpAndAccountLimitReturnsLargerRetry()
    {
        for(var i=0;i<5;i++) Fault(()=>Auth.Login(Credentials()),FaultKind.Unauthorized);
        Assert.That(Fault(()=>Auth.Login(Credentials(),10),FaultKind.RateLimited).RetryAfter,Is.EqualTo(900));
        Assert.That(Fault(()=>Auth.Login(Credentials("other@example.test"),1000),FaultKind.RateLimited).RetryAfter,Is.EqualTo(1000));
    }
    [Test] public void SuccessDoesNotErasePreviousFailures()
    {
        Auth.Register(Credentials());Fault(()=>Auth.Login(Credentials(password:"wrong-password")),FaultKind.Unauthorized);Auth.Login(Credentials());Assert.That(Data.All<LoginFailures>().Single().Attempts,Has.Length.EqualTo(1));
    }
    [Test] public void Refresh_RotatesAndPreservesAbsoluteExpiry()
    {
        Auth.Register(Credentials());var first=Auth.Login(Credentials());Now=Now.AddDays(29);var second=Auth.Refresh(first.RefreshToken);
        Assert.That(second.RefreshToken,Is.Not.EqualTo(first.RefreshToken));Assert.That(second.RefreshExpiresAt,Is.EqualTo(first.RefreshExpiresAt));Fault(()=>Auth.Refresh(first.RefreshToken),FaultKind.Unauthorized);
    }
    [TestCase("unknown")] [TestCase("expired")] [TestCase("revoked")]
    public void Refresh_InvalidStateRejected(string state)
    {
        Auth.Register(Credentials());var pair=Auth.Login(Credentials());var token=pair.RefreshToken;
        if(state=="expired")Now=pair.RefreshExpiresAt;if(state=="revoked")Auth.Logout(token);if(state=="unknown")token="unknown";
        Fault(()=>Auth.Refresh(token),FaultKind.Unauthorized);
    }
    [Test] public void Logout_IdempotentRevokesSessionChainOnly()
    {
        Auth.Register(Credentials());var first=Auth.Login(Credentials());var other=Auth.Login(Credentials());var rotated=Auth.Refresh(first.RefreshToken);
        Auth.Logout(first.RefreshToken);Auth.Logout(first.RefreshToken);Auth.Logout("unknown");Fault(()=>Auth.Refresh(rotated.RefreshToken),FaultKind.Unauthorized);Assert.That(Auth.Refresh(other.RefreshToken),Is.Not.Null);
    }
    [Test] public void Reset_ChangesPasswordConsumesTokenRevokesAllSessions()
    {
        Auth.Register(Credentials());var a=Auth.Login(Credentials());var b=Auth.Login(Credentials());Auth.Forgot(" STUDENT@example.test ");var token=Mails.Single().Token;
        Auth.Reset(new() {Token=token,Password="new-password-123"});Fault(()=>Auth.Login(Credentials()),FaultKind.Unauthorized);
        Assert.That(Auth.Login(Credentials(password:"new-password-123")),Is.Not.Null);Fault(()=>Auth.Refresh(a.RefreshToken),FaultKind.Unauthorized);Fault(()=>Auth.Refresh(b.RefreshToken),FaultKind.Unauthorized);
        Fault(()=>Auth.Reset(new() {Token=token,Password="other-password"}),FaultKind.Validation);
    }
    [Test] public void Forgot_UnknownDoesNotSend_NewRequestInvalidatesPrevious()
    {
        Auth.Forgot("unknown@example.test");Assert.That(Mails,Is.Empty);Auth.Register(Credentials());Auth.Forgot("student@example.test");var old=Mails.Single().Token;Auth.Forgot("student@example.test");
        Fault(()=>Auth.Reset(new() {Token=old,Password="new-password-123"}),FaultKind.Validation);Assert.That(Mails,Has.Count.EqualTo(2));
    }
    [TestCase("expired")] [TestCase("unknown")] [TestCase("missing-user")]
    public void Reset_InvalidToken(string state)
    {
        Auth.Register(Credentials());Auth.Forgot("student@example.test");var token=Mails.Single().Token;
        if(state=="expired")Now=Mails.Single().End;if(state=="unknown")token="unknown";
        if(state=="missing-user") {var user=Data.All<UserAccount>().Single();Data.Rows.Remove((typeof(UserAccount),user.Id));}
        Fault(()=>Auth.Reset(new() {Token=token,Password="new-password-123"}),FaultKind.Validation);
    }
    [Test] public void TokenInputsCannotBeEmpty()
    {
        Fault(()=>Auth.Refresh(""),FaultKind.Validation);Fault(()=>Auth.Logout(""),FaultKind.Validation);Fault(()=>Auth.Reset(new(){Token="",Password="new-password-123"}),FaultKind.Validation);
    }
}
