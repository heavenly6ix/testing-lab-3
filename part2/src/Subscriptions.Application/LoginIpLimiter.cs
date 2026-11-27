namespace Subscriptions.Application;
// Local single-process rolling window; no distributed guarantee.
public sealed class LoginIpLimiter(IClock clock)
{
    private readonly Dictionary<string,Queue<DateTimeOffset>> attempts=new(StringComparer.Ordinal);
    private readonly object gate=new();
    public int Admit(string ip)
    {
        lock(gate)
        {
            var now=clock.UtcNow;var cutoff=now.AddMinutes(-15);
            foreach(var key in attempts.Keys.ToArray())
            {
                var q=attempts[key];while(q.TryPeek(out var time) && time<=cutoff)q.Dequeue();
                if(q.Count==0)attempts.Remove(key);
            }
            if(!attempts.TryGetValue(ip,out var bucket)) {bucket=new();attempts.Add(ip,bucket);}
            if(bucket.Count>=30)return Math.Max(1,(int)Math.Ceiling((bucket.Peek().AddMinutes(15)-now).TotalSeconds));
            bucket.Enqueue(now);return 0;
        }
    }
}
