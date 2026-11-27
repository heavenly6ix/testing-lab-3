using Moq;
using NUnit.Framework;
using Subscriptions.Application;
using Subscriptions.Domain;
namespace Subscriptions.Application.Tests;
public class MemoryData : IData
{
    public Dictionary<(Type,Guid),Entity> Rows { get; set; }=[];
    public T? Get<T>(Guid id) where T:Entity => Rows.GetValueOrDefault((typeof(T),id)) as T;
    public IReadOnlyList<T> All<T>(Guid? owner=null) where T:Entity => Rows.Values.OfType<T>().Where(x=>owner is null || x.UserId==owner).ToList();
    public void Put<T>(T entity) where T:Entity => Rows[(typeof(T),entity.Id)]=entity;
    public T Transaction<T>(Func<IData,T> work)
    {
        var before=new Dictionary<(Type,Guid),Entity>(Rows);
        try { return work(this); } catch { Rows=before;throw; }
    }
}
public abstract class TestContext
{
    protected MemoryData Data=null!;
    protected Mock<IStore> Store=null!;
    protected Mock<IClock> Clock=null!;
    protected Mock<IPasswordHasher> Passwords=null!;
    protected Mock<ISecrets> Secrets=null!;
    protected Mock<ITokenIssuer> Issuer=null!;
    protected Mock<IEmailSender> Email=null!;
    protected DateTimeOffset Now;
    protected Guid User;
    protected List<(string Email,string Token,DateTimeOffset End)> Mails=null!;
    protected AuthService Auth=>new(Store.Object,Clock.Object,Passwords.Object,Secrets.Object,Issuer.Object,Email.Object);
    protected SubscriptionService Subs=>new(Store.Object,Clock.Object);
    protected PaymentService Payments=>new(Store.Object,Clock.Object);
    protected AnalyticsService Analytics=>new(Store.Object);
    protected ForecastService Forecast=>new(Store.Object,Clock.Object);
    protected static Credentials Credentials(string email="student@example.test",string password="password-12345")=>new() { Email=email,Password=password };
    protected SubscriptionInput Input()=>new() { Name=" Music ",Amount=10m,Currency="RUB",BillingPeriod="monthly",Category=" Media ",StartDate=new(2020,1,1),NextPaymentDate=new(2020,1,31) };
    protected PaymentInput PayInput(decimal amount=10,string status="successful")=>new() { Amount=amount,Currency="RUB",Date=DateOnly.FromDateTime(Now.UtcDateTime),Status=status };
    protected Subscription Sub()=>Subs.Create(User,Input());
    protected Payment Pay(Subscription? subscription=null)=>Payments.Create(User,(subscription??Sub()).Id,Guid.NewGuid().ToString(),PayInput());
    protected static BusinessFault Fault(TestDelegate action,FaultKind kind)
    {
        var fault=Assert.Throws<BusinessFault>(action)!;Assert.That(fault.Kind,Is.EqualTo(kind));return fault;
    }
    [SetUp] public void Initialize()
    {
        Data=new();Store=new(MockBehavior.Strict);Clock=new();Passwords=new();Secrets=new();Issuer=new();Email=new();Mails=[];
        Now=new(2026,11,21,12,0,0,TimeSpan.Zero);User=Guid.NewGuid();Clock.SetupGet(x=>x.UtcNow).Returns(()=>Now);
        Passwords.SetupGet(x=>x.DummyHash).Returns("dummy");Passwords.Setup(x=>x.Hash(It.IsAny<string>())).Returns((string s)=>"hash:"+s);
        Passwords.Setup(x=>x.Verify(It.IsAny<string>(),It.IsAny<string>())).Returns((string pwd,string hash)=>hash=="hash:"+pwd);
        Secrets.Setup(x=>x.New()).Returns(()=>Guid.NewGuid().ToString());Secrets.Setup(x=>x.Digest(It.IsAny<string>())).Returns((string s)=>"digest:"+s);
        Issuer.Setup(x=>x.Access(It.IsAny<Guid>(),It.IsAny<DateTimeOffset>(),It.IsAny<DateTimeOffset>())).Returns("jwt");
        Email.Setup(x=>x.SendReset(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<DateTimeOffset>())).Callback((string mail,string token,DateTimeOffset end)=>Mails.Add((mail,token,end)));
        Bind<UserView>();Bind<AuthAttempt>();Bind<TokenPair>();Bind<bool>();Bind<Subscription>();Bind<Payment>();Bind<Page<Subscription>>();Bind<Page<Payment>>();Bind<IReadOnlyList<AuditRecord>>();Bind<ExpensesResult>();Bind<IReadOnlyList<Subscription>>();
    }
    private void Bind<T>()
    {
        Store.Setup(x=>x.Read(It.IsAny<Func<IData,T>>())).Returns((Func<IData,T> work)=>work(Data));
        Store.Setup(x=>x.Write(It.IsAny<Func<IData,T>>())).Returns((Func<IData,T> work)=>Data.Transaction(work));
    }
}
