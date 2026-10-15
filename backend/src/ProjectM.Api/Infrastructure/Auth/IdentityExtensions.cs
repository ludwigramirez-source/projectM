using Microsoft.AspNetCore.Identity;
using ProjectM.Api.Infrastructure.Errors;

namespace ProjectM.Api.Infrastructure.Auth;

public static class IdentityExtensions
{
    /// <summary>Convierte errores de Identity en un 400 de validación con mensajes en español.</summary>
    public static void ThrowIfFailed(this IdentityResult result, string field = "password")
    {
        if (result.Succeeded) return;
        var errors = result.Errors
            .GroupBy(e => e.Code.Contains("Email") || e.Code.Contains("UserName") ? "email" : field)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
        throw AppException.Validation(errors);
    }
}

/// <summary>Mensajes de Identity en español.</summary>
public class SpanishIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordMismatch() => new() { Code = nameof(PasswordMismatch), Description = "La contraseña actual no es correcta." };
    public override IdentityError PasswordTooShort(int length) => new() { Code = nameof(PasswordTooShort), Description = $"La contraseña debe tener al menos {length} caracteres." };
    public override IdentityError PasswordRequiresDigit() => new() { Code = nameof(PasswordRequiresDigit), Description = "La contraseña debe incluir al menos un número." };
    public override IdentityError PasswordRequiresLower() => new() { Code = nameof(PasswordRequiresLower), Description = "La contraseña debe incluir al menos una minúscula." };
    public override IdentityError PasswordRequiresUpper() => new() { Code = nameof(PasswordRequiresUpper), Description = "La contraseña debe incluir al menos una mayúscula." };
    public override IdentityError PasswordRequiresNonAlphanumeric() => new() { Code = nameof(PasswordRequiresNonAlphanumeric), Description = "La contraseña debe incluir al menos un símbolo." };
    public override IdentityError DuplicateEmail(string email) => new() { Code = nameof(DuplicateEmail), Description = $"Ya existe un usuario con el correo {email}." };
    public override IdentityError DuplicateUserName(string userName) => new() { Code = nameof(DuplicateUserName), Description = $"Ya existe un usuario con el correo {userName}." };
    public override IdentityError InvalidEmail(string? email) => new() { Code = nameof(InvalidEmail), Description = "El correo no es válido." };
}
