using System.Net.Mail;
using Subscriptions.Domain;
namespace Subscriptions.Application;
public enum FaultKind { Validation, Unauthorized, NotFound, Conflict, RateLimited }
public class BusinessFault(FaultKind kind, string code, string message, Dictionary<string,string[]>? fields = null, int retryAfter = 0) : Exception(message)
{
    public FaultKind Kind { get; } = kind;
    public string Code { get; } = code;
    public Dictionary<string,string[]>? Fields { get; } = fields;
    public int RetryAfter { get; } = retryAfter;
    public static BusinessFault Invalid(string field) => new(FaultKind.Validation,"validation","Invalid request.",new() { [field] = ["Invalid or missing value."] });
    public static BusinessFault Missing() => new(FaultKind.NotFound,"not_found","Resource not found.");
    public static BusinessFault Unauthorized() => new(FaultKind.Unauthorized,"unauthorized","Invalid credentials or token.");
    public static BusinessFault Conflict(string code = "invalid_state") => new(FaultKind.Conflict,code,"Operation conflicts with current state.");
    public static BusinessFault Limited(int seconds) => new(FaultKind.RateLimited,"rate_limited","Too many attempts.",retryAfter:seconds);
}
public static class Rules
{
    public static readonly string[] Currencies = ["RUB","USD","EUR"];
    public static string Text(string? value, int limit, string field)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0 || text.Length > limit) throw BusinessFault.Invalid(field);
        return text;
    }
    public static string Email(string? value)
    {
        var email = Text(value,254,"email").ToLowerInvariant();
        if (!MailAddress.TryCreate(email,out var parsed) || parsed.Address != email || !email.Contains('@')) throw BusinessFault.Invalid("email");
        return email;
    }
    public static void Password(string? value)
    {
        if (value is null || value.Length < 12 || value.Length > 128) throw BusinessFault.Invalid("password");
    }
    public static void Amount(decimal amount)
    {
        if (amount <= 0 || amount > 999999999.99m || decimal.Round(amount,2) != amount) throw BusinessFault.Invalid("amount");
    }
    public static void Currency(string currency) { if (!Currencies.Contains(currency)) throw BusinessFault.Invalid("currency"); }
    public static void Period(string period) { if (period is not ("monthly" or "yearly")) throw BusinessFault.Invalid("billingPeriod"); }
    public static void PaymentStatus(string status) { if (status is not ("successful" or "failed")) throw BusinessFault.Invalid("status"); }
    public static void SubscriptionStatus(string? status) { if (status is not (null or "active" or "paused" or "cancelled")) throw BusinessFault.Invalid("status"); }
    public static T Owned<T>(IData data,Guid id,Guid user) where T:Entity
    {
        var result = data.Get<T>(id);
        if (result is null || result.UserId != user) throw BusinessFault.Missing();
        return result;
    }
    public static void Paging(int page,int size) { if (page < 1 || size < 1 || size > 100) throw BusinessFault.Invalid("pagination"); }
    public static Page<T> Page<T>(IEnumerable<T> items,int page,int size)
    {
        Paging(page,size); var list=items.ToList();
        var offset=(long)(page-1)*size;
        return new(offset >= list.Count ? [] : list.Skip((int)offset).Take(size).ToList(),list.Count,page,size);
    }
    public static void DateRange(DateOnly? from,DateOnly? to)
    {
        if (from > to) throw BusinessFault.Invalid("dateRange");
    }
    public static DateOnly Today(IClock clock) => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
    public static void PaymentDate(DateOnly date,IClock clock) { if (date > Today(clock)) throw BusinessFault.Invalid("date"); }
}
