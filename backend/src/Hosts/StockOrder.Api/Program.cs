using System.Text;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using StockOrder.Api;
using StockOrder.Infrastructure;
using StockOrder.Infrastructure.Api;
using StockOrder.Infrastructure.Auth;
using StockOrder.Infrastructure.Correlation;
using StockOrder.Infrastructure.Database;
using StockOrder.Infrastructure.Logging;
using StockOrder.Infrastructure.Mobile;
using StockOrder.Infrastructure.Startup;
using StockOrder.Modules.Catalog;
using StockOrder.Modules.Finance;
using StockOrder.Modules.Identity;
using StockOrder.Modules.Inventory;
using StockOrder.Modules.Sync;
using StockOrder.Modules.Platform;
using StockOrder.Modules.Platform.Infrastructure.Bootstrap;
using StockOrder.Modules.Sales;
using StockOrder.Modules.Tenancy;

// `StockOrder.Api.exe bootstrap-platform --admin <username>` runs the one-time platform
// bootstrap (section 10) and exits without starting the web server.
if (args.Length > 0 && args[0] == "bootstrap-platform")
{
    await BootstrapCli.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);

Log.Logger = SerilogSetup.Configure(new LoggerConfiguration(), builder.Configuration).CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddStockOrderInfrastructure(builder.Configuration);
builder.Services.AddTenancyModule();
builder.Services.AddIdentityModule();
builder.Services.AddPlatformModule();
builder.Services.AddInventoryModule();
builder.Services.AddSyncModule();
builder.Services.AddCatalogModule();
builder.Services.AddFinanceModule();
builder.Services.AddSalesModule();

builder.Services.AddValidatorsFromAssembly(typeof(StockOrder.Modules.Tenancy.TenancyModule).Assembly);
builder.Services.AddValidatorsFromAssembly(typeof(StockOrder.Modules.Identity.IdentityModule).Assembly);
builder.Services.AddValidatorsFromAssembly(typeof(StockOrder.Modules.Platform.PlatformModule).Assembly);
builder.Services.AddValidatorsFromAssembly(typeof(StockOrder.Modules.Inventory.InventoryModule).Assembly);
builder.Services.AddValidatorsFromAssembly(typeof(StockOrder.Modules.Sync.SyncModule).Assembly);
builder.Services.AddValidatorsFromAssembly(typeof(StockOrder.Modules.Catalog.CatalogModule).Assembly);
builder.Services.AddValidatorsFromAssembly(typeof(StockOrder.Modules.Finance.FinanceModule).Assembly);
builder.Services.AddValidatorsFromAssembly(typeof(StockOrder.Modules.Sales.SalesModule).Assembly);

builder.Services.AddMediator(options =>
{
    // Handlers depend on scoped services (DbContexts, IUnitOfWork, ICurrentUserContext).
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.PipelineBehaviors = [typeof(StockOrder.Infrastructure.Validation.ValidationBehavior<,>)];
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// DI-injected options configuration: avoids building a throwaway service provider just to
// read the already-registered ISigningCertificateProvider (section 10).
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<ISigningCertificateProvider>((options, certificateProvider) =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new X509SecurityKey(certificateProvider.Certificate),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new StockOrder.Infrastructure.Api.UtcDateTimeJsonConverter()));
builder.Services.AddOpenApi();

// CORS allows only the admin site origin (section 17). AdminSite:Origin is a plain
// (non-secret) config value — env var AdminSite__Origin in production, defaults to the
// Angular dev server's own origin so `ng serve` works against a local API out of the box.
const string AdminSiteCorsPolicy = "AdminSite";
var adminSiteOrigin = builder.Configuration["AdminSite:Origin"] ?? "http://localhost:4200";
builder.Services.AddCors(options =>
{
    options.AddPolicy(AdminSiteCorsPolicy, policy => policy
        .WithOrigins(adminSiteOrigin)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders(CorrelationIdMiddleware.HeaderName, "ETag"));
});

builder.Services.AddRateLimiter(options =>
{
    // Login: 10/min per IP (section 17). Per-username partitioning happens inside the
    // handler itself via User.FailedLoginCount/LockoutEndUtc (section 10).
    options.AddPolicy("login", httpContext => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 10,
        }));

    // The customer menu (QR) is per IP only: a restaurant's guests often share its Wi-Fi, so one
    // address can be many phones, each loading the page and its pictures.
    options.AddPolicy(StockOrder.Modules.Catalog.Api.PublicMenuController.RateLimitPolicy, httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 1200,
            }));

    options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        httpContext.Request.Path.StartsWithSegments("/m")
            ? System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("public-menu")
            : System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                httpContext.User.FindFirst("sub")?.Value ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 300,
                }));
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

// Refuses to start if the database is missing a script the code expects (section 7).
await using (var scope = app.Services.CreateAsyncScope())
{
    var databaseOptions = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>();
    await SchemaVersionChecker.EnsureAppliedAsync(databaseOptions, CancellationToken.None);
}

app.UseForwardedHeaders();
app.UseExceptionHandler();

// Spec section 2 says Scalar UI is Development-only (avoids exposing the API schema/
// endpoint list publicly). Deviation, like the JWT-vs-OpenIddict one already noted in
// section 2: exposed in Production too when OpenApi:EnableInProduction is explicitly
// set (env var OpenApi__EnableInProduction=true), because the operator wants a
// browsable API UI without building out every screen in the Angular app first. Off by
// default even in Production, so a tenant deployment doesn't get this for free.
var enableOpenApiUi = app.Environment.IsDevelopment()
    || builder.Configuration.GetValue<bool>("OpenApi:EnableInProduction");
if (enableOpenApiUi)
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();

app.UseHttpsRedirection();
app.UseRateLimiter();

app.UseRouting();
app.UseCors(AdminSiteCorsPolicy);
app.UseAuthentication();
app.UseMiddleware<MobileVersionGateMiddleware>();

// Must run before UseAuthorization: PermissionAuthorizationHandler queries IdentityDbContext,
// which needs the request's connection/transaction already open (section 5.4).
app.UseMiddleware<UnitOfWorkMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

app.Run();
