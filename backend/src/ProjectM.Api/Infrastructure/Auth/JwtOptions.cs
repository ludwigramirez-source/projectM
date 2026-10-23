namespace ProjectM.Api.Infrastructure.Auth;

public class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "ProjectM";
    public string Audience { get; set; } = "ProjectM.Web";
    public string Key { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}

public class AuthOptions
{
    public const string Section = "Auth";
    /// <summary>Marca la cookie de refresh como Secure (requiere HTTPS salvo en localhost).</summary>
    public bool SecureCookies { get; set; } = true;
    /// <summary>Intentos de login por IP y minuto (además del bloqueo por cuenta tras 5 fallos).</summary>
    public int LoginPermitLimitPerMinute { get; set; } = 10;
    /// <summary>Renovaciones de sesión por IP y minuto (cada recarga de página hace una).</summary>
    public int RefreshPermitLimitPerMinute { get; set; } = 120;
    /// <summary>
    /// Intentos de login por minuto sumando todas las IP. Acota el "password spraying" aunque un atacante
    /// rote o falsifique su IP (X-Forwarded-For).
    /// </summary>
    public int LoginGlobalPermitLimitPerMinute { get; set; } = 60;
    /// <summary>Escrituras costosas (tareas, comentarios, etiquetas) y consultas del dashboard por usuario y minuto.</summary>
    public int UserWritePermitLimitPerMinute { get; set; } = 60;
    /// <summary>Subidas de adjuntos por usuario cada 10 minutos.</summary>
    public int UploadPermitLimitPer10Minutes { get; set; } = 30;
    /// <summary>
    /// Segundos en que un refresh token recién rotado se trata como carrera benigna (otra pestaña)
    /// en lugar de como robo: se responde 409 REFRESH_EN_CURSO sin revocar todas las sesiones.
    /// </summary>
    public int RefreshReuseGraceSeconds { get; set; } = 10;
    /// <summary>
    /// Obliga a cambiar la contraseña en el primer ingreso del Admin sembrado, de los usuarios creados por un
    /// Admin y tras un restablecimiento.
    /// </summary>
    public bool ForcePasswordChange { get; set; } = true;
}
