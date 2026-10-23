using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Domain.Users;

namespace ProjectM.Api.Infrastructure.Auth;

public record IssuedTokens(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

public enum RotationStatus
{
    /// <summary>Rotado: se emitió un sucesor.</summary>
    Rotated,
    /// <summary>Token inexistente, vencido o de usuario inactivo.</summary>
    Invalid,
    /// <summary>Otra petición lo acaba de rotar (p. ej. otra pestaña): reintentar con la cookie nueva.</summary>
    ConcurrentRotation,
    /// <summary>Reutilización de un token ya rotado fuera del margen: posible robo, se revocan todas las sesiones.</summary>
    ReuseDetected
}

public record RotationResult(RotationStatus Status, AppUser? User = null, string? RawToken = null, RefreshToken? Entity = null);

/// <summary>Emite JWT de acceso y refresh tokens rotativos (con detección de reutilización).</summary>
public class TokenService(AppDbContext db, IOptions<JwtOptions> options, IOptions<AuthOptions> authOptions, TimeProvider clock)
{
    /// <summary>Claim presente mientras el usuario debe cambiar su contraseña.</summary>
    public const string PasswordChangeClaim = "pwd_change";

    private readonly JwtOptions _opt = options.Value;
    private readonly AuthOptions _auth = authOptions.Value;

    public async Task<IssuedTokens> IssueAsync(AppUser user, IList<string> roles, string? ip, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var access = CreateAccessToken(user, roles, now, out var accessExp);
        var (raw, entity) = NewRefreshToken(user.Id, ip, now);
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(ct);
        return new IssuedTokens(access, accessExp, raw, entity.ExpiresAt);
    }

    /// <summary>
    /// Rota el refresh token de forma atómica: solo una petición concurrente puede revocarlo y obtener
    /// sucesor (UPDATE condicional). Presentar un token ya rotado revoca todas las sesiones del usuario,
    /// salvo dentro de un margen corto tras la rotación (carrera benigna entre pestañas).
    /// </summary>
    public async Task<RotationResult> RotateAsync(string rawToken, string? ip, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hash = Hash(rawToken);
        var existing = await db.RefreshTokens.AsNoTracking().Include(x => x.User)
            .FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (existing is null) return new(RotationStatus.Invalid);

        if (existing.RevokedAt is not null) return await OnReusedAsync(existing, now, ct);
        if (!existing.IsActive(now) || !existing.User.IsActive) return new(RotationStatus.Invalid);

        var (raw, replacement) = NewRefreshToken(existing.UserId, ip, now);
        var won = await db.RefreshTokens
            .Where(x => x.Id == existing.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now)
                                      .SetProperty(x => x.ReplacedById, replacement.Id), ct);
        if (won != 1)
        {
            // Otra petición lo rotó entre la lectura y el UPDATE: no se bifurca la sesión.
            var current = await db.RefreshTokens.AsNoTracking().FirstAsync(x => x.Id == existing.Id, ct);
            return await OnReusedAsync(current, now, ct);
        }

        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct);
        return new(RotationStatus.Rotated, existing.User, raw, replacement);
    }

    private async Task<RotationResult> OnReusedAsync(RefreshToken token, DateTimeOffset now, CancellationToken ct)
    {
        var withinGrace = token.ReplacedById is not null && token.RevokedAt is { } revoked
                          && now - revoked <= TimeSpan.FromSeconds(_auth.RefreshReuseGraceSeconds);
        if (withinGrace) return new(RotationStatus.ConcurrentRotation);

        await RevokeAllAsync(token.UserId, ct);
        return new(RotationStatus.ReuseDetected);
    }

    public string CreateAccessToken(AppUser user, IList<string> roles, DateTimeOffset now, out DateTimeOffset expires)
    {
        expires = now.AddMinutes(_opt.AccessTokenMinutes);
        // Sin correo ni otros datos personales: el token viaja en la URL de SignalR (?access_token=).
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("security_stamp", user.SecurityStamp ?? "")
        };
        if (user.MustChangePassword) claims.Add(new Claim(PasswordChangeClaim, "1"));
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var creds = new SigningCredentials(SigningKey(_opt.Key), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_opt.Issuer, _opt.Audience, claims,
            notBefore: now.UtcDateTime, expires: expires.UtcDateTime, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct)
    {
        var hash = Hash(rawToken);
        var now = clock.GetUtcNow();
        await db.RefreshTokens.Where(x => x.TokenHash == hash && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
    }

    public async Task RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.RefreshTokens.Where(x => x.UserId == userId && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
    }

    public static SymmetricSecurityKey SigningKey(string key) => new(Encoding.UTF8.GetBytes(key));

    private (string Raw, RefreshToken Entity) NewRefreshToken(Guid userId, string? ip, DateTimeOffset now)
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        return (raw, new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = Hash(raw),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_opt.RefreshTokenDays),
            CreatedByIp = ip
        });
    }

    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
