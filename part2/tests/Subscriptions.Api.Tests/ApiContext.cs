using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Subscriptions.Application;
using Subscriptions.Infrastructure;
namespace Subscriptions.Api.Tests;
public sealed class MutableClock : IClock { public DateTimeOffset UtcNow { get; set; }=DateTimeOffset.UtcNow; }
public sealed class FailureObserver : IWriteObserver
{
    public string? Kind { get; set; }
    public void AfterWrite(string kind) {if(kind==Kind)throw new InvalidOperationException("Injected transaction interruption.");}
}
public sealed class LogCapture : ILoggerProvider
{
    public readonly List<string> Messages=[];
    public ILogger CreateLogger(string name)=>new Capture(this);
    public void Dispose() { }
    private sealed class Capture(LogCapture owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
        public bool IsEnabled(LogLevel level)=>true;
        public void Log<TState>(LogLevel level,EventId id,TState state,Exception? error,Func<TState,Exception?,string> format){lock(owner.Messages)owner.Messages.Add(format(state,error));}
    }
}
public sealed class TestFactory : WebApplicationFactory<Program>
{
    public string Folder { get; }=Path.Combine(Path.GetTempPath(),"subscriptions-tests-"+Guid.NewGuid());
    public string DatabasePath=>Path.Combine(Folder,"test.db");
    public MutableClock Clock { get; }=new();
    public FailureObserver Observer { get; }=new();
    public LogCapture Logs { get; }=new();
    private static readonly Lazy<ArgonPasswordHasher> Hasher=new(()=>new());
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services=>
        {
            services.RemoveAll<IClock>();services.AddSingleton<IClock>(Clock);
            services.RemoveAll<IStore>();services.AddSingleton<IStore>(new SqliteStore(DatabasePath,Observer));
            services.RemoveAll<IPasswordHasher>();services.AddSingleton<IPasswordHasher>(Hasher.Value);
            services.AddSingleton<ILoggerProvider>(Logs);
        });
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);if(Directory.Exists(Folder))Directory.Delete(Folder,true);
    }
}
public abstract class ApiContext
{
    protected TestFactory Factory=null!;
    protected HttpClient Client=null!;
    protected const string Password="test-password-123";
    [SetUp] public void Setup(){Factory=new();Client=Factory.CreateClient();}
    [TearDown] public void Cleanup(){Client.Dispose();Factory.Dispose();}
    protected static async Task<JsonElement> Json(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    protected static async Task<HttpResponseMessage> Expect(HttpResponseMessage response,int status)
    {
        Assert.That((int)response.StatusCode,Is.EqualTo(status),"Unexpected HTTP status (sensitive bodies deliberately not printed).");
        Assert.That(response.Headers.Contains("X-Correlation-ID"),Is.True);
        if(status>=400)
        {
            var body=await Json(response);Assert.That(body.GetProperty("code").GetString(),Is.Not.Empty);Assert.That(body.GetProperty("message").GetString(),Is.Not.Empty);
            Assert.That(body.GetProperty("correlationId").GetString(),Is.EqualTo(response.Headers.GetValues("X-Correlation-ID").Single()));
            Assert.That(body.TryGetProperty("fields",out _),Is.EqualTo(status==400));
        }
        return response;
    }
    protected async Task<JsonElement> Login(string email="a@example.test",bool register=true,HttpClient? client=null)
    {
        client??=Client;if(register)await Expect(await client.PostAsJsonAsync("/api/v1/auth/register",new{email,password=Password}),201);
        var tokens=await Json(await Expect(await client.PostAsJsonAsync("/api/v1/auth/login",new{email,password=Password}),200));
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",tokens.GetProperty("accessToken").GetString());return tokens;
    }
    protected object SubscriptionBody(string currency="RUB")=>new{name="Music",amount=10.00m,currency,billingPeriod="monthly",category="Media",startDate="2020-01-01",nextPaymentDate=DateOnly.FromDateTime(Factory.Clock.UtcNow.UtcDateTime).ToString("yyyy-MM-dd")};
    protected async Task<Guid> Sub()
    {
        var result=await Json(await Expect(await Client.PostAsJsonAsync("/api/v1/subscriptions",SubscriptionBody()),201));return result.GetProperty("id").GetGuid();
    }
    protected object PaymentBody(decimal amount=10m)=>new{amount,currency="RUB",date=DateOnly.FromDateTime(Factory.Clock.UtcNow.UtcDateTime).ToString("yyyy-MM-dd"),status="successful"};
    protected async Task<HttpResponseMessage> PostPayment(Guid sub,string key,object? body=null)
    {
        var request=new HttpRequestMessage(HttpMethod.Post,$"/api/v1/subscriptions/{sub}/payments"){Content=JsonContent.Create(body??PaymentBody())};request.Headers.Add("Idempotency-Key",key);return await Client.SendAsync(request);
    }
}
