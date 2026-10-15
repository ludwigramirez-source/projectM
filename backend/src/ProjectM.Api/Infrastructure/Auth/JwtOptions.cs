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
    public int LoginPermitLimitPerMinute { get; set; } = 5;
}
