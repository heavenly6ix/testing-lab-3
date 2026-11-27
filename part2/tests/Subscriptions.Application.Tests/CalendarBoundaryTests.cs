using NUnit.Framework;
using Subscriptions.Application;
namespace Subscriptions.Application.Tests;
[TestFixture] public class CalendarBoundaryTests : TestContext
{
    [TestCase("subscription")] [TestCase("payment")] [TestCase("analytics")]
    public void ValidMinimumCalendarDateIsNotMissingInput(string operation)
    {
        var min=DateOnly.MinValue;
        if(operation=="subscription")Assert.That(Subs.Create(User,Input() with{StartDate=min,NextPaymentDate=min}).NextPaymentDate,Is.EqualTo(min));
        if(operation=="payment")Assert.That(Payments.Create(User,Sub().Id,"min-date",PayInput() with{Date=min}).Date,Is.EqualTo(min));
        if(operation=="analytics")Assert.That(Analytics.Expenses(User,min,min).Totals.Values,Is.All.Zero);
    }
}
