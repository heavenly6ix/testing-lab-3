namespace Subscriptions.Application;
public record Credentials { public required string Email { get; init; } public required string Password { get; init; } }
public record RefreshRequest { public required string RefreshToken { get; init; } }
public record ForgotRequest { public required string Email { get; init; } }
public record ResetRequest { public required string Token { get; init; } public required string Password { get; init; } }
public record UserView(Guid Id, string Email);
public record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt, DateTimeOffset RefreshExpiresAt);
public record AuthAttempt(TokenPair? Tokens, int RetryAfter = 0);
public record SubscriptionInput
{
    public required string Name { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string BillingPeriod { get; init; }
    public required string Category { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly NextPaymentDate { get; init; }
}
public record SubscriptionPatch
{
    public string? Name { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public string? BillingPeriod { get; init; }
    public string? Category { get; init; }
    public DateOnly? NextPaymentDate { get; init; }
}
public record PaymentInput
{
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required DateOnly Date { get; init; }
    public required string Status { get; init; }
}
public record PaymentPatch { public decimal? Amount { get; init; } public DateOnly? Date { get; init; } public string? Status { get; init; } }
public record Page<T>(IReadOnlyList<T> Items, int Total, [property: System.Text.Json.Serialization.JsonPropertyName("page")] int PageNumber, int PageSize);
public record Breakdown(string Key, string Currency, decimal Amount);
public record ExpensesResult(Dictionary<string, decimal> Totals, IReadOnlyList<Breakdown> ByMonth, IReadOnlyList<Breakdown> ByCategory, IReadOnlyList<Breakdown> BySubscription);
public record ForecastItem(DateOnly Date, Guid SubscriptionId, string Name, decimal Amount, string Currency);
public record ForecastResult(DateOnly From, DateOnly ToExclusive, IReadOnlyList<ForecastItem> Items, Dictionary<string, decimal> Totals);
