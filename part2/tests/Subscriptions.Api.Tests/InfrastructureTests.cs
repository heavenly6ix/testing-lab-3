using NUnit.Framework;
using Subscriptions.Infrastructure;
using Subscriptions.Application;
using Subscriptions.Domain;
namespace Subscriptions.Api.Tests;
[TestFixture,NonParallelizable] public class InfrastructureTests
{
    [Test] public void Argon2idUsesDistinctRandomSixteenByteSaltAndVerifies()
    {
        var hasher=new ArgonPasswordHasher();var a=hasher.Hash("test-password");var b=hasher.Hash("test-password");Assert.That(a,Is.Not.EqualTo(b));
        Assert.That(Convert.FromBase64String(a.Split('$')[3]),Has.Length.EqualTo(16));Assert.That(hasher.Verify("test-password",a),Is.True);Assert.That(hasher.Verify("wrong",a),Is.False);
        Assert.That(hasher.Verify("test", "bad"),Is.False);Assert.That(hasher.Verify("test", "argon2id$v=19$m=65536,t=3,p=4$not-base64!$also-bad"),Is.False);
        Assert.That(hasher.Verify("test", "argon2id$v=19$m=65536,t=3,p=4$AA==$AA=="),Is.False);
    }
    [Test] public void CryptoSecretsAreRandomOpaqueAndOnlyDigestIsStable()
    {
        var secrets=new CryptoSecrets();var a=secrets.New();var b=secrets.New();Assert.That(a,Is.Not.EqualTo(b));Assert.That(Microsoft.IdentityModel.Tokens.Base64UrlEncoder.DecodeBytes(a),Has.Length.EqualTo(32));Assert.That(secrets.Digest(a),Is.EqualTo(secrets.Digest(a)));Assert.That(secrets.Digest(a),Is.Not.EqualTo(a));
    }
    [Test] public void UniqueConstraintIsRealAndDoesNotBecome503()
    {
        var folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());var store=new SqliteStore(Path.Combine(folder,"db.sqlite"));
        try
        {
            store.Write(d=>{d.Put(new UserAccount{Email="same@example.test",PasswordHash="hash"});return true;});
            var fault=Assert.Throws<BusinessFault>(()=>store.Write(d=>{d.Put(new UserAccount{Email="same@example.test",PasswordHash="other"});return true;}))!;
            Assert.That(fault.Kind,Is.EqualTo(FaultKind.Conflict));Assert.That(store.Read(d=>d.All<UserAccount>().Count),Is.EqualTo(1));
        }
        finally {Directory.Delete(folder,true);}
    }
    [Test] public void MailboxExpiresAndReplacesPerEmailWithoutPublicInbox()
    {
        var clock=new MutableClock();var box=new MemoryEmailSender(clock);box.SendReset("a","one",clock.UtcNow.AddMinutes(15));box.SendReset("a","two",clock.UtcNow.AddMinutes(15));Assert.That(box.Snapshot().Single().Token,Is.EqualTo("two"));Assert.That(box.Snapshot().Single().Link,Does.EndWith("#token=two"));clock.UtcNow=clock.UtcNow.AddMinutes(15);Assert.That(box.Snapshot(),Is.Empty);
    }
}
