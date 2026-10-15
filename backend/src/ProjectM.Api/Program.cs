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
    o.UseSqlServer(config.GetConnectionString("Default"),
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
    // La API solo es accesible a través de nginx dentro de la red de Docker.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

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
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

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
