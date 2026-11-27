using Subscriptions.Domain;
namespace Subscriptions.Application;

public interface IClock { DateTimeOffset UtcNow { get; } }
public interface IData
{
    T? Get<T>(Guid id) where T : Entity;
    IReadOnlyList<T> All<T>(Guid? owner = null) where T : Entity;
    void Put<T>(T entity) where T : Entity;
}
public interface IStore
{
    T Read<T>(Func<IData, T> query);
    T Write<T>(Func<IData, T> work);
}
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
    string DummyHash { get; }
}
public interface ISecrets { string New(); string Digest(string secret); }
public interface ITokenIssuer { string Access(Guid userId, DateTimeOffset now, DateTimeOffset expiresAt); }
public interface IEmailSender { void SendReset(string email, string token, DateTimeOffset expiresAt); }
public class StorageUnavailableException : Exception
{
    public StorageUnavailableException(Exception inner) : base("Storage unavailable.", inner) { }
}
