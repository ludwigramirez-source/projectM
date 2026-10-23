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
public record UserInfo(Guid Id, string Email, string FullName, string Role, bool MustChangePassword);
public record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserInfo User);

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("El correo es obligatorio.")
            .EmailAddress().WithMessage("El correo no es válido.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("La contraseña es obligatoria.")
            .MaximumLength(128).WithMessage("La contraseña no puede superar 128 caracteres.");
    }
}

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("La contraseña actual es obligatoria.")
            .MaximumLength(128).WithMessage("La contraseña no puede superar 128 caracteres.");
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(10)
            .WithMessage("La nueva contraseña debe tener al menos 10 caracteres.")
            .MaximumLength(128).WithMessage("La contraseña no puede superar 128 caracteres.")
            .NotEqual(x => x.CurrentPassword).WithMessage("La nueva contraseña debe ser distinta de la actual.");
    }
}

public static class AuthEndpoints
{
    public const string RefreshCookie = "pm_refresh";
    public const string CookiePath = "/api/v1/auth";
    public const string LoginRateLimitPolicy = "login";
    public const string RefreshRateLimitPolicy = "refresh";

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/auth").WithTags("Autenticación");

        g.MapPost("/login", Login).AllowAnonymous().RequireRateLimiting(LoginRateLimitPolicy).Validate<LoginRequest>();
        g.MapPost("/refresh", Refresh).AllowAnonymous().RequireRateLimiting(RefreshRateLimitPolicy);
        g.MapPost("/logout", Logout).AllowAnonymous();
        g.MapGet("/me", Me).RequireAuthorization();
        g.MapPost("/change-password", ChangePassword).RequireAuthorization().RequireRateLimiting(LoginRateLimitPolicy)
            .Validate<ChangePasswordRequest>();
        return api;
    }

    /// <summary>Mismo mensaje para correo inexistente, contraseña errónea y cuenta bloqueada (no revela cuentas).</summary>
    private static AppException InvalidCredentials() => new(401, ErrorCodes.InvalidCredentials,
        "Correo o contraseña incorrectos. Tras 5 intentos fallidos la cuenta se bloquea 15 minutos.");

    // Hash de una contraseña aleatoria: se verifica contra él cuando el usuario no existe, para que la
    // respuesta tarde lo mismo que con un usuario real (evita enumerar cuentas por tiempo).
    private static readonly Lazy<string> DummyHash = new(() => new PasswordHasher<AppUser>()
        .HashPassword(new AppUser(), Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24))));

    private static async Task<IResult> Login(LoginRequest req, UserManager<AppUser> users, SignInManager<AppUser> signIn,
        IPasswordHasher<AppUser> hasher, TokenService tokens, IOptions<AuthOptions> authOpt, HttpContext http, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(req.Email.Trim());
        if (user is null || !user.IsActive)
        {
            hasher.VerifyHashedPassword(new AppUser(), DummyHash.Value, req.Password);
            throw InvalidCredentials();
        }

        var result = await signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (!result.Succeeded) throw InvalidCredentials(); // incluye IsLockedOut: misma respuesta

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
        switch (rotated.Status)
        {
            case RotationStatus.ConcurrentRotation:
                // Otra pestaña acaba de renovar la sesión: el cliente reintenta con la cookie nueva.
                throw AppException.Conflict("REFRESH_EN_CURSO", "La sesión se está renovando. Intenta de nuevo.");
            case RotationStatus.Invalid:
            case RotationStatus.ReuseDetected:
                ClearRefreshCookie(http, authOpt.Value);
                throw unauthorized;
        }

        var user = rotated.User!;
        var roles = await users.GetRolesAsync(user);
        var access = tokens.CreateAccessToken(user, roles, clock.GetUtcNow(), out var exp);
        SetRefreshCookie(http, rotated.RawToken!, rotated.Entity!.ExpiresAt, authOpt.Value);
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

    /// <summary>
    /// Cambia la contraseña verificando la actual con bloqueo por intentos (como el login), cierra las demás
    /// sesiones y devuelve una sesión nueva para seguir trabajando sin volver a iniciar sesión.
    /// </summary>
    private static async Task<IResult> ChangePassword(ChangePasswordRequest req, ICurrentUser current,
        UserManager<AppUser> users, SignInManager<AppUser> signIn, TokenService tokens, IOptions<AuthOptions> authOpt,
        Microsoft.Extensions.Caching.Memory.IMemoryCache cache, HttpContext http, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(current.Id.ToString()) ?? throw AppException.NotFound("El usuario");
        var check = await signIn.CheckPasswordSignInAsync(user, req.CurrentPassword, lockoutOnFailure: true);
        if (check.IsLockedOut)
            throw new AppException(423, "CUENTA_BLOQUEADA", "Demasiados intentos. Intenta de nuevo en 15 minutos.");
        if (!check.Succeeded)
            throw AppException.Validation("currentPassword", "La contraseña actual no es correcta.");

        var token = await users.GeneratePasswordResetTokenAsync(user);
        (await users.ResetPasswordAsync(user, token, req.NewPassword)).ThrowIfFailed("newPassword");
        user.MustChangePassword = false;
        (await users.UpdateAsync(user)).ThrowIfFailed();
        await tokens.RevokeAllAsync(user.Id, ct);
        UserSessionCache.Invalidate(cache, user.Id);

        var roles = await users.GetRolesAsync(user);
        var issued = await tokens.IssueAsync(user, roles, http.Connection.RemoteIpAddress?.ToString(), ct);
        SetRefreshCookie(http, issued.RefreshToken, issued.RefreshTokenExpiresAt, authOpt.Value);
        return Results.Ok(new AuthResponse(issued.AccessToken, issued.AccessTokenExpiresAt, ToInfo(user, roles)));
    }

    public static UserInfo ToInfo(AppUser u, IList<string> roles) =>
        new(u.Id, u.Email ?? "", u.FullName, Roles.All.FirstOrDefault(roles.Contains) ?? Roles.Observador,
            u.MustChangePassword);

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
