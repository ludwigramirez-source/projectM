using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ProjectM.Api.Infrastructure.Auth;
using ProjectM.Api.Infrastructure.Errors;
using ProjectM.Api.Infrastructure.Pagination;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Api.Infrastructure.Validation;
using ProjectM.Domain.Users;

namespace ProjectM.Api.Features.Users;

public record UserDto(Guid Id, string Email, string FullName, string Role, bool IsActive, DateTimeOffset CreatedAt);
public record UserLookupDto(Guid Id, string FullName, string Email);
public record CreateUserRequest(string Email, string FullName, string Role, string Password);
public record UpdateUserRequest(string Email, string FullName, string Role, bool IsActive);
public record ResetPasswordRequest(string NewPassword);

public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256).WithMessage("Correo no válido.");
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150).WithMessage("El nombre es obligatorio (máx. 150).");
        RuleFor(x => x.Role).Must(r => Roles.All.Contains(r)).WithMessage("Rol no válido.");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10).WithMessage("La contraseña debe tener al menos 10 caracteres.");
    }
}

public class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256).WithMessage("Correo no válido.");
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150).WithMessage("El nombre es obligatorio (máx. 150).");
        RuleFor(x => x.Role).Must(r => Roles.All.Contains(r)).WithMessage("Rol no válido.");
    }
}

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator() =>
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(10).WithMessage("La contraseña debe tener al menos 10 caracteres.");
}

public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/users").WithTags("Usuarios").RequireAuthorization(Policies.Admin);
        admin.MapGet("/", List);
        admin.MapPost("/", Create).Validate<CreateUserRequest>();
        admin.MapPut("/{id:guid}", Update).Validate<UpdateUserRequest>();
        admin.MapPost("/{id:guid}/deactivate", Deactivate);
        admin.MapPost("/{id:guid}/reset-password", ResetPassword).Validate<ResetPasswordRequest>();

        // Búsqueda ligera para asignar responsables / miembros (cualquier usuario autenticado).
        api.MapGet("/users/lookup", Lookup).WithTags("Usuarios").RequireAuthorization();
        return api;
    }

    private static IQueryable<UserDto> Project(AppDbContext db, IQueryable<AppUser> users) =>
        users.Select(u => new UserDto(u.Id, u.Email!, u.FullName,
            db.UserRoles.Where(ur => ur.UserId == u.Id)
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
                .OrderBy(n => n == Roles.Admin ? 0 : n == Roles.Gestor ? 1 : n == Roles.Miembro ? 2 : 3)
                .FirstOrDefault() ?? Roles.Observador,
            u.IsActive, u.CreatedAt));

    private static async Task<IResult> List(AppDbContext db, string? q, string? role, bool? active,
        int? page, int? pageSize, CancellationToken ct)
    {
        var users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            users = users.Where(u => u.FullName.Contains(term) || u.Email!.Contains(term));
        }
        if (active is not null) users = users.Where(u => u.IsActive == active);
        if (!string.IsNullOrWhiteSpace(role))
        {
            users = users.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id &&
                                                            db.Roles.Any(r => r.Id == ur.RoleId && r.Name == role)));
        }
        var result = await Project(db, users.OrderBy(u => u.FullName)).ToPagedAsync(page, pageSize, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> Lookup(AppDbContext db, string? q, CancellationToken ct)
    {
        var users = db.Users.AsNoTracking().Where(u => u.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            users = users.Where(u => u.FullName.Contains(term) || u.Email!.Contains(term));
        }
        var items = await users.OrderBy(u => u.FullName).Take(50)
            .Select(u => new UserLookupDto(u.Id, u.FullName, u.Email!)).ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> Create(CreateUserRequest req, UserManager<AppUser> users, AppDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = req.Email.Trim(),
            UserName = req.Email.Trim(),
            FullName = req.FullName.Trim(),
            EmailConfirmed = true,
            CreatedAt = clock.GetUtcNow()
        };
        (await users.CreateAsync(user, req.Password)).ThrowIfFailed();
        (await users.AddToRoleAsync(user, req.Role)).ThrowIfFailed("role");
        var dto = await Project(db, db.Users.Where(u => u.Id == user.Id)).FirstAsync(ct);
        return Results.Created($"/api/v1/users/{user.Id}", dto);
    }

    private static async Task<IResult> Update(Guid id, UpdateUserRequest req, UserManager<AppUser> users, AppDbContext db,
        ICurrentUser current, TokenService tokens, IMemoryCache cache, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString()) ?? throw AppException.NotFound("El usuario");
        if (user.Id == current.Id && (req.Role != Roles.Admin || !req.IsActive))
            throw AppException.Conflict("AUTO_DEGRADACION", "No puedes quitarte el rol de Admin ni desactivarte a ti mismo.");

        var wasActive = user.IsActive;
        user.FullName = req.FullName.Trim();
        user.IsActive = req.IsActive;
        if (!string.Equals(user.Email, req.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            user.Email = req.Email.Trim();
            user.UserName = req.Email.Trim();
        }
        (await users.UpdateAsync(user)).ThrowIfFailed("email");

        var currentRoles = await users.GetRolesAsync(user);
        if (!currentRoles.SequenceEqual([req.Role]))
        {
            (await users.RemoveFromRolesAsync(user, currentRoles)).ThrowIfFailed("role");
            (await users.AddToRoleAsync(user, req.Role)).ThrowIfFailed("role");
            await users.UpdateSecurityStampAsync(user); // invalida tokens con el rol anterior
        }
        if (wasActive && !req.IsActive)
        {
            await users.UpdateSecurityStampAsync(user);
            await tokens.RevokeAllAsync(user.Id, ct);
        }
        UserSessionCache.Invalidate(cache, user.Id);
        return Results.Ok(await Project(db, db.Users.Where(u => u.Id == id)).FirstAsync(ct));
    }

    private static async Task<IResult> Deactivate(Guid id, UserManager<AppUser> users, ICurrentUser current,
        TokenService tokens, IMemoryCache cache, CancellationToken ct)
    {
        if (id == current.Id) throw AppException.Conflict("AUTO_DEGRADACION", "No puedes desactivarte a ti mismo.");
        var user = await users.FindByIdAsync(id.ToString()) ?? throw AppException.NotFound("El usuario");
        user.IsActive = false;
        (await users.UpdateAsync(user)).ThrowIfFailed();
        await users.UpdateSecurityStampAsync(user);
        await tokens.RevokeAllAsync(user.Id, ct);
        UserSessionCache.Invalidate(cache, user.Id);
        return Results.NoContent();
    }

    private static async Task<IResult> ResetPassword(Guid id, ResetPasswordRequest req, UserManager<AppUser> users,
        TokenService tokens, IMemoryCache cache, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString()) ?? throw AppException.NotFound("El usuario");
        var token = await users.GeneratePasswordResetTokenAsync(user);
        (await users.ResetPasswordAsync(user, token, req.NewPassword)).ThrowIfFailed();
        await users.SetLockoutEndDateAsync(user, null);
        await tokens.RevokeAllAsync(user.Id, ct);
        UserSessionCache.Invalidate(cache, user.Id);
        return Results.NoContent();
    }
}
