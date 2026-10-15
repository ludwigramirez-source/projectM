using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using ProjectM.Api.Features.Auth;
using ProjectM.Api.Features.Users;
using ProjectM.Api.Infrastructure;
using ProjectM.Api.Infrastructure.Errors;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Api.Infrastructure.Seed;
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
}

app.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { status = "ok" })
        : Results.Json(new { status = "db_unavailable" }, statusCode: 503))
    .AllowAnonymous();

var api = app.MapGroup("/api/v1").RequireAuthorization();
api.MapAuthEndpoints();
api.MapUserEndpoints();

app.Run();

public partial class Program;
