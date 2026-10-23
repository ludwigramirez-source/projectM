using System.Security.Cryptography;
using System.Text;
using ProjectM.Api.Infrastructure.Auth;
using ProjectM.Api.Infrastructure.Errors;

namespace ProjectM.Api.Infrastructure;

/// <summary>Validaciones de configuración que impiden arrancar en un estado inseguro.</summary>
public static class StartupGuard
{
    /// <summary>Valores de ejemplo que alguna vez estuvieron publicados en el repositorio (.env.example).</summary>
    public static readonly string[] PublicPlaceholders =
    [
        "reemplazar-por-una-clave-aleatoria-larga-de-al-menos-32-caracteres",
        "Admin_Cambiar_123!",
        "Demo_Cambiar_123!",
        "Cambiar_Esta_Clave_123"
    ];

    public static void RejectPublicPlaceholders(IConfiguration config, IHostEnvironment env)
    {
        if (env.IsDevelopment()) return;

        foreach (var key in new[] { "Jwt:Key", "Seed:AdminPassword", "Seed:DemoPassword" })
            if (PublicPlaceholders.Contains(config[key]))
                throw new InvalidOperationException(
                    $"{key} usa un valor de ejemplo publicado en el repositorio. Genera uno propio (p. ej. openssl rand -base64 48).");

        var connection = config.GetConnectionString("Default") ?? "";
        if (PublicPlaceholders.Any(p => connection.Contains(p, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                "La contraseña de la base de datos es un valor de ejemplo del repositorio. Cámbiala (POSTGRES_PASSWORD).");
    }
}

public static class SecurityMiddleware
{
    public const string ProxySecretHeader = "X-Proxy-Secret";

    /// <summary>
    /// Si Proxy:SharedSecret está configurado, /api/v1/auth/* solo se acepta cuando llega a través del proxy del
    /// frontend (que inyecta el encabezado X-Proxy-Secret). Así el login no puede atacarse llamando directamente a
    /// la API pública con una IP falsificada en X-Forwarded-For. El resto de la API (SignalR, adjuntos) sigue
    /// accesible directamente con JWT.
    /// </summary>
    public static IApplicationBuilder UseProxySecretForAuth(this IApplicationBuilder app, IConfiguration config)
    {
        var secret = config["Proxy:SharedSecret"];
        if (string.IsNullOrWhiteSpace(secret)) return app;
        var expected = Encoding.UTF8.GetBytes(secret);

        return app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api/v1/auth"))
            {
                var provided = Encoding.UTF8.GetBytes(ctx.Request.Headers[ProxySecretHeader].ToString());
                if (!CryptographicOperations.FixedTimeEquals(provided, expected))
                    throw new AppException(403, ErrorCodes.Forbidden, "Acceso no permitido por este origen.");
            }
            await next();
        });
    }

    /// <summary>
    /// Mientras el token lleve el claim pwd_change, solo se permite /api/v1/auth/* (ver sesión, cambiar contraseña,
    /// cerrar sesión). El resto de la API responde 403 CAMBIO_CONTRASENA_REQUERIDO.
    /// </summary>
    public static IApplicationBuilder UseRequiredPasswordChange(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            if (ctx.User.HasClaim(TokenService.PasswordChangeClaim, "1")
                && ctx.Request.Path.StartsWithSegments("/api/v1")
                && !ctx.Request.Path.StartsWithSegments("/api/v1/auth"))
                throw new AppException(403, PasswordChangeRequiredCode,
                    "Debes cambiar tu contraseña antes de continuar.");
            await next();
        });

    public const string PasswordChangeRequiredCode = "CAMBIO_CONTRASENA_REQUERIDO";
}
