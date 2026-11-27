using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Subscriptions.Application;
namespace Subscriptions.Api;
public record ErrorResponse(string Code,string Message,string CorrelationId,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] Dictionary<string,string[]>? Fields=null);
public record AllowedQuery(params string[] Names);
public static class Transport
{
    public static string Correlation(HttpContext ctx)=>ctx.Response.Headers["X-Correlation-ID"].ToString();
    public static Task Error(HttpContext ctx,int status,string code,string message,Dictionary<string,string[]>? fields=null)
    {
        ctx.Response.StatusCode=status;return ctx.Response.WriteAsJsonAsync(new ErrorResponse(code,message,Correlation(ctx),fields));
    }
    public static async Task Boundary(HttpContext ctx,RequestDelegate next)
    {
        var proposed=ctx.Request.Headers["X-Correlation-ID"].ToString();
        var id=proposed.Length is >0 and <=64 && proposed.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_')?proposed:Guid.NewGuid().ToString("N");
        ctx.Response.Headers["X-Correlation-ID"]=id;var timer=Stopwatch.StartNew();
        if(ctx.Request.Path.StartsWithSegments("/api/v1/auth"))ctx.Response.Headers.CacheControl="no-store";
        try { await next(ctx); }
        catch(BusinessFault fault)
        {
            if(fault.Kind==FaultKind.Validation && ctx.Items.TryGetValue("ipRetry",out var retry) && (int)retry!>0)fault=BusinessFault.Limited((int)retry!);
            var status=fault.Kind switch {FaultKind.Validation=>400,FaultKind.Unauthorized=>401,FaultKind.NotFound=>404,FaultKind.Conflict=>409,FaultKind.RateLimited=>429,_=>500};
            if(status==429)ctx.Response.Headers.RetryAfter=fault.RetryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if(status==401)ctx.Response.Headers.WWWAuthenticate="Bearer";
            await Error(ctx,status,fault.Code,fault.Message,fault.Fields);
        }
        catch(BadHttpRequestException fault)
        {
            if(ctx.Items.TryGetValue("ipRetry",out var retry) && (int)retry!>0) {ctx.Response.Headers.RetryAfter=retry!.ToString();await Error(ctx,429,"rate_limited","Too many attempts.");}
            else if(fault.StatusCode==413)await Error(ctx,413,"body_too_large","Request body exceeds 1 MiB.");
            else await Error(ctx,fault.StatusCode,fault.StatusCode==415?"unsupported_media_type":"validation","Invalid request.",fault.StatusCode==400?new(){["request"]=["Invalid JSON, identifier or query."]}:null);
        }
        catch(StorageUnavailableException) {await Error(ctx,503,"unavailable","Storage temporarily unavailable.");}
        catch(Exception) {await Error(ctx,500,"internal_error","Unexpected server error.");}
        finally
        {
            ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RequestAudit").LogInformation("{Route} {Status} {DurationMs}ms {CorrelationId}",
                (ctx.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched",ctx.Response.StatusCode,timer.Elapsed.TotalMilliseconds,id);
        }
    }
    public static async Task Validate(HttpContext ctx,RequestDelegate next)
    {
        if(!ctx.Request.Path.StartsWithSegments("/api/v1")){await next(ctx);return;}
        if(ctx.Request.Path=="/api/v1/auth/login" && ctx.Request.Method=="POST")ctx.Items["ipRetry"]=ctx.RequestServices.GetRequiredService<LoginIpLimiter>().Admit(ctx.Connection.RemoteIpAddress?.ToString()??"local");
        var names=ctx.GetEndpoint()?.Metadata.GetMetadata<AllowedQuery>()?.Names ?? [];
        if(ctx.Request.Query.Keys.Any(k=>!names.Contains(k,StringComparer.Ordinal)))throw BusinessFault.Invalid("query");
        if(ctx.Request.ContentLength>1048576)throw new BadHttpRequestException("Body too large.",413);
        if(ctx.Request.ContentLength is not 0)
        {
            var buffer=new byte[8192];using var body=new MemoryStream();int read;
            while((read=await ctx.Request.Body.ReadAsync(buffer))>0)
            {
                if(body.Length+read>1048576)throw new BadHttpRequestException("Body too large.",413);
                await body.WriteAsync(buffer.AsMemory(0,read));
            }
            if(body.Length>0)
            {
                if(!ctx.Request.HasJsonContentType())throw new BadHttpRequestException("JSON required.",415);
                body.Position=0;
                try
                {
                    using var document=await JsonDocument.ParseAsync(body);
                    if(document.RootElement.ValueKind!=JsonValueKind.Object)throw BusinessFault.Invalid("body");
                    if(ctx.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IAcceptsMetadata>()?.RequestType is null)throw BusinessFault.Invalid("body");
                    var properties=document.RootElement.EnumerateObject().ToArray();
                    if(properties.Any(x=>x.Value.ValueKind==JsonValueKind.Null)||properties.Select(x=>x.Name).Distinct().Count()!=properties.Length)throw BusinessFault.Invalid("body");
                    if(ctx.Request.Method=="PATCH" && properties.Length==0)throw BusinessFault.Invalid("body");
                }
                catch(JsonException){throw BusinessFault.Invalid("body");}
                ctx.Request.Body=new MemoryStream(body.ToArray());ctx.Request.ContentLength=body.Length;
            }
        }
        await next(ctx);
    }
}
public sealed class UtcJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)=>reader.GetDateTimeOffset().ToUniversalTime();
    public override void Write(Utf8JsonWriter writer,DateTimeOffset value,JsonSerializerOptions options)=>writer.WriteStringValue(value.UtcDateTime.ToString("O"));
}
