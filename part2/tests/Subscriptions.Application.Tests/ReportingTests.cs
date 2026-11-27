using NUnit.Framework;
using Subscriptions.Application;
using Subscriptions.Domain;
namespace Subscriptions.Application.Tests;
[TestFixture] public class ReportingTests : TestContext
{
    private Payment Entry(DateOnly date,string currency="RUB",decimal amount=1,string status="successful",bool isVoided=false,string category="Media",Guid? owner=null,Guid? subscription=null)
    {
        var p=new Payment { UserId=owner??User,SubscriptionId=subscription??Guid.NewGuid(),Amount=amount,Currency=currency,Date=date,Status=status,IsVoided=isVoided,SnapshotCategory=category,SnapshotName="Past",CreatedAt=Now,UpdatedAt=Now };Data.Put(p);return p;
    }
    [Test] public void Expenses_EmptyReturnsZeroAndNoGroups()
    {
        var x=Analytics.Expenses(User,new(2024,1,1),new(2024,1,1));Assert.That(x.Totals.Values,Is.All.Zero);Assert.That(x.ByMonth,Is.Empty);Assert.That(x.ByCategory,Is.Empty);Assert.That(x.BySubscription,Is.Empty);
    }
    [Test] public void Expenses_ExactDatesCurrenciesStatusesSnapshotsAndOwners()
    {
        var start=new DateOnly(2024,2,1);var end=new DateOnly(2024,2,29);var sid=Guid.NewGuid();
        Entry(start,amount:.1m,subscription:sid);Entry(end,amount:.2m,subscription:sid);Entry(end,"USD",5,category:"Work");Entry(end,"EUR",7);
        Entry(start.AddDays(-1),amount:999);Entry(end.AddDays(1),amount:999);Entry(end,status:"failed");Entry(end,isVoided:true);Entry(end,owner:Guid.NewGuid());
        var x=Analytics.Expenses(User,start,end);Assert.That(x.Totals,Is.EquivalentTo(new Dictionary<string,decimal>{{"RUB",.3m},{"USD",5},{"EUR",7}}));Assert.That(x.ByMonth,Has.Count.EqualTo(3));
        Assert.That(x.BySubscription.Single(g=>g.Key==sid.ToString()).Amount,Is.EqualTo(.3m));Assert.That(x.ByCategory.Single(g=>g.Key=="Work").Amount,Is.EqualTo(5));
    }
    [Test] public void Expenses_YearMonthsAndCategoryCurrencyRemainDistinct()
    {
        Entry(new(2023,12,31));Entry(new(2024,1,1));Entry(new(2024,1,1),"USD",2);
        Assert.That(Analytics.Expenses(User,new(2023,12,31),new(2024,1,1)).ByMonth.Select(x=>x.Key),Is.EquivalentTo(new[]{"2023-12","2024-01","2024-01"}));
    }
    [TestCase("reverse")] [TestCase("large")]
    public void Expenses_InvalidRanges(string kind)
    {
        var from=new DateOnly(2024,1,1);var to=from;
        if(kind=="reverse")to=from.AddDays(-1);if(kind=="large")to=from.AddDays(366);if(kind=="missing-from")from=default;if(kind=="missing-to")to=default;
        Fault(()=>Analytics.Expenses(User,from,to),FaultKind.Validation);
    }
    [Test] public void Expenses_366InclusiveDaysAreAllowed()=>Assert.That(Analytics.Expenses(User,new(2024,1,1),new(2024,12,31)).Totals["RUB"],Is.Zero);
    private Subscription Schedule(DateOnly anchor,string period="monthly",string status="active",bool archived=false,Guid? owner=null)
    {
        var s=new Subscription{UserId=owner??User,Name="Music",Amount=5,Currency="RUB",BillingPeriod=period,Category="Media",StartDate=anchor,NextPaymentDate=anchor,OriginalBillingDay=anchor.Day,OriginalBillingMonth=anchor.Month,Status=status,Archived=archived};Data.Put(s);return s;
    }
    [TestCase(28,2023,2,28)] [TestCase(29,2023,2,28)] [TestCase(30,2023,2,28)] [TestCase(31,2023,2,28)]
    [TestCase(28,2024,2,28)] [TestCase(29,2024,2,29)] [TestCase(30,2024,2,29)] [TestCase(31,2024,2,29)]
    public void Forecast_FebruaryAnchors(int day,int year,int month,int expectedDay)
    {
        Now=new(year,month,1,0,0,0,TimeSpan.Zero);var schedule=Schedule(new(year,1,day));var result=Forecast.Forecast(User);
        Assert.That(result.Items.First().Date,Is.EqualTo(new DateOnly(year,month,expectedDay)));Assert.That(Data.Get<Subscription>(schedule.Id)!.OriginalBillingDay,Is.EqualTo(day));
    }
    [Test] public void Forecast_ClampDoesNotDriftAndMayContainTwoOccurrences()
    {
        var schedule=Schedule(new(2023,1,31));Now=new(2023,1,31,0,0,0,TimeSpan.Zero);Assert.That(Forecast.Forecast(User).Items.Select(x=>x.Date),Is.EqualTo(new[]{new DateOnly(2023,1,31),new DateOnly(2023,2,28)}));
        Now=new(2023,3,2,0,0,0,TimeSpan.Zero);Assert.That(Forecast.Forecast(User).Items.Single().Date,Is.EqualTo(new DateOnly(2023,3,31)));Assert.That(Data.Get<Subscription>(schedule.Id)!.NextPaymentDate,Is.EqualTo(new DateOnly(2023,1,31)));
    }
    [TestCase(2025,28)] [TestCase(2028,29)]
    public void Forecast_AnnualLeapAnchorReturnsOnLeapYear(int year,int day)
    {
        Schedule(new(2024,2,29),"yearly");Now=new(year,2,1,0,0,0,TimeSpan.Zero);Assert.That(Forecast.Forecast(User).Items.Single().Date,Is.EqualTo(new DateOnly(year,2,day)));
    }
    [Test] public void Forecast_YearBoundaryAndExclusiveEnd()
    {
        Now=new(2024,12,15,0,0,0,TimeSpan.Zero);Schedule(new(2023,1,1),"yearly");Schedule(new(2023,1,14));var result=Forecast.Forecast(User);
        Assert.That(result.ToExclusive,Is.EqualTo(new DateOnly(2025,1,14)));Assert.That(result.Items.Select(x=>x.Date),Is.EqualTo(new[]{new DateOnly(2025,1,1)}));
    }
    [Test] public void Forecast_OnlyOwnActiveUnarchivedAndFutureSchedules()
    {
        Now=new(2026,1,1,0,0,0,TimeSpan.Zero);Schedule(new(2020,1,2),status:"paused");Schedule(new(2020,1,2),status:"cancelled");Schedule(new(2020,1,2),archived:true);Schedule(new(2020,1,2),owner:Guid.NewGuid());Schedule(new(2030,1,2));
        Assert.That(Forecast.Forecast(User).Items,Is.Empty);Assert.That(Forecast.Forecast(User).Totals.Values,Is.All.Zero);
        var valid=Schedule(new(2020,1,2));Assert.That(Forecast.Forecast(User).Items.Single().SubscriptionId,Is.EqualTo(valid.Id));
    }
    [Test] public void Forecast_SkipEarlierDayThisMonthAndKeepCurrencies()
    {
        Now=new(2026,1,15,0,0,0,TimeSpan.Zero);var a=Schedule(new(2020,1,1));var b=Schedule(new(2020,1,20));Data.Put(b with{Currency="USD",Amount=3});
        var x=Forecast.Forecast(User);Assert.That(x.Items.Select(i=>i.Date),Is.EqualTo(new[]{new DateOnly(2026,1,20),new DateOnly(2026,2,1)}));Assert.That(x.Totals["RUB"],Is.EqualTo(5));Assert.That(x.Totals["USD"],Is.EqualTo(3));
    }
    [Test] public void Forecast_FutureAnnualBeyondWindowAndBeforeFirstOccurrence()
    {
        Now=new(2026,1,1,0,0,0,TimeSpan.Zero);Schedule(new(2030,2,28),"yearly");Schedule(new(2026,12,31),"yearly");Assert.That(Forecast.Forecast(User).Items,Is.Empty);
    }
}
