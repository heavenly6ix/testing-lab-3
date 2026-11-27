using System.Text.Json;
using Subscriptions.Domain;
namespace Subscriptions.Application;
public class PaymentService(IStore store,IClock clock)
{
    public Payment Create(Guid user,Guid subscriptionId,string? key,PaymentInput input)
    {
        if (string.IsNullOrEmpty(key) || key.Length>128 || key.Any(c=>c<'!' || c>'~')) throw BusinessFault.Invalid("idempotencyKey");
        // Canonical typed values: decimal numeric equality ignores insignificant trailing zeros.
        var payload=JsonSerializer.Serialize(new { subscriptionId,amount=input.Amount.ToString("G29",System.Globalization.CultureInfo.InvariantCulture),input.Currency,input.Date,input.Status });
        return store.Write(data=>
        {
            var subscription=Rules.Owned<Subscription>(data,subscriptionId,user);
            var previous=data.All<IdempotencyRecord>(user).FirstOrDefault(x=>x.Key==key);
            if (previous is not null)
            {
                if (previous.Payload!=payload) throw BusinessFault.Conflict("idempotency_conflict");
                return previous.Response;
            }
            Rules.Amount(input.Amount); Rules.Currency(input.Currency); Rules.PaymentStatus(input.Status); Rules.PaymentDate(input.Date,clock);
            if (input.Currency!=subscription.Currency) throw BusinessFault.Invalid("currency");
            var now=clock.UtcNow;
            var item=new Payment { UserId=user,SubscriptionId=subscriptionId,Amount=input.Amount,Currency=input.Currency,Date=input.Date,Status=input.Status,
                SnapshotName=subscription.Name,SnapshotCategory=subscription.Category,CreatedAt=now,UpdatedAt=now };
            data.Put(item);
            data.Put(new IdempotencyRecord { UserId=user,Key=key,SubscriptionId=subscriptionId,Payload=payload,Response=item });
            AddAudit(data,item,null,"create");return item;
        });
    }
    public Page<Payment> List(Guid user,Guid? subscriptionId=null,string? status=null,DateOnly? from=null,DateOnly? to=null,int page=1,int pageSize=20)
    {
        if (status is not null) Rules.PaymentStatus(status); Rules.DateRange(from,to); Rules.Paging(page,pageSize);
        return store.Read(data=>
        {
            if (subscriptionId is { } id) Rules.Owned<Subscription>(data,id,user);
            return Rules.Page(data.All<Payment>(user).Where(x=>subscriptionId is null || x.SubscriptionId==subscriptionId)
                .Where(x=>status is null || x.Status==status).Where(x=>from is null || x.Date>=from).Where(x=>to is null || x.Date<=to)
                .OrderByDescending(x=>x.Date).ThenBy(x=>x.Id),page,pageSize);
        });
    }
    public Payment Update(Guid user,Guid id,PaymentPatch input) => store.Write(data=>
    {
        var old=Rules.Owned<Payment>(data,id,user);
        if (old.IsVoided) throw BusinessFault.Conflict();
        var item=old with { Amount=input.Amount ?? old.Amount,Date=input.Date ?? old.Date,Status=input.Status ?? old.Status,UpdatedAt=clock.UtcNow };
        Rules.Amount(item.Amount); Rules.PaymentStatus(item.Status); Rules.PaymentDate(item.Date,clock);
        data.Put(item);AddAudit(data,item,old,"correct");return item;
    });
    public Payment Void(Guid user,Guid id) => store.Write(data=>
    {
        var old=Rules.Owned<Payment>(data,id,user);
        if (old.IsVoided) return old;
        var item=old with { IsVoided=true,UpdatedAt=clock.UtcNow };data.Put(item);AddAudit(data,item,old,"void");return item;
    });
    public IReadOnlyList<AuditRecord> Audit(Guid user,Guid id) => store.Read<IReadOnlyList<AuditRecord>>(data=>
    {
        Rules.Owned<Payment>(data,id,user);
        return data.All<AuditRecord>(user).Where(x=>x.PaymentId==id).OrderBy(x=>x.At).ThenBy(x=>x.Id).ToList();
    });
    private void AddAudit(IData data,Payment current,Payment? previous,string action) => data.Put(new AuditRecord
        { UserId=current.UserId,PaymentId=current.Id,Action=action,At=clock.UtcNow,Previous=previous,Current=current });
}
