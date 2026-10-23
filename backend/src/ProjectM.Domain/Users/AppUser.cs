using Microsoft.AspNetCore.Identity;

namespace ProjectM.Domain.Users;

/// <summary>Usuario del sistema (ASP.NET Identity). El rol global vive en las tablas de Identity.</summary>
public class AppUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Debe cambiar la contraseña antes de usar la aplicación (contraseña asignada por otra persona).</summary>
    public bool MustChangePassword { get; set; }
}

public class AppRole : IdentityRole<Guid>
{
    public AppRole() { }
    public AppRole(string name) : base(name) { }
}

/// <summary>Roles globales del sistema.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Gestor = "Gestor";
    public const string Miembro = "Miembro";
    public const string Observador = "Observador";

    public static readonly string[] All = [Admin, Gestor, Miembro, Observador];
}

/// <summary>Refresh token rotativo. Solo se guarda el hash SHA-256 del token.</summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedById { get; set; }
    public string? CreatedByIp { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
