using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Wings.Api.Infrastructure;
using Wings.Api.Security;
using Wings.Application;
using Wings.Application.Abstractions;
using Wings.Infrastructure;
using Wings.Infrastructure.Persistence;
using Wings.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

// ---- Services ----------------------------------------------------------------------------------------------------

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddSingleton<IChangeNotifier, SignalRChangeNotifier>();
builder.Services.AddWingsApplication();
builder.Services.AddWingsInfrastructure();
builder.Services.PostConfigure<StorageOptions>(o =>
{
    if (!Path.IsPathRooted(o.RootPath)) o.RootPath = Path.Combine(builder.Environment.ContentRootPath, o.RootPath);
});

builder.Services.AddOptions<JwtOptions>().BindConfiguration("Auth:Jwt")
    .Validate(o => o.SigningKey.Length >= 32, "Auth:Jwt:SigningKey must be at least 32 characters. Set it via environment variable or secret store.")
    .ValidateOnStart();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer()
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, AgentKeyHandler>(AgentKeyHandler.Scheme, null)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, IntegrationKeyHandler>(IntegrationKeyHandler.Scheme, null);
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<IOptions<JwtOptions>>((o, jwtOptions) =>
{
    var jwt = jwtOptions.Value;
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience, IssuerSigningKey = jwt.SigningKey.Length >= 32 ? jwt.Key() : null,
        ValidateIssuerSigningKey = true, ClockSkew = TimeSpan.FromMinutes(1), NameClaimType = "unique_name",
    };
    // SignalR sends the token as a query parameter for WebSocket connections.
    o.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/hubs") && ctx.Request.Query["access_token"] is { Count: > 0 } t) ctx.Token = t;
            return Task.CompletedTask;
        },
    };
});
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(AgentKeyHandler.Policy, p => p.AddAuthenticationSchemes(AgentKeyHandler.Scheme).RequireClaim(AgentKeyHandler.AgentIdClaim))
    .AddPolicy(IntegrationKeyHandler.Policy, p => p.AddAuthenticationSchemes(IntegrationKeyHandler.Scheme).RequireClaim(IntegrationKeyHandler.IntegrationClaim));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue("Auth:LoginAttemptsPerMinute", 10), Window = TimeSpan.FromMinutes(1),
        }));
});

static long MaxBody(IConfiguration c) => (c.GetValue<long?>("Packages:MaxUploadBytes") ?? 100L * 1024 * 1024) + 1024 * 1024;
builder.Services.AddOptions<FormOptions>().Configure<IConfiguration>((o, c) => o.MultipartBodyLengthLimit = MaxBody(c));
builder.WebHost.ConfigureKestrel((ctx, k) => k.Limits.MaxRequestBodySize = MaxBody(ctx.Configuration));
builder.Services.AddOptions<Microsoft.AspNetCore.Builder.IISServerOptions>().Configure<IConfiguration>((o, c) => o.MaxRequestBodySize = MaxBody(c));

builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddCors();
builder.Services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>().Configure<IConfiguration>((o, c) =>
{
    if (c.GetSection("Cors:Origins").Get<string[]>() is { Length: > 0 } origins)
        o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials());
});

// ---- Pipeline ----------------------------------------------------------------------------------------------------

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers.XFrameOptions = "DENY";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
// Explicit routing after static files, so folders such as /sample-portal/x/ resolve to their index.html instead of the SPA fallback.
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment()) app.MapOpenApi().AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();
app.MapHub<ChangesHub>("/hubs/changes");
app.MapFallback("/api/{**rest}", () => Results.Problem(statusCode: 404, title: "Not found")).AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

public partial class Program;
