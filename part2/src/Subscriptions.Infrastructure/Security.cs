using System.Security.Cryptography;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Konscious.Security.Cryptography;
using Subscriptions.Application;
namespace Subscriptions.Infrastructure;
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
public sealed class CryptoSecrets : ISecrets
{
    public string New()=>Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    public string Digest(string secret)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
public sealed class ArgonPasswordHasher : IPasswordHasher
{
    public string DummyHash { get; }
    public ArgonPasswordHasher() { DummyHash=Hash("dummy-password-not-an-account"); }
    public string Hash(string password)
    {
        var salt=RandomNumberGenerator.GetBytes(16);
        return "argon2id$v=19$m=65536,t=3,p=4$"+Convert.ToBase64String(salt)+"$"+Convert.ToBase64String(Derive(password,salt));
    }
    public bool Verify(string password,string hash)
    {
        try
        {
            var parts=hash.Split('$');
            if (parts.Length!=5 || parts[0]!="argon2id" || parts[1]!="v=19" || parts[2]!="m=65536,t=3,p=4") return false;
            var salt=Convert.FromBase64String(parts[3]);var expected=Convert.FromBase64String(parts[4]);
            if (salt.Length!=16 || expected.Length!=32) return false;
            return CryptographicOperations.FixedTimeEquals(expected,Derive(password,salt));
        }
        catch (FormatException) { return false; }
    }
    private static byte[] Derive(string password,byte[] salt)
    {
        using var algorithm=new Argon2id(Encoding.UTF8.GetBytes(password)) { Salt=salt,MemorySize=65536,Iterations=3,DegreeOfParallelism=4 };
        return algorithm.GetBytes(32);
    }
}
public record JwtSettings(string Key,string Issuer="subscriptions",string Audience="subscriptions-api");
public sealed class JwtIssuer(JwtSettings settings) : ITokenIssuer
{
    public string Access(Guid userId,DateTimeOffset now,DateTimeOffset expiresAt)
    {
        var credentials=new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),SecurityAlgorithms.HmacSha256);
        var token=new JwtSecurityToken(settings.Issuer,settings.Audience,[new Claim(JwtRegisteredClaimNames.Sub,userId.ToString()),new Claim(JwtRegisteredClaimNames.Jti,Guid.NewGuid().ToString())],now.UtcDateTime,expiresAt.UtcDateTime,credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
public record ResetMail(string Email,string Token,DateTimeOffset ExpiresAt)
{
    // Client UI is outside this backend; example link is only in the local test mailbox.
    public string Link => "https://client.example.test/reset-password#token="+Uri.EscapeDataString(Token);
}
public sealed class MemoryEmailSender(IClock clock) : IEmailSender
{
    private readonly List<ResetMail> messages=[];
    public void SendReset(string email,string token,DateTimeOffset expiresAt)
    {
        lock(messages) { messages.RemoveAll(x=>x.ExpiresAt<=clock.UtcNow || x.Email==email);messages.Add(new(email,token,expiresAt)); }
    }
    public IReadOnlyList<ResetMail> Snapshot() { lock(messages) return messages.Where(x=>x.ExpiresAt>clock.UtcNow).ToArray(); }
}
