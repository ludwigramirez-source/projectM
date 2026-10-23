using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using ProjectM.Api.Features.Auth;
using ProjectM.Api.Features.Closure;
using ProjectM.Api.Features.Collaboration;
using ProjectM.Api.Features.Dashboard;
using ProjectM.Api.Features.Projects;
using ProjectM.Api.Features.Spaces;
using ProjectM.Api.Features.Tasks;
using ProjectM.Api.Features.Templates;
using ProjectM.Api.Features.Users;
using ProjectM.Api.Infrastructure;
using ProjectM.Api.Infrastructure.Authorization;
using ProjectM.Api.Infrastructure.Errors;
using ProjectM.Api.Infrastructure.Events;
using ProjectM.Api.Infrastructure.Notifications;
using ProjectM.Api.Infrastructure.Realtime;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Api.Infrastructure.Seed;
using ProjectM.Api.Infrastructure.Storage;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Servicios ----------
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Extensions.TryAdd("traceId", ctx.HttpContext.TraceIdentifier);
    if (!ctx.ProblemDetails.Extensions.ContainsKey("code"))
    {
        ctx.ProblemDetails.Extensions["code"] = ctx.ProblemDetails.Status switch
        {
            401 => ErrorCodes.Unauthorized,
            403 => ErrorCodes.Forbidden,
            404 => ErrorCodes.NotFound,
            429 => ErrorCodes.RateLimited,
            _ => ErrorCodes.Internal
        };
    }
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(
            ConnectionStrings.NormalizePostgres(config.GetConnectionString("Default"), config["Database:RootCertificate"]))
        {
            // Sin Kerberos: evita el intento de cifrado GSS (la imagen no trae libgssapi y se usa TLS).
            GssEncryptionMode = Npgsql.GssEncryptionMode.Disable
        }.ConnectionString,
        sql => sql.EnableRetryOnFailure(5)));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddAuthServices(config);
builder.Services.AddSingleton<AppClock>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, ProjectAuthorizationHandler>();
builder.Services.AddScoped<ProjectGuard>();
builder.Services.AddScoped<ProjectQueries>();
builder.Services.AddScoped<ProjectLifecycle>();
builder.Services.AddScoped<ReferenceDataSeeder>();
builder.Services.AddScoped<DemoSeeder>();
builder.Services.AddScoped<TaskQueries>();
builder.Services.Configure<StorageOptions>(config.GetSection(StorageOptions.Section));
if (string.Equals(config["Storage:Provider"], "S3", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IFileStorage, S3FileStorage>();
else
    builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<DomainEventDispatcher>();
builder.Services.AddScoped<NotificationSender>();
builder.Services.AddSingleton<INotificationChannel, SignalRNotificationChannel>();
if (config.GetValue("Notifications:DueDateJob", true)) builder.Services.AddHostedService<DueDateNotifier>();
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 25 * 1024 * 1024);
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddOpenApi();

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // La API solo se expone detrás de proxies: nginx en Docker (1 salto) o Vercel → Render (2 saltos).
    // Proxy:ForwardLimit indica cuántos proxies confiables hay, para obtener la IP real del cliente
    // (la usa el rate limiting del login).
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = config.GetValue("Proxy:ForwardLimit", 1);
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// Nunca arrancar fuera de Development con los valores de ejemplo publicados en el repositorio (.env.example).
StartupGuard.RejectPublicPlaceholders(config, builder.Environment);

var allowedOrigins = (config["Cors:AllowedOrigins"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// ---------- Pipeline ----------
var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors();
app.UseProxySecretForAuth(config);
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseRequiredPasswordChange();

await using (var scope = app.Services.CreateAsyncScope())
{
    if (config.GetValue<bool>("Database:MigrateOnStartup"))
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();
    await scope.ServiceProvider.GetRequiredService<ReferenceDataSeeder>().SeedAsync();
    await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedAsync();
}

app.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { status = "ok" })
        : Results.Json(new { status = "db_unavailable" }, statusCode: 503))
    .AllowAnonymous();

var api = app.MapGroup("/api/v1").RequireAuthorization();
api.MapAuthEndpoints();
api.MapUserEndpoints();
api.MapSpaceEndpoints();
api.MapProjectEndpoints();
api.MapTrackingEndpoints();
api.MapClosureEndpoints();
api.MapTemplateEndpoints();
api.MapTaskEndpoints();
api.MapTaskSupportEndpoints();
api.MapCalendarEndpoints();
api.MapCollaborationEndpoints();
api.MapDashboardEndpoints();

app.MapHub<NotificationsHub>("/hubs/notifications");

app.Run();

public partial class Program;
