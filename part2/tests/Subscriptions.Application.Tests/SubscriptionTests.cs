using NUnit.Framework;
using Subscriptions.Application;
using Subscriptions.Domain;
namespace Subscriptions.Application.Tests;
[TestFixture] public class SubscriptionTests : TestContext
{
    [Test] public void Create_TrimsAndCapturesAnchor()
    {
        var item=Sub();Assert.Multiple(()=> { Assert.That(item.Name,Is.EqualTo("Music"));Assert.That(item.Category,Is.EqualTo("Media"));Assert.That(item.UserId,Is.EqualTo(User));Assert.That(item.OriginalBillingDay,Is.EqualTo(31));Assert.That(item.OriginalBillingMonth,Is.EqualTo(1));Assert.That(item.Status,Is.EqualTo("active")); });
        Assert.That(Subs.Get(User,item.Id),Is.EqualTo(item));
    }
    [TestCase("name")] [TestCase("name-long")] [TestCase("category")] [TestCase("category-long")] [TestCase("amount-zero")] [TestCase("amount-negative")] [TestCase("amount-large")] [TestCase("amount-precision")] [TestCase("currency")] [TestCase("period")] [TestCase("dates")]
    public void InvalidCreationWritesNothing(string kind)
    {
        var input=kind switch {
            "name"=>Input() with {Name=" "},"name-long"=>Input() with {Name=new string('x',101)},"category"=>Input() with{Category=""},"category-long"=>Input() with{Category=new string('x',51)},
            "amount-zero"=>Input() with{Amount=0},"amount-negative"=>Input() with{Amount=-1},"amount-large"=>Input() with{Amount=1000000000m},"amount-precision"=>Input() with{Amount=1.001m},
            "currency"=>Input() with{Currency="GBP"},"period"=>Input() with{BillingPeriod="weekly"},"dates"=>Input() with{NextPaymentDate=new(2019,1,1)},_=>Input() with{StartDate=default}};
        Fault(()=>Subs.Create(User,input),FaultKind.Validation);Assert.That(Data.All<Subscription>(),Is.Empty);
    }
    [TestCase("missing")] [TestCase("foreign")]
    public void OwnershipAllSubscriptionOperations(string kind)
    {
        var item=Sub();var owner=kind=="foreign"?Guid.NewGuid():User;var id=kind=="missing"?Guid.NewGuid():item.Id;
        Fault(()=>Subs.Get(owner,id),FaultKind.NotFound);Fault(()=>Subs.Update(owner,id,new(){Name="x"}),FaultKind.NotFound);Fault(()=>Subs.ChangeStatus(owner,id,"paused"),FaultKind.NotFound);Fault(()=>Subs.Archive(owner,id),FaultKind.NotFound);
    }
    [Test] public void List_FiltersStablePaginationArchiveAndOwner()
    {
        var a=Sub();var b=Subs.Create(User,Input() with{Name="Other",Category="Other",NextPaymentDate=new(2021,1,1)});Subs.Create(Guid.NewGuid(),Input());Subs.ChangeStatus(User,b.Id,"paused");
        var page=Subs.List(User,pageSize:1);Assert.That(page.Total,Is.EqualTo(2));Assert.That(page.Items.Single().Id,Is.EqualTo(a.Id));Assert.That(Subs.List(User,status:"paused",category:" Other ").Items.Single().Id,Is.EqualTo(b.Id));
        Assert.That(Subs.List(User,category:"absent").Items,Is.Empty);Assert.That(Subs.List(User,page:int.MaxValue).Items,Is.Empty);
        Subs.Archive(User,a.Id);Subs.Archive(User,a.Id);Assert.That(Subs.List(User).Total,Is.EqualTo(1));Assert.That(Subs.List(User,includeArchived:true).Total,Is.EqualTo(2));Assert.That(Subs.Get(User,a.Id).Archived,Is.True);
    }
    [TestCase(0,20)] [TestCase(1,0)] [TestCase(1,101)]
    public void InvalidPagination(int page,int size)=>Fault(()=>Subs.List(User,page:page,pageSize:size),FaultKind.Validation);
    [Test] public void InvalidStatusFilter()=>Fault(()=>Subs.List(User,status:"unknown"),FaultKind.Validation);
    [Test] public void UpdateAllFieldsAndAnchor_PreservesHistoricalPayment()
    {
        var item=Sub();var pay=Pay(item);var next=Rules.Today(Clock.Object).AddDays(5);
        var updated=Subs.Update(User,item.Id,new(){Name=" New ",Category=" Work ",Currency="USD",Amount=20,BillingPeriod="yearly",NextPaymentDate=next});
        Assert.That(updated,Is.EqualTo(item with{Name="New",Category="Work",Currency="USD",Amount=20,BillingPeriod="yearly",NextPaymentDate=next,OriginalBillingDay=next.Day,OriginalBillingMonth=next.Month}));
        Assert.That(Payments.List(User).Items.Single(),Is.EqualTo(pay));
        var amountOnly=Subs.Update(User,item.Id,new(){Amount=30});Assert.That(amountOnly.OriginalBillingDay,Is.EqualTo(next.Day));
    }
    [TestCase("period")] [TestCase("today")] [TestCase("before-start")] [TestCase("amount")] [TestCase("currency")]
    public void InvalidUpdateDoesNotChangeSavedObject(string kind)
    {
        var item=Sub(); if(kind=="before-start") item=Subs.Create(User,Input() with {StartDate=Rules.Today(Clock.Object).AddDays(10),NextPaymentDate=Rules.Today(Clock.Object).AddDays(20)});
        var patch=kind switch {"period"=>new SubscriptionPatch{BillingPeriod="yearly"},"today"=>new(){NextPaymentDate=Rules.Today(Clock.Object)},"before-start"=>new(){NextPaymentDate=Rules.Today(Clock.Object).AddDays(1)},"amount"=>new(){Amount=-1},_=>new(){Currency="BAD"}};
        Fault(()=>Subs.Update(User,item.Id,patch),FaultKind.Validation);Assert.That(Subs.Get(User,item.Id),Is.EqualTo(item));
    }
    [Test] public void StatusTransitionsArePersistentAndIdempotent()
    {
        var item=Sub();foreach(var target in new[]{"active","paused","paused","active","cancelled","cancelled"})Assert.That(Subs.ChangeStatus(User,item.Id,target).Status,Is.EqualTo(target));
        Fault(()=>Subs.ChangeStatus(User,item.Id,"active"),FaultKind.Conflict);Fault(()=>Subs.Update(User,item.Id,new(){Amount=20}),FaultKind.Conflict);Fault(()=>Subs.ChangeStatus(User,item.Id,"wrong"),FaultKind.Validation);
    }
    [Test] public void ArchivedIsReadOnlyButHistoryStays()
    {
        var item=Sub();var pay=Pay(item);Subs.Archive(User,item.Id);Fault(()=>Subs.Update(User,item.Id,new(){Name="x"}),FaultKind.Conflict);Fault(()=>Subs.ChangeStatus(User,item.Id,"paused"),FaultKind.Conflict);
        Assert.That(Payments.List(User).Items.Single(),Is.EqualTo(pay));Assert.That(Analytics.Expenses(User,pay.Date,pay.Date).Totals["RUB"],Is.EqualTo(10));
    }
}
