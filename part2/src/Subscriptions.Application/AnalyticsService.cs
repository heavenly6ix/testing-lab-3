using Subscriptions.Domain;
namespace Subscriptions.Application;
public class AnalyticsService(IStore store)
{
    public ExpensesResult Expenses(Guid user,DateOnly from,DateOnly to)
    {
        Rules.DateRange(from,to);
        if (to.DayNumber-from.DayNumber+1>366) throw BusinessFault.Invalid("dateRange");
        return store.Read(data=>
        {
            var payments=data.All<Payment>(user).Where(x=>x.Status=="successful" && !x.IsVoided && x.Date>=from && x.Date<=to).ToList();
            return new ExpensesResult(MoneyTotals.Sum(payments.Select(x=>(x.Currency,x.Amount))),
                Group(payments,x=>x.Date.ToString("yyyy-MM",System.Globalization.CultureInfo.InvariantCulture)),
                Group(payments,x=>x.SnapshotCategory),Group(payments,x=>x.SubscriptionId.ToString()));
        });
    }
    private static IReadOnlyList<Breakdown> Group(IEnumerable<Payment> payments,Func<Payment,string> key) =>
        payments.GroupBy(x=>(Key:key(x),x.Currency)).OrderBy(x=>x.Key.Key,StringComparer.Ordinal).ThenBy(x=>x.Key.Currency,StringComparer.Ordinal)
            .Select(x=>new Breakdown(x.Key.Key,x.Key.Currency,x.Sum(p=>p.Amount))).ToList();
}
public static class MoneyTotals
{
    public static Dictionary<string,decimal> Sum(IEnumerable<(string Currency,decimal Amount)> values)
    {
        var result=Rules.Currencies.ToDictionary(x=>x,_=>0m,StringComparer.Ordinal);
        foreach (var (currency,amount) in values) result[currency]+=amount;
        return result;
    }
}
