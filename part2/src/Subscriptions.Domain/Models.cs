using System.Text.Json.Serialization;
namespace Subscriptions.Domain;

// Passive immutable persistence records. Business rules live in Application.
public abstract record Entity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    [JsonIgnore] public virtual string? UniqueKey => null;
}
public record UserAccount : Entity
{
    public required string Email { get; init; }
    public required string PasswordHash { get; init; }
    public override string UniqueKey => Email;
}
public record Session : Entity
{
    public Guid SessionId { get; init; }
    public required string Hash { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public bool Revoked { get; init; }
    public override string UniqueKey => Hash;
}
public record PasswordReset : Entity
{
    public required string Hash { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public bool Used { get; init; }
    public override string UniqueKey => Hash;
}
public record LoginFailures : Entity
{
    public required string Email { get; init; }
    public DateTimeOffset[] Attempts { get; init; } = [];
    public override string UniqueKey => Email;
}
public record Subscription : Entity
{
    public required string Name { get; init; }
    public decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string BillingPeriod { get; init; }
    public required string Category { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly NextPaymentDate { get; init; }
    public int OriginalBillingDay { get; init; }
    public int OriginalBillingMonth { get; init; }
    public string Status { get; init; } = "active";
    public bool Archived { get; init; }
}
public record Payment : Entity
{
    public Guid SubscriptionId { get; init; }
    public decimal Amount { get; init; }
    public required string Currency { get; init; }
    public DateOnly Date { get; init; }
    public required string Status { get; init; }
    public bool IsVoided { get; init; }
    public required string SnapshotName { get; init; }
    public required string SnapshotCategory { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
public record IdempotencyRecord : Entity
{
    public required string Key { get; init; }
    public Guid SubscriptionId { get; init; }
    public required string Payload { get; init; }
    public required Payment Response { get; init; }
    public override string UniqueKey => UserId + ":" + Key;
}
public record AuditRecord : Entity
{
    public Guid PaymentId { get; init; }
    public required string Action { get; init; }
    public DateTimeOffset At { get; init; }
    public Payment? Previous { get; init; }
    public required Payment Current { get; init; }
}
