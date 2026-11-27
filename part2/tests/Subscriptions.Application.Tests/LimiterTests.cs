using NUnit.Framework;
using Subscriptions.Application;
namespace Subscriptions.Application.Tests;
[TestFixture] public class LimiterTests : TestContext
{
    [Test] public void ExactRollingWindowRetriesAndSeparatesIps()
    {
        var limiter=new LoginIpLimiter(Clock.Object);for(var i=0;i<30;i++)Assert.That(limiter.Admit("one"),Is.Zero);
        Assert.That(limiter.Admit("one"),Is.EqualTo(900));Assert.That(limiter.Admit("two"),Is.Zero);
        Now=Now.AddSeconds(899.5);Assert.That(limiter.Admit("one"),Is.EqualTo(1));Now=Now.AddSeconds(.5);Assert.That(limiter.Admit("one"),Is.Zero);Assert.That(limiter.Admit("two"),Is.Zero);
    }
    [Test] public async Task ParallelAdmissionsCannotExceedThirty()
    {
        var limiter=new LoginIpLimiter(Clock.Object);var results=await Task.WhenAll(Enumerable.Range(0,100).Select(_=>Task.Run(()=>limiter.Admit("one"))));Assert.That(results.Count(x=>x==0),Is.EqualTo(30));Assert.That(results.Count(x=>x==900),Is.EqualTo(70));
    }
    [Test] public void RollingWindowKeepsRecentEntryAfterOlderEntryExpires()
    {
        var limiter=new LoginIpLimiter(Clock.Object);limiter.Admit("one");Now=Now.AddMinutes(1);for(var i=0;i<29;i++)limiter.Admit("one");Now=Now.AddMinutes(14);Assert.That(limiter.Admit("one"),Is.Zero);Assert.That(limiter.Admit("one"),Is.EqualTo(60));
    }
    [Test] public void PasswordWhitespaceIsSignificantNotTrimmed()
    {
        var credentials=Credentials(password:new string(' ',12));Auth.Register(credentials);Assert.That(Auth.Login(credentials),Is.Not.Null);
    }
}
