using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Subscriptions.Api;
using Subscriptions.Application;
using Subscriptions.Infrastructure;

var builder=WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore",LogLevel.Warning);
builder.WebHost.ConfigureKestrel(options=>options.Limits.MaxRequestBodySize=1048576);
var key=builder.Configuration["Jwt:Key"];
if(string.IsNullOrEmpty(key))
{
    if(!builder.Environment.IsDevelopment())throw new InvalidOperationException("Jwt:Key must be supplied outside Development.");
    key=Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)); // ephemeral, never logged or persisted
}
if(Encoding.UTF8.GetByteCount(key)<32)throw new InvalidOperationException("JWT key must contain at least 32 bytes.");
var jwt=new JwtSettings(key,builder.Configuration["Jwt:Issuer"]??"subscriptions",builder.Configuration["Jwt:Audience"]??"subscriptions-api");
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<IClock,SystemClock>();builder.Services.AddSingleton<ISecrets,CryptoSecrets>();builder.Services.AddSingleton<IPasswordHasher,ArgonPasswordHasher>();builder.Services.AddSingleton<ITokenIssuer,JwtIssuer>();
builder.Services.AddSingleton<MemoryEmailSender>();builder.Services.AddSingleton<IEmailSender>(sp=>sp.GetRequiredService<MemoryEmailSender>());
builder.Services.AddSingleton<IWriteObserver,NoWriteObserver>();
var dbPath=builder.Configuration["Database:Path"]??System.IO.Path.Combine(builder.Environment.ContentRootPath,"data","subscriptions.db");
builder.Services.AddSingleton<IStore>(sp=>new SqliteStore(dbPath,sp.GetRequiredService<IWriteObserver>()));
builder.Services.AddSingleton<LoginIpLimiter>();
builder.Services.AddScoped<AuthService>();builder.Services.AddScoped<SubscriptionService>();builder.Services.AddScoped<PaymentService>();builder.Services.AddScoped<AnalyticsService>();builder.Services.AddScoped<ForecastService>();
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(o=>o.ThrowOnBadRequest=true);
builder.Services.ConfigureHttpJsonOptions(o=>{o.SerializerOptions.UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow;o.SerializerOptions.PropertyNameCaseInsensitive=false;o.SerializerOptions.Converters.Add(new UtcJsonConverter());});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options=>
{
    options.MapInboundClaims=false;
    options.TokenValidationParameters=new(){ValidateIssuerSigningKey=true,IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),ValidateIssuer=true,ValidIssuer=jwt.Issuer,ValidateAudience=true,ValidAudience=jwt.Audience,ValidateLifetime=true,RequireExpirationTime=true,ClockSkew=TimeSpan.Zero,ValidAlgorithms=[SecurityAlgorithms.HmacSha256]};
    options.Events=new JwtBearerEvents
    {
        OnChallenge=async ctx=>{ctx.HandleResponse();ctx.Response.Headers.WWWAuthenticate="Bearer";await Transport.Error(ctx.HttpContext,401,"unauthorized","Authentication required.");},
        OnForbidden=ctx=>Transport.Error(ctx.HttpContext,403,"forbidden","Access denied."),
        OnTokenValidated=ctx=>{if(!Guid.TryParse(ctx.Principal?.FindFirst("sub")?.Value,out _))ctx.Fail("Invalid subject.");return Task.CompletedTask;}
    };
});
builder.Services.AddAuthorization();builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options=>
{
    options.SwaggerDoc("v1",new OpenApiInfo{Title="Subscriptions API",Version="v1",Description="Educational local API. No banking charges or FX."});
    options.AddSecurityDefinition("Bearer",new OpenApiSecurityScheme{Type=SecuritySchemeType.Http,Scheme="bearer",BearerFormat="JWT"});
    options.OperationFilter<ContractFilter>();
});
var app=builder.Build();
// Schema is created on the first database request. A down database must not kill startup.
app.UseRouting();app.Use(Transport.Boundary);
if(!app.Environment.IsDevelopment()){app.UseHsts();app.UseHttpsRedirection();}
app.UseAuthentication();app.UseAuthorization();app.Use(Transport.Validate);
if(app.Environment.IsDevelopment()){app.UseSwagger();app.UseSwaggerUI(options=>options.SwaggerEndpoint("/swagger/v1/swagger.json","Subscriptions v1"));}
app.UseStatusCodePages(async status=>
{
    var ctx=status.HttpContext;var code=ctx.Response.StatusCode;
    await Transport.Error(ctx,code,code==404?"not_found":code==405?"method_not_allowed":"http_error","Request could not be completed.");
});
ApiRoutes.MapApi(app);app.Run();
public partial class Program { }
