using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Subscriptions.Application;
using Subscriptions.Domain;
using Subscriptions.Infrastructure;
namespace Subscriptions.Api.Tests;
[TestFixture,NonParallelizable] public class BusinessApiTests : ApiContext
{
    [Test] public async Task SubscriptionCrudFiltersStatusAndArchive()
    {
        await Login();var id=await Sub();
        var list=await Json(await Expect(await Client.GetAsync("/api/v1/subscriptions?status=active&category=Media&page=1&pageSize=1"),200));Assert.That(list.GetProperty("total").GetInt32(),Is.EqualTo(1));Assert.That(list.GetProperty("page").GetInt32(),Is.EqualTo(1));
        var next=DateOnly.FromDateTime(Factory.Clock.UtcNow.UtcDateTime).AddDays(1).ToString("yyyy-MM-dd");
        await Expect(await Client.PatchAsJsonAsync($"/api/v1/subscriptions/{id}",new{name="Updated",amount=20m,nextPaymentDate=next}),200);
        foreach(var command in new[]{"pause","pause","resume","cancel","cancel"})await Expect(await Client.PostAsync($"/api/v1/subscriptions/{id}/{command}",null),200);
        await Expect(await Client.PostAsync($"/api/v1/subscriptions/{id}/resume",null),409);
        await Expect(await Client.DeleteAsync($"/api/v1/subscriptions/{id}"),204);await Expect(await Client.DeleteAsync($"/api/v1/subscriptions/{id}"),204);
        Assert.That((await Json(await Client.GetAsync("/api/v1/subscriptions"))).GetProperty("total").GetInt32(),Is.Zero);
        Assert.That((await Json(await Client.GetAsync("/api/v1/subscriptions?includeArchived=true"))).GetProperty("total").GetInt32(),Is.EqualTo(1));
        Assert.That((await Json(await Client.GetAsync($"/api/v1/subscriptions/{id}"))).GetProperty("archived").GetBoolean(),Is.True);
    }
    [Test] public async Task PaymentReplayCorrectionVoidAuditAndHistoricalCurrency()
    {
        await Login();var id=await Sub();var first=await Json(await Expect(await PostPayment(id,"key"),201));var pid=first.GetProperty("id").GetGuid();
        Assert.That((await Json(await Expect(await PostPayment(id,"key",PaymentBody(10.00m)),201))).ToString(),Is.EqualTo(first.ToString()));
        await Expect(await PostPayment(id,"key",PaymentBody(11)),409);await Expect(await PostPayment(id,"other"),201);
        await Expect(await Client.PatchAsJsonAsync($"/api/v1/subscriptions/{id}",new{currency="USD",category="New"}),200);
        var updated=await Json(await Expect(await Client.PatchAsJsonAsync($"/api/v1/payments/{pid}",new{amount=.3m}),200));Assert.That(updated.GetProperty("currency").GetString(),Is.EqualTo("RUB"));Assert.That(updated.GetProperty("snapshotCategory").GetString(),Is.EqualTo("Media"));
        await Expect(await Client.PostAsync($"/api/v1/payments/{pid}/void",null),200);await Expect(await Client.PostAsync($"/api/v1/payments/{pid}/void",null),200);await Expect(await Client.PatchAsJsonAsync($"/api/v1/payments/{pid}",new{amount=5m}),409);
        var audit=await Json(await Expect(await Client.GetAsync($"/api/v1/payments/{pid}/audit"),200));Assert.That(audit.GetArrayLength(),Is.EqualTo(3));Assert.That(audit.EnumerateArray().Select(x=>x.GetProperty("action").GetString()),Is.EquivalentTo(new[]{"create","correct","void"}));
        await Expect(await Client.DeleteAsync($"/api/v1/subscriptions/{id}"),204);
        Assert.That((await Json(await Expect(await PostPayment(id,"key"),201))).ToString(),Is.EqualTo(first.ToString()));
        var history=await Json(await Expect(await Client.GetAsync($"/api/v1/payments?subscriptionId={id}&status=successful&pageSize=100"),200));Assert.That(history.GetProperty("total").GetInt32(),Is.EqualTo(2));
    }
    [Test] public async Task ForeignResourcesLookAbsentForAllOperations()
    {
        await Login();var sub=await Sub();var payment=await Json(await PostPayment(sub,"key"));var pid=payment.GetProperty("id").GetGuid();await Login("b@example.test");
        await Expect(await Client.GetAsync($"/api/v1/subscriptions/{sub}"),404);await Expect(await Client.PatchAsJsonAsync($"/api/v1/subscriptions/{sub}",new{name="stolen"}),404);
        foreach(var command in new[]{"pause","resume","cancel"})await Expect(await Client.PostAsync($"/api/v1/subscriptions/{sub}/{command}",null),404);
        await Expect(await Client.DeleteAsync($"/api/v1/subscriptions/{sub}"),404);await Expect(await PostPayment(sub,"other"),404);
        await Expect(await Client.GetAsync($"/api/v1/payments?subscriptionId={sub}"),404);await Expect(await Client.PatchAsJsonAsync($"/api/v1/payments/{pid}",new{amount=1m}),404);
        await Expect(await Client.PostAsync($"/api/v1/payments/{pid}/void",null),404);await Expect(await Client.GetAsync($"/api/v1/payments/{pid}/audit"),404);
        Assert.That((await Json(await Client.GetAsync("/api/v1/payments"))).GetProperty("total").GetInt32(),Is.Zero);
    }
    [Test] public async Task AnalyticsAndForecastUseOwnCurrencySnapshotsAndArchiveRules()
    {
        await Login();var sub=await Sub();await PostPayment(sub,"one",PaymentBody(.1m));await PostPayment(sub,"two",PaymentBody(.2m));
        var today=DateOnly.FromDateTime(Factory.Clock.UtcNow.UtcDateTime).ToString("yyyy-MM-dd");var route=$"/api/v1/analytics/expenses?from={today}&to={today}";
        var expenses=await Json(await Expect(await Client.GetAsync(route),200));Assert.That(expenses.GetProperty("totals").GetProperty("RUB").GetDecimal(),Is.EqualTo(.3m));
        var forecast=await Json(await Expect(await Client.GetAsync("/api/v1/analytics/forecast"),200));Assert.That(forecast.GetProperty("items").GetArrayLength(),Is.GreaterThanOrEqualTo(1));
        await Client.PostAsync($"/api/v1/subscriptions/{sub}/pause",null);Assert.That((await Json(await Client.GetAsync("/api/v1/analytics/forecast"))).GetProperty("items").GetArrayLength(),Is.Zero);
        await Client.PostAsync($"/api/v1/subscriptions/{sub}/resume",null);await Client.DeleteAsync($"/api/v1/subscriptions/{sub}");
        Assert.That((await Json(await Client.GetAsync(route))).GetProperty("totals").GetProperty("RUB").GetDecimal(),Is.EqualTo(.3m));
        var empty=await Json(await Client.GetAsync("/api/v1/analytics/expenses?from=2001-01-01&to=2001-01-01"));Assert.That(empty.GetProperty("byMonth").GetArrayLength(),Is.Zero);
    }
    [TestCase("Payment")] [TestCase("IdempotencyRecord")] [TestCase("AuditRecord")]
    public async Task TransactionFailureReallyRollsBackAllRows(string kind)
    {
        await Login();var sub=await Sub();Factory.Observer.Kind=kind;await Expect(await PostPayment(sub,"key"),500);Factory.Observer.Kind=null;
        var store=Factory.Services.GetRequiredService<IStore>();Assert.That(store.Read(d=>d.All<Payment>().Count+d.All<IdempotencyRecord>().Count+d.All<AuditRecord>().Count),Is.Zero);
        await Expect(await PostPayment(sub,"key"),201);
    }
    [Test] public async Task CorrectionAuditFailureRollsBackPayment()
    {
        await Login();var sub=await Sub();var pay=await Json(await PostPayment(sub,"key"));var id=pay.GetProperty("id").GetGuid();Factory.Observer.Kind="AuditRecord";
        await Expect(await Client.PatchAsJsonAsync($"/api/v1/payments/{id}",new{amount=99m}),500);Factory.Observer.Kind=null;
        var history=await Json(await Client.GetAsync("/api/v1/payments"));Assert.That(history.GetProperty("items")[0].GetProperty("amount").GetDecimal(),Is.EqualTo(10));
        Assert.That((await Json(await Client.GetAsync($"/api/v1/payments/{id}/audit"))).GetArrayLength(),Is.EqualTo(1));
    }
    [Test] public async Task ConcurrentSameKeyCreatesExactlyOneTriplet()
    {
        await Login();var sub=await Sub();var results=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>PostPayment(sub,"same")));
        Assert.That(results.All(x=>(int)x.StatusCode==201),Is.True);var ids=new List<Guid>();foreach(var r in results)ids.Add((await Json(r)).GetProperty("id").GetGuid());Assert.That(ids.Distinct().Count(),Is.EqualTo(1));
        var store=Factory.Services.GetRequiredService<IStore>();Assert.That(store.Read(d=>d.All<Payment>().Count),Is.EqualTo(1));Assert.That(store.Read(d=>d.All<IdempotencyRecord>().Count),Is.EqualTo(1));Assert.That(store.Read(d=>d.All<AuditRecord>().Count),Is.EqualTo(1));
    }
    [Test] public async Task RealDatabaseCannotOpenReturns503AndServerRecovers()
    {
        await Login();var backup=Factory.DatabasePath+".backup";File.Move(Factory.DatabasePath,backup);Directory.CreateDirectory(Factory.DatabasePath);
        try { await Expect(await Client.PostAsJsonAsync("/api/v1/subscriptions",SubscriptionBody()),503);await Expect(await Client.GetAsync("/api/v1/health"),503); }
        finally { Directory.Delete(Factory.DatabasePath);File.Move(backup,Factory.DatabasePath); }
        await Expect(await Client.GetAsync("/api/v1/health"),200);Assert.That((await Json(await Client.GetAsync("/api/v1/subscriptions"))).GetProperty("total").GetInt32(),Is.Zero);
    }
    [Test] public async Task OpenApiAndSwaggerExposeSchemasSecurityAndIdempotencyHeader()
    {
        await Expect(await Client.GetAsync("/swagger/index.html"),200);var spec=await Json(await Expect(await Client.GetAsync("/swagger/v1/swagger.json"),200));
        Assert.That(spec.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer",out _),Is.True);
        var paths=spec.GetProperty("paths");Assert.That(paths.EnumerateObject().Count(),Is.EqualTo(19));
        Assert.That(paths.EnumerateObject().Sum(p=>p.Value.EnumerateObject().Count(x=>new[]{"get","post","patch","delete"}.Contains(x.Name))),Is.EqualTo(22));
        Assert.That(paths.GetProperty("/api/v1/subscriptions").GetProperty("post").GetProperty("security")[0].GetProperty("Bearer").GetArrayLength(),Is.Zero);
        Assert.That(paths.GetProperty("/api/v1/auth/register").GetProperty("post").TryGetProperty("requestBody",out _),Is.True);
        var parameters=paths.GetProperty("/api/v1/subscriptions/{subscriptionId}/payments").GetProperty("post").GetProperty("parameters");Assert.That(parameters.EnumerateArray().Any(x=>x.GetProperty("name").GetString()=="Idempotency-Key"),Is.True);
    }
    [TestCase("/api/v1/subscriptions?pageSize=101")] [TestCase("/api/v1/subscriptions?page=0")] [TestCase("/api/v1/subscriptions?status=unknown")] [TestCase("/api/v1/subscriptions/not-uuid")]
    [TestCase("/api/v1/subscriptions?userId=someone")] [TestCase("/api/v1/payments?from=bad")] [TestCase("/api/v1/payments?from=2026-02-01&to=2026-01-01")]
    [TestCase("/api/v1/analytics/expenses?from=2024-01-01&to=2025-01-01")] [TestCase("/api/v1/analytics/expenses")] [TestCase("/api/v1/analytics/expenses?from=2024-01-01")] [TestCase("/api/v1/analytics/expenses?to=2024-01-01")]
    public async Task InvalidQueriesHaveUnifiedValidation(string path){await Login();await Expect(await Client.GetAsync(path),400);}
    [TestCase("{}")] [TestCase("{\"name\":null}")] [TestCase("{\"userId\":\"steal\"}")] [TestCase("{\"amount\":-1}")]
    public async Task BadPatchesRejected(string body)
    {
        await Login();var sub=await Sub();await Expect(await Client.PatchAsync($"/api/v1/subscriptions/{sub}",new StringContent(body,Encoding.UTF8,"application/json")),400);
    }
    [Test] public async Task OversizeBothLengthAndChunkedAndCorrelationValidation()
    {
        await Login();var body="{\"name\":\""+new string('x',1048576)+"\"}";
        await Expect(await Client.PostAsync("/api/v1/subscriptions",new StringContent(body,Encoding.UTF8,"application/json")),413);
        var request=new HttpRequestMessage(HttpMethod.Post,"/api/v1/subscriptions"){Content=new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)))};request.Headers.TransferEncodingChunked=true;
        await Expect(await Client.SendAsync(request),413);
        Client.DefaultRequestHeaders.Add("X-Correlation-ID","my-correlation-123");Assert.That((await Client.GetAsync("/api/v1/health")).Headers.GetValues("X-Correlation-ID").Single(),Is.EqualTo("my-correlation-123"));
        Client.DefaultRequestHeaders.Remove("X-Correlation-ID");Client.DefaultRequestHeaders.Add("X-Correlation-ID","bad value");Assert.That((await Client.GetAsync("/api/v1/health")).Headers.GetValues("X-Correlation-ID").Single(),Is.Not.EqualTo("bad value"));
    }
    [Test] public async Task InvalidCreateAndMissingKeyDoNotWrite()
    {
        await Login();await Expect(await Client.PostAsJsonAsync("/api/v1/subscriptions",new{name="bad"}),400);var sub=await Sub();
        await Expect(await Client.PostAsJsonAsync($"/api/v1/subscriptions/{sub}/payments",PaymentBody()),400);
        await Expect(await PostPayment(sub,"currency",new{amount=10,currency="USD",date="2020-01-01",status="successful"}),400);
        Assert.That(Factory.Services.GetRequiredService<IStore>().Read(d=>d.All<Payment>().Count),Is.Zero);
    }
    [Test] public async Task BodylessCommandsDoNotSilentlyAcceptUnknownPayload()
    {
        await Login();var sub=await Sub();
        await Expect(await Client.PostAsJsonAsync($"/api/v1/subscriptions/{sub}/pause",new{userId="ignored-foreign-id"}),400);
    }

    [Test] public async Task StartupWithUnavailableDatabaseStillServes503()
    {
        using var blocked=new TestFactory();Directory.CreateDirectory(blocked.DatabasePath);
        using var client=blocked.CreateClient();await Expect(await client.GetAsync("/api/v1/health"),503);
        Directory.Delete(blocked.DatabasePath);await Expect(await client.GetAsync("/api/v1/health"),200);
    }

    [Test] public async Task EarliestHistoricalDateIsValidAndSurvivesSqliteAndJson()
    {
        await Login();var sub=await Sub();await Expect(await PostPayment(sub,"earliest",new{amount=10,currency="RUB",date="0001-01-01",status="successful"}),201);
        var report=await Json(await Expect(await Client.GetAsync("/api/v1/analytics/expenses?from=0001-01-01&to=0001-01-01"),200));
        Assert.That(report.GetProperty("totals").GetProperty("RUB").GetDecimal(),Is.EqualTo(10));
    }

}
