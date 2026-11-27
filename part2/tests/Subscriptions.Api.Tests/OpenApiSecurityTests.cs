using NUnit.Framework;
namespace Subscriptions.Api.Tests;

[TestFixture, NonParallelizable]
public class OpenApiSecurityTests : ApiContext
{
    private static readonly (string Method, string Path)[] ProtectedOperations =
    [
        ("post", "/api/v1/subscriptions"), ("get", "/api/v1/subscriptions"),
        ("get", "/api/v1/subscriptions/{id}"), ("patch", "/api/v1/subscriptions/{id}"),
        ("delete", "/api/v1/subscriptions/{id}"),
        ("post", "/api/v1/subscriptions/{id}/pause"), ("post", "/api/v1/subscriptions/{id}/resume"),
        ("post", "/api/v1/subscriptions/{id}/cancel"),
        ("post", "/api/v1/subscriptions/{subscriptionId}/payments"),
        ("get", "/api/v1/payments"), ("patch", "/api/v1/payments/{id}"),
        ("post", "/api/v1/payments/{id}/void"), ("get", "/api/v1/payments/{id}/audit"),
        ("get", "/api/v1/analytics/expenses"), ("get", "/api/v1/analytics/forecast")
    ];

    [Test]
    public async Task ProtectedOperations_SerializeNamedBearerRequirementWithoutAnonymousAlternative()
    {
        var document = await Json(await Expect(await Client.GetAsync("/swagger/v1/swagger.json"), 200));
        Assert.Multiple(() =>
        {
            foreach (var (method, path) in ProtectedOperations)
            {
                var operation = document.GetProperty("paths").GetProperty(path).GetProperty(method);
                Assert.That(operation.TryGetProperty("security", out var requirements), Is.True, $"{method} {path}");
                if (!operation.TryGetProperty("security", out requirements)) continue;
                Assert.That(requirements.GetArrayLength(), Is.EqualTo(1), $"{method} {path}: no anonymous alternative");
                foreach (var requirement in requirements.EnumerateArray())
                {
                    Assert.That(requirement.EnumerateObject().Select(p => p.Name), Is.EqualTo(new[] { "Bearer" }), $"{method} {path}: security must name the registered scheme, not {{}}");
                    if (requirement.TryGetProperty("Bearer", out var scopes))
                        Assert.That(scopes.GetArrayLength(), Is.Zero, $"{method} {path}: HTTP Bearer has no OAuth scopes");
                }
            }
        });
    }

    [Test]
    public async Task PublicOperations_RemainAnonymous_WithRegisteredHttpBearerScheme()
    {
        var document = await Json(await Expect(await Client.GetAsync("/swagger/v1/swagger.json"), 200));
        var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Multiple(() =>
        {
            Assert.That(scheme.GetProperty("type").GetString(), Is.EqualTo("http"));
            Assert.That(scheme.GetProperty("scheme").GetString(), Is.EqualTo("bearer"));
            Assert.That(scheme.GetProperty("bearerFormat").GetString(), Is.EqualTo("JWT"));
            Assert.That(!document.TryGetProperty("security", out var global) || global.GetArrayLength() == 0, Is.True);
            foreach (var route in new[] { "register", "login", "refresh", "logout", "password/forgot", "password/reset" })
            {
                var operation = document.GetProperty("paths").GetProperty("/api/v1/auth/" + route).GetProperty("post");
                Assert.That(!operation.TryGetProperty("security", out var security) || security.GetArrayLength() == 0, Is.True, route);
            }
            var health = document.GetProperty("paths").GetProperty("/api/v1/health").GetProperty("get");
            Assert.That(!health.TryGetProperty("security", out var healthSecurity) || healthSecurity.GetArrayLength() == 0, Is.True);
        });
    }
}
