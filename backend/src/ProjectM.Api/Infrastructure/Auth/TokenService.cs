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

/// <summary>Emite JWT de acceso y refresh tokens rotativos (con detección de reutilización).</summary>
public class TokenService(AppDbContext db, IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JwtOptions _opt = options.Value;

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
    /// Rota el refresh token. Si se presenta un token ya revocado (posible robo), se revocan
    /// todas las sesiones del usuario.
    /// </summary>
    public async Task<(AppUser User, string RawToken, RefreshToken Entity)?> RotateAsync(string rawToken, string? ip, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hash = Hash(rawToken);
        var existing = await db.RefreshTokens.Include(x => x.User).FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (existing is null) return null;

        if (existing.RevokedAt is not null)
        {
            await RevokeAllAsync(existing.UserId, ct);
            return null;
        }
        if (!existing.IsActive(now) || !existing.User.IsActive) return null;

        var (raw, replacement) = NewRefreshToken(existing.UserId, ip, now);
        existing.RevokedAt = now;
        existing.ReplacedById = replacement.Id;
        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct);
        return (existing.User, raw, replacement);
    }

    public string CreateAccessToken(AppUser user, IList<string> roles, DateTimeOffset now, out DateTimeOffset expires)
    {
        expires = now.AddMinutes(_opt.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("security_stamp", user.SecurityStamp ?? "")
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var creds = new SigningCredentials(SigningKey(_opt.Key), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_opt.Issuer, _opt.Audience, claims,
            notBefore: now.UtcDateTime, expires: expires.UtcDateTime, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct)
    {
        var hash = Hash(rawToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (token is { RevokedAt: null })
        {
            token.RevokedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
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
