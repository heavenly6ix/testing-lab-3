using Subscriptions.Domain;
namespace Subscriptions.Application;
public class SubscriptionService(IStore store,IClock clock)
{
    public Subscription Create(Guid user,SubscriptionInput input)
    {
        var name=Rules.Text(input.Name,100,"name"); var category=Rules.Text(input.Category,50,"category");
        Rules.Amount(input.Amount); Rules.Currency(input.Currency); Rules.Period(input.BillingPeriod);
        if (input.NextPaymentDate<input.StartDate) throw BusinessFault.Invalid("dates");
        var item=new Subscription { UserId=user,Name=name,Amount=input.Amount,Currency=input.Currency,BillingPeriod=input.BillingPeriod,Category=category,
            StartDate=input.StartDate,NextPaymentDate=input.NextPaymentDate,OriginalBillingDay=input.NextPaymentDate.Day,OriginalBillingMonth=input.NextPaymentDate.Month };
        return store.Write(data=> {data.Put(item);return item;});
    }
    public Subscription Get(Guid user,Guid id) => store.Read(data=>Rules.Owned<Subscription>(data,id,user));
    public Page<Subscription> List(Guid user,string? status=null,string? category=null,bool includeArchived=false,int page=1,int pageSize=20)
    {
        Rules.SubscriptionStatus(status); Rules.Paging(page,pageSize);
        return store.Read(data=>Rules.Page(data.All<Subscription>(user)
            .Where(x=>includeArchived || !x.Archived).Where(x=>status is null || x.Status==status)
            .Where(x=>category is null || x.Category==category.Trim()).OrderBy(x=>x.NextPaymentDate).ThenBy(x=>x.Id),page,pageSize));
    }
    public Subscription Update(Guid user,Guid id,SubscriptionPatch input) => store.Write(data=>
    {
        var old=Rules.Owned<Subscription>(data,id,user);
        if (old.Archived || old.Status=="cancelled") throw BusinessFault.Conflict();
        if (input.BillingPeriod is not null && input.BillingPeriod!=old.BillingPeriod && input.NextPaymentDate is null) throw BusinessFault.Invalid("nextPaymentDate");
        if (input.NextPaymentDate is { } date && (date<=Rules.Today(clock) || date<old.StartDate)) throw BusinessFault.Invalid("nextPaymentDate");
        var item=old with { Name=input.Name is null?old.Name:Rules.Text(input.Name,100,"name"),Amount=input.Amount ?? old.Amount,
            Currency=input.Currency ?? old.Currency,BillingPeriod=input.BillingPeriod ?? old.BillingPeriod,
            Category=input.Category is null?old.Category:Rules.Text(input.Category,50,"category"),NextPaymentDate=input.NextPaymentDate ?? old.NextPaymentDate,
            OriginalBillingDay=input.NextPaymentDate?.Day ?? old.OriginalBillingDay,OriginalBillingMonth=input.NextPaymentDate?.Month ?? old.OriginalBillingMonth };
        Rules.Amount(item.Amount); Rules.Currency(item.Currency); Rules.Period(item.BillingPeriod);
        data.Put(item); return item;
    });
    public Subscription ChangeStatus(Guid user,Guid id,string target) => store.Write(data=>
    {
        Rules.SubscriptionStatus(target);
        var old=Rules.Owned<Subscription>(data,id,user);
        if (old.Archived || (old.Status=="cancelled" && target!="cancelled")) throw BusinessFault.Conflict();
        var item=old with { Status=target }; data.Put(item);return item;
    });
    public void Archive(Guid user,Guid id) => store.Write(data=>
    {
        var old=Rules.Owned<Subscription>(data,id,user); data.Put(old with { Archived=true });return true;
    });
}
