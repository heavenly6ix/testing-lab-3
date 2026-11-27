using Subscriptions.Domain;
namespace Subscriptions.Application;

public class AuthService(IStore store,IClock clock,IPasswordHasher passwords,ISecrets secrets,ITokenIssuer issuer,IEmailSender emailSender)
{
    public UserView Register(Credentials input)
    {
        var email=Rules.Email(input.Email); Rules.Password(input.Password);
        var hash=passwords.Hash(input.Password);
        return store.Write(data =>
        {
            if (data.All<UserAccount>().Any(x=>x.Email==email)) throw BusinessFault.Conflict("duplicate_email");
            var id=Guid.NewGuid();
            data.Put(new UserAccount { Id=id,UserId=id,Email=email,PasswordHash=hash });
            return new UserView(id,email);
        });
    }
    public TokenPair Login(Credentials input,int ipRetryAfter=0)
    {
        var email=Rules.Email(input.Email);
        if (string.IsNullOrEmpty(input.Password) || input.Password.Length>128) throw BusinessFault.Invalid("password");
        var now=clock.UtcNow;
        // Return the unsuccessful outcome; throwing inside Write would roll back failure timestamps.
        var outcome=store.Write(data =>
        {
            var failures=data.All<LoginFailures>().FirstOrDefault(x=>x.Email==email)
                ?? new LoginFailures { Email=email };
            var recent=failures.Attempts.Where(t=>t>now.AddMinutes(-15)).ToArray();
            var accountRetry=recent.Length>=5 ? (int)Math.Ceiling((recent[0].AddMinutes(15)-now).TotalSeconds) : 0;
            var retry=Math.Max(accountRetry,ipRetryAfter);
            if (retry>0) return new AuthAttempt(null,retry);
            var user=data.All<UserAccount>().FirstOrDefault(x=>x.Email==email);
            var matches=passwords.Verify(input.Password,user?.PasswordHash ?? passwords.DummyHash);
            if (user is null || !matches)
            {
                data.Put(failures with { Attempts=[..recent,now] });
                return new AuthAttempt(null);
            }
            return new AuthAttempt(NewSession(data,user.Id,Guid.NewGuid(),now.AddDays(30),now));
        });
        if (outcome.RetryAfter>0) throw BusinessFault.Limited(outcome.RetryAfter);
        return outcome.Tokens ?? throw BusinessFault.Unauthorized();
    }
    private TokenPair NewSession(IData data,Guid user,Guid sessionId,DateTimeOffset end,DateTimeOffset now)
    {
        var raw=secrets.New();
        data.Put(new Session { UserId=user,SessionId=sessionId,Hash=secrets.Digest(raw),ExpiresAt=end });
        var accessEnd=DateTimeOffset.FromUnixTimeSeconds(now.AddMinutes(15).ToUnixTimeSeconds());
        return new(issuer.Access(user,now,accessEnd),raw,accessEnd,end);
    }
    public TokenPair Refresh(string raw)
    {
        Rules.Text(raw,256,"refreshToken"); var hash=secrets.Digest(raw); var now=clock.UtcNow;
        return store.Write(data=>
        {
            var session=data.All<Session>().FirstOrDefault(x=>x.Hash==hash);
            if (session is null || session.Revoked || session.ExpiresAt<=now) throw BusinessFault.Unauthorized();
            data.Put(session with { Revoked=true });
            return NewSession(data,session.UserId,session.SessionId,session.ExpiresAt,now);
        });
    }
    public void Logout(string raw)
    {
        Rules.Text(raw,256,"refreshToken"); var hash=secrets.Digest(raw);
        store.Write(data=>
        {
            var token=data.All<Session>().FirstOrDefault(x=>x.Hash==hash);
            if (token is not null)
                foreach (var session in data.All<Session>(token.UserId).Where(x=>x.SessionId==token.SessionId))
                    data.Put(session with { Revoked=true });
            return true;
        });
    }
    public void Forgot(string input)
    {
        var email=Rules.Email(input); var raw=secrets.New(); var end=clock.UtcNow.AddMinutes(15);
        var shouldSend=store.Write(data=>
        {
            var user=data.All<UserAccount>().FirstOrDefault(x=>x.Email==email);
            if (user is null) return false;
            foreach (var old in data.All<PasswordReset>(user.Id)) data.Put(old with { Used=true });
            data.Put(new PasswordReset { UserId=user.Id,Hash=secrets.Digest(raw),ExpiresAt=end });
            return true;
        });
        if (shouldSend) emailSender.SendReset(email,raw,end);
    }
    public void Reset(ResetRequest input)
    {
        Rules.Password(input.Password); Rules.Text(input.Token,256,"token");
        var hash=secrets.Digest(input.Token); var now=clock.UtcNow;
        store.Write(data=>
        {
            var reset=data.All<PasswordReset>().FirstOrDefault(x=>x.Hash==hash);
            if (reset is null || reset.Used || reset.ExpiresAt<=now) throw BusinessFault.Invalid("token");
            var user=data.Get<UserAccount>(reset.UserId) ?? throw BusinessFault.Invalid("token");
            data.Put(user with { PasswordHash=passwords.Hash(input.Password) });
            data.Put(reset with { Used=true });
            foreach (var session in data.All<Session>(user.Id)) data.Put(session with { Revoked=true });
            return true;
        });
    }
}
