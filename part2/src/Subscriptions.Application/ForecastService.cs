using Subscriptions.Domain;
namespace Subscriptions.Application;
public class ForecastService(IStore store,IClock clock)
{
    public ForecastResult Forecast(Guid user)
    {
        var from=Rules.Today(clock); var to=from.AddDays(30);
        var subscriptions=store.Read<IReadOnlyList<Subscription>>(data=>data.All<Subscription>(user));
        var items=new List<ForecastItem>();
        foreach (var item in subscriptions.Where(x=>x.Status=="active" && !x.Archived))
        {
            var monthly=item.BillingPeriod=="monthly";
            DateOnly cursor;
            if (monthly)
            {
                var months=Math.Max(0,(from.Year-item.NextPaymentDate.Year)*12+from.Month-item.NextPaymentDate.Month);
                cursor=new DateOnly(item.NextPaymentDate.Year,item.NextPaymentDate.Month,1).AddMonths(months);
            }
            else cursor=new DateOnly(Math.Max(from.Year,item.NextPaymentDate.Year),item.OriginalBillingMonth,1);
            while (cursor<to)
            {
                var day=Math.Min(item.OriginalBillingDay,DateTime.DaysInMonth(cursor.Year,cursor.Month));
                var date=new DateOnly(cursor.Year,cursor.Month,day);
                if (date>=to) break;
                if (date>=from && date>=item.StartDate && date>=item.NextPaymentDate)
                    items.Add(new ForecastItem(date,item.Id,item.Name,item.Amount,item.Currency));
                cursor=monthly?cursor.AddMonths(1):cursor.AddYears(1);
            }
        }
        var ordered=items.OrderBy(x=>x.Date).ThenBy(x=>x.SubscriptionId).ToList();
        return new(from,to,ordered,MoneyTotals.Sum(ordered.Select(x=>(x.Currency,x.Amount))));
    }
}
