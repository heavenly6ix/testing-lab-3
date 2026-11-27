using System.Security.Claims;
using Subscriptions.Application;
using Subscriptions.Domain;
namespace Subscriptions.Api;
public static class ApiRoutes
{
    private static Guid User(HttpContext context)=>Guid.Parse(context.User.FindFirstValue("sub")!);
    private const string RecoveryMessage="If the account exists, recovery instructions have been sent.";
    public static void MapApi(WebApplication app)
    {
        var auth=app.MapGroup("/api/v1/auth").WithTags("Auth");
        auth.MapPost("/register",(Credentials input,AuthService svc)=>{var user=svc.Register(input);return Results.Json(user,statusCode:201);}).Produces<UserView>(201);
        auth.MapPost("/login",(Credentials input,AuthService svc,HttpContext ctx)=>svc.Login(input,(int)(ctx.Items["ipRetry"]??0))).Produces<TokenPair>();
        auth.MapPost("/refresh",(RefreshRequest input,AuthService svc)=>svc.Refresh(input.RefreshToken)).Produces<TokenPair>();
        auth.MapPost("/logout",(RefreshRequest input,AuthService svc)=>{svc.Logout(input.RefreshToken);return Results.NoContent();}).Produces(204);
        auth.MapPost("/password/forgot",(ForgotRequest input,AuthService svc)=>{svc.Forgot(input.Email);return Results.Json(new{message=RecoveryMessage},statusCode:202);}).Produces<RecoveryResponse>(202);
        auth.MapPost("/password/reset",(ResetRequest input,AuthService svc)=>{svc.Reset(input);return Results.NoContent();}).Produces(204);
        var subs=app.MapGroup("/api/v1/subscriptions").RequireAuthorization().WithTags("Subscriptions");
        subs.MapPost("",(SubscriptionInput input,SubscriptionService svc,HttpContext ctx)=>{var s=svc.Create(User(ctx),input);return Results.Created($"/api/v1/subscriptions/{s.Id}",s);}).Produces<Subscription>(201);
        subs.MapGet("",(SubscriptionService svc,HttpContext ctx,string? status,string? category,bool includeArchived=false,int page=1,int pageSize=20)=>svc.List(User(ctx),status,category,includeArchived,page,pageSize))
            .WithMetadata(new AllowedQuery("status","category","includeArchived","page","pageSize")).Produces<Page<Subscription>>();
        subs.MapGet("/{id}",(Guid id,SubscriptionService svc,HttpContext ctx)=>svc.Get(User(ctx),id)).Produces<Subscription>();
        subs.MapPatch("/{id}",(Guid id,SubscriptionPatch input,SubscriptionService svc,HttpContext ctx)=>svc.Update(User(ctx),id,input)).Produces<Subscription>();
        foreach(var (command,target) in new[]{("pause","paused"),("resume","active"),("cancel","cancelled")})
        {
            var newStatus=target;
            subs.MapPost("/{id}/"+command,(Guid id,SubscriptionService svc,HttpContext ctx)=>svc.ChangeStatus(User(ctx),id,newStatus)).Produces<Subscription>();
        }
        subs.MapDelete("/{id}",(Guid id,SubscriptionService svc,HttpContext ctx)=>{svc.Archive(User(ctx),id);return Results.NoContent();}).Produces(204);
        subs.MapPost("/{subscriptionId}/payments",(Guid subscriptionId,PaymentInput input,PaymentService svc,HttpContext ctx)=>
        {
            var item=svc.Create(User(ctx),subscriptionId,ctx.Request.Headers["Idempotency-Key"].ToString(),input);
            return Results.Created($"/api/v1/payments?subscriptionId={item.SubscriptionId}",item);
        }).Produces<Payment>(201);
        var pay=app.MapGroup("/api/v1/payments").RequireAuthorization().WithTags("Payments");
        pay.MapGet("",(PaymentService svc,HttpContext ctx,Guid? subscriptionId,string? status,DateOnly? from,DateOnly? to,int page=1,int pageSize=20)=>svc.List(User(ctx),subscriptionId,status,from,to,page,pageSize))
            .WithMetadata(new AllowedQuery("subscriptionId","status","from","to","page","pageSize")).Produces<Page<Payment>>();
        pay.MapPatch("/{id}",(Guid id,PaymentPatch input,PaymentService svc,HttpContext ctx)=>svc.Update(User(ctx),id,input)).Produces<Payment>();
        pay.MapPost("/{id}/void",(Guid id,PaymentService svc,HttpContext ctx)=>svc.Void(User(ctx),id)).Produces<Payment>();
        pay.MapGet("/{id}/audit",(Guid id,PaymentService svc,HttpContext ctx)=>svc.Audit(User(ctx),id)).Produces<IReadOnlyList<AuditRecord>>();
        var analytics=app.MapGroup("/api/v1/analytics").RequireAuthorization().WithTags("Analytics");
        analytics.MapGet("/expenses",(DateOnly from,DateOnly to,AnalyticsService svc,HttpContext ctx)=>svc.Expenses(User(ctx),from,to)).WithMetadata(new AllowedQuery("from","to")).Produces<ExpensesResult>();
        analytics.MapGet("/forecast",(ForecastService svc,HttpContext ctx)=>svc.Forecast(User(ctx))).Produces<ForecastResult>();
        app.MapGet("/api/v1/health",(IStore store)=>{store.Read(d=>true);return new HealthResponse("ok");}).WithTags("Health").Produces<HealthResponse>();
    }
}
public record RecoveryResponse(string Message);
public record HealthResponse(string Status);
