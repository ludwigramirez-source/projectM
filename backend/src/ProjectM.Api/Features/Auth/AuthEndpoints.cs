using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ProjectM.Api.Infrastructure.Auth;
using ProjectM.Api.Infrastructure.Errors;
using ProjectM.Api.Infrastructure.Validation;
using ProjectM.Domain.Users;

namespace ProjectM.Api.Features.Auth;

public record LoginRequest(string Email, string Password);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record UserInfo(Guid Id, string Email, string FullName, string Role);
public record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserInfo User);

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("El correo es obligatorio.")
            .EmailAddress().WithMessage("El correo no es válido.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("La contraseña es obligatoria.");
    }
}

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("La contraseña actual es obligatoria.");
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(10)
            .WithMessage("La nueva contraseña debe tener al menos 10 caracteres.");
    }
}

public static class AuthEndpoints
{
    public const string RefreshCookie = "pm_refresh";
    public const string CookiePath = "/api/v1/auth";
    public const string LoginRateLimitPolicy = "login";

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/auth").WithTags("Autenticación");

        g.MapPost("/login", Login).AllowAnonymous().RequireRateLimiting(LoginRateLimitPolicy).Validate<LoginRequest>();
        g.MapPost("/refresh", Refresh).AllowAnonymous().RequireRateLimiting(LoginRateLimitPolicy);
        g.MapPost("/logout", Logout).AllowAnonymous();
        g.MapGet("/me", Me).RequireAuthorization();
        g.MapPost("/change-password", ChangePassword).RequireAuthorization().Validate<ChangePasswordRequest>();
        return api;
    }

    private static async Task<IResult> Login(LoginRequest req, UserManager<AppUser> users, SignInManager<AppUser> signIn,
        TokenService tokens, IOptions<AuthOptions> authOpt, HttpContext http, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(req.Email.Trim());
        // Mensaje genérico: no revela si el correo existe.
        var invalid = new AppException(401, ErrorCodes.InvalidCredentials, "Correo o contraseña incorrectos.");
        if (user is null || !user.IsActive) throw invalid;

        var result = await signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
            throw new AppException(423, "CUENTA_BLOQUEADA",
                "La cuenta está bloqueada temporalmente por intentos fallidos. Intenta en 15 minutos.");
        if (!result.Succeeded) throw invalid;

        var roles = await users.GetRolesAsync(user);
        var issued = await tokens.IssueAsync(user, roles, http.Connection.RemoteIpAddress?.ToString(), ct);
        SetRefreshCookie(http, issued.RefreshToken, issued.RefreshTokenExpiresAt, authOpt.Value);
        return Results.Ok(new AuthResponse(issued.AccessToken, issued.AccessTokenExpiresAt, ToInfo(user, roles)));
    }

    private static async Task<IResult> Refresh(HttpContext http, TokenService tokens, UserManager<AppUser> users,
        IOptions<AuthOptions> authOpt, TimeProvider clock, CancellationToken ct)
    {
        var raw = http.Request.Cookies[RefreshCookie];
        var unauthorized = new AppException(401, ErrorCodes.Unauthorized, "La sesión expiró. Inicia sesión de nuevo.");
        if (string.IsNullOrEmpty(raw)) throw unauthorized;

        var rotated = await tokens.RotateAsync(raw, http.Connection.RemoteIpAddress?.ToString(), ct);
        if (rotated is null)
        {
            ClearRefreshCookie(http, authOpt.Value);
            throw unauthorized;
        }

        var (user, newRaw, entity) = rotated.Value;
        var roles = await users.GetRolesAsync(user);
        var access = tokens.CreateAccessToken(user, roles, clock.GetUtcNow(), out var exp);
        SetRefreshCookie(http, newRaw, entity.ExpiresAt, authOpt.Value);
        return Results.Ok(new AuthResponse(access, exp, ToInfo(user, roles)));
    }

    private static async Task<IResult> Logout(HttpContext http, TokenService tokens, IOptions<AuthOptions> authOpt, CancellationToken ct)
    {
        var raw = http.Request.Cookies[RefreshCookie];
        if (!string.IsNullOrEmpty(raw)) await tokens.RevokeAsync(raw, ct);
        ClearRefreshCookie(http, authOpt.Value);
        return Results.NoContent();
    }

    private static async Task<IResult> Me(ICurrentUser current, UserManager<AppUser> users)
    {
        var user = await users.FindByIdAsync(current.Id.ToString()) ?? throw AppException.NotFound("El usuario");
        return Results.Ok(ToInfo(user, await users.GetRolesAsync(user)));
    }

    private static async Task<IResult> ChangePassword(ChangePasswordRequest req, ICurrentUser current,
        UserManager<AppUser> users, TokenService tokens, Microsoft.Extensions.Caching.Memory.IMemoryCache cache, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(current.Id.ToString()) ?? throw AppException.NotFound("El usuario");
        var result = await users.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        result.ThrowIfFailed();
        await tokens.RevokeAllAsync(user.Id, ct);
        UserSessionCache.Invalidate(cache, user.Id);
        return Results.NoContent();
    }

    public static UserInfo ToInfo(AppUser u, IList<string> roles) =>
        new(u.Id, u.Email ?? "", u.FullName, Roles.All.FirstOrDefault(roles.Contains) ?? Roles.Observador);

    private static void SetRefreshCookie(HttpContext http, string token, DateTimeOffset expires, AuthOptions opt) =>
        http.Response.Cookies.Append(RefreshCookie, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = opt.SecureCookies,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            Expires = expires
        });

    private static void ClearRefreshCookie(HttpContext http, AuthOptions opt) =>
        http.Response.Cookies.Delete(RefreshCookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = opt.SecureCookies,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath
        });
}
