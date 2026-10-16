using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using ProjectM.Api.Features.Auth;
using ProjectM.Api.Infrastructure.Auth;
using ProjectM.Api.Infrastructure.Errors;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Api.Infrastructure.Seed;
using ProjectM.Domain.Users;

namespace ProjectM.Api.Infrastructure;

public static class ServiceRegistration
{
    public static IServiceCollection AddAuthServices(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.Configure<AuthOptions>(config.GetSection(AuthOptions.Section));
        var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key debe configurarse (variable JWT_KEY) con al menos 32 caracteres.");

        services.AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequiredLength = 10;
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
                o.User.RequireUniqueEmail = true;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<SpanishIdentityErrorDescriber>();

        JwtSecurityTokenHandler.DefaultMapInboundClaims = false;
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = TokenService.SigningKey(jwt.Key),
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.Name,
                    RoleClaimType = ClaimTypes.Role
                };
                o.Events = new JwtBearerEvents
                {
                    // SignalR envía el token por query string en WebSockets.
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                            ctx.Token = token;
                        return Task.CompletedTask;
                    },
                    // Invalida tokens de usuarios desactivados o cuyo rol/contraseña cambió.
                    OnTokenValidated = async ctx =>
                    {
                        var sub = ctx.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
                        var stamp = ctx.Principal?.FindFirstValue("security_stamp");
                        if (!Guid.TryParse(sub, out var userId)) { ctx.Fail("Token inválido"); return; }

                        var cache = ctx.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                        var state = await cache.GetOrCreateAsync(UserSessionCache.Key(userId), async entry =>
                        {
                            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                            var users = ctx.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
                            var user = await users.FindByIdAsync(userId.ToString());
                            return user is null ? null : new UserState(user.IsActive, user.SecurityStamp);
                        });
                        if (state is null || !state.IsActive || state.SecurityStamp != stamp)
                            ctx.Fail("Sesión revocada");
                    }
                };
            });

        services.AddAuthorization(Policies.AddPolicies);
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<TokenService>();
        services.AddScoped<IdentitySeeder>();

        var authOpt = config.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Login: estricto (fuerza bruta; además hay bloqueo por cuenta). Refresh: holgado, porque
            // cada recarga de página lo usa y una oficina tras NAT comparte la misma IP.
            static void PerIpPolicy(RateLimiterOptions o, string name, int limit) =>
                o.AddPolicy(name, http =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        http.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = limit,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        }));
            PerIpPolicy(o, AuthEndpoints.LoginRateLimitPolicy, authOpt.LoginPermitLimitPerMinute);
            PerIpPolicy(o, AuthEndpoints.RefreshRateLimitPolicy, authOpt.RefreshPermitLimitPerMinute);
            o.OnRejected = async (ctx, ct) =>
            {
                var pd = new ProblemDetails
                {
                    Status = 429,
                    Title = "Demasiados intentos. Espera un minuto e intenta de nuevo."
                };
                pd.Extensions["code"] = ErrorCodes.RateLimited;
                ctx.HttpContext.Response.StatusCode = 429;
                await ctx.HttpContext.Response.WriteAsJsonAsync(pd, (System.Text.Json.JsonSerializerOptions?)null,
                    "application/problem+json", ct);
            };
        });

        return services;
    }

    private record UserState(bool IsActive, string? SecurityStamp);
}
