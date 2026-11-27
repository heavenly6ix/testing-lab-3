using Moq;
using NUnit.Framework;
using Subscriptions.Application;
using Subscriptions.Domain;
namespace Subscriptions.Application.Tests;
[TestFixture] public class PaymentTests : TestContext
{
    [Test] public void CreateAtomicTripletAndOriginalResponseReplay()
    {
        var item=Sub();var first=Payments.Create(User,item.Id,"key",PayInput());
        Assert.That(Payments.Create(User,item.Id,"key",PayInput() with{Amount=10.00m}),Is.EqualTo(first));
        Assert.That(Data.All<Payment>(),Has.Count.EqualTo(1));Assert.That(Data.All<IdempotencyRecord>(),Has.Count.EqualTo(1));var audit=Payments.Audit(User,first.Id).Single();
        Assert.That(audit.Previous,Is.Null);Assert.That(audit.Current,Is.EqualTo(first));Assert.That(audit.UserId,Is.EqualTo(User));Assert.That(audit.At,Is.EqualTo(Now));
        Now=Now.AddMinutes(1);Payments.Update(User,first.Id,new(){Amount=20});Payments.Void(User,first.Id);Subs.Update(User,item.Id,new(){Currency="USD",Category="New"});Subs.Archive(User,item.Id);
        Assert.That(Payments.Create(User,item.Id,"key",PayInput()),Is.EqualTo(first));
    }
    [Test] public void DifferentKeyCreatesNew_ChangedPayloadConflicts()
    {
        var sub=Sub();Payments.Create(User,sub.Id,"key",PayInput());Payments.Create(User,sub.Id,"other",PayInput());
        Assert.That(Fault(()=>Payments.Create(User,sub.Id,"key",PayInput(20)),FaultKind.Conflict).Code,Is.EqualTo("idempotency_conflict"));
        Fault(()=>Payments.Create(User,Sub().Id,"key",PayInput()),FaultKind.Conflict);Assert.That(Data.All<Payment>(),Has.Count.EqualTo(2));
    }
    [Test] public void ChangedFractionalPayloadMustNotBecomeReplay()
    {
        var sub=Sub();Payments.Create(User,sub.Id,"key",PayInput());Fault(()=>Payments.Create(User,sub.Id,"key",PayInput(10.001m)),FaultKind.Conflict);
    }
    [TestCase(null)] [TestCase("")] [TestCase("has space")] [TestCase("ключ")]
    public void InvalidKeys(string? key)=>Fault(()=>Payments.Create(User,Sub().Id,key,PayInput()),FaultKind.Validation);
    [Test] public void TooLongKey()=>Fault(()=>Payments.Create(User,Sub().Id,new string('a',129),PayInput()),FaultKind.Validation);
    [TestCase("amount")] [TestCase("currency")] [TestCase("mismatch")] [TestCase("status")] [TestCase("future")]
    public void InvalidCreateLeavesNoTriplet(string kind)
    {
        var input=kind switch {"amount"=>PayInput(-1),"currency"=>PayInput() with{Currency="GBP"},"mismatch"=>PayInput() with{Currency="USD"},"status"=>PayInput(status:"bad"),"future"=>PayInput() with{Date=Rules.Today(Clock.Object).AddDays(1)},_=>PayInput() with{Date=default}};
        Fault(()=>Payments.Create(User,Sub().Id,"key",input),FaultKind.Validation);Assert.That(Data.All<Payment>(),Is.Empty);Assert.That(Data.All<AuditRecord>(),Is.Empty);Assert.That(Data.All<IdempotencyRecord>(),Is.Empty);
    }
    [TestCase("foreign")] [TestCase("missing")]
    public void OwnershipAppliesToCreateFilterUpdateVoidAudit(string kind)
    {
        var sub=Sub();var pay=Pay(sub);var owner=kind=="foreign"?Guid.NewGuid():User;var sid=kind=="missing"?Guid.NewGuid():sub.Id;var pid=kind=="missing"?Guid.NewGuid():pay.Id;
        Fault(()=>Payments.Create(owner,sid,"key",PayInput()),FaultKind.NotFound);Fault(()=>Payments.List(owner,subscriptionId:sid),FaultKind.NotFound);Fault(()=>Payments.Update(owner,pid,new(){Amount=1}),FaultKind.NotFound);Fault(()=>Payments.Void(owner,pid),FaultKind.NotFound);Fault(()=>Payments.Audit(owner,pid),FaultKind.NotFound);
    }
    [Test] public void CorrectionPreservesCurrencyAndSnapshotsAuditPrevious()
    {
        var sub=Sub();var old=Pay(sub);Subs.Update(User,sub.Id,new(){Currency="USD",Name="New",Category="New"});Now=Now.AddDays(1);
        var updated=Payments.Update(User,old.Id,new(){Amount=0.30m,Date=old.Date.AddDays(-1),Status="failed"});
        Assert.That(updated.Currency,Is.EqualTo("RUB"));Assert.That(updated.SnapshotName,Is.EqualTo("Music"));Assert.That(updated.SnapshotCategory,Is.EqualTo("Media"));
        var audit=Payments.Audit(User,old.Id).Single(x=>x.Action=="correct");Assert.That(audit.Previous,Is.EqualTo(old));Assert.That(audit.Current,Is.EqualTo(updated));Assert.That(updated.UpdatedAt,Is.EqualTo(Now));
        var voided=Payments.Void(User,old.Id);Assert.That(Payments.Void(User,old.Id),Is.EqualTo(voided));Assert.That(Payments.Audit(User,old.Id),Has.Count.EqualTo(3));Fault(()=>Payments.Update(User,old.Id,new(){Amount=3}),FaultKind.Conflict);
    }
    [TestCase("amount")] [TestCase("status")] [TestCase("date")]
    public void InvalidCorrectionRollsBack(string kind)
    {
        var old=Pay();var patch=kind switch {"amount"=>new PaymentPatch{Amount=0},"status"=>new(){Status="x"},_=>new(){Date=Rules.Today(Clock.Object).AddDays(1)}};
        Fault(()=>Payments.Update(User,old.Id,patch),FaultKind.Validation);Assert.That(Payments.List(User).Items.Single(),Is.EqualTo(old));Assert.That(Payments.Audit(User,old.Id),Has.Count.EqualTo(1));
    }
    [Test] public void HistoryFiltersPagesVoidsAndOwnOnly()
    {
        var sub=Sub();var a=Pay(sub);var b=Payments.Create(User,sub.Id,"failed",PayInput(status:"failed") with{Date=a.Date.AddDays(-1)});var foreign=Subs.Create(Guid.NewGuid(),Input());Payments.Create(foreign.UserId,foreign.Id,"key",PayInput());Payments.Void(User,a.Id);Subs.Archive(User,sub.Id);
        Assert.That(Payments.List(User).Total,Is.EqualTo(2));Assert.That(Payments.List(User,sub.Id,"failed",a.Date.AddDays(-1),a.Date.AddDays(-1)).Items.Single().Id,Is.EqualTo(b.Id));
        Assert.That(Payments.List(User,from:a.Date).Items.Single().IsVoided,Is.True);Assert.That(Payments.List(User,to:b.Date).Items.Single().Id,Is.EqualTo(b.Id));
        Assert.That(Payments.List(User,pageSize:1).Items.Single().Id,Is.EqualTo(a.Id));Assert.That(Payments.List(User,page:2,pageSize:1).Items.Single().Id,Is.EqualTo(b.Id));Assert.That(Payments.List(User,page:3,pageSize:1).Items,Is.Empty);
    }
    [Test] public void HistoryInvalidFilters()
    {
        Fault(()=>Payments.List(User,status:"bad"),FaultKind.Validation);Fault(()=>Payments.List(User,from:new(2026,2,2),to:new(2026,1,1)),FaultKind.Validation);Fault(()=>Payments.List(User,pageSize:101),FaultKind.Validation);
    }
    [Test] public void TransactionPortFailureIsPropagatedWithoutClaimingSuccess()
    {
        var sub=Sub();Store.Setup(x=>x.Write(It.IsAny<Func<IData,Payment>>())).Throws(new StorageUnavailableException(new IOException("test")));
        Assert.Throws<StorageUnavailableException>(()=>Payments.Create(User,sub.Id,"key",PayInput()));Assert.That(Data.All<Payment>(),Is.Empty);
    }
}
