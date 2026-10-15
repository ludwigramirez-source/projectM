using Microsoft.Extensions.Caching.Memory;

namespace ProjectM.Api.Infrastructure.Auth;

/// <summary>Caché corta (30 s) del estado de sesión del usuario usada al validar cada JWT.</summary>
public static class UserSessionCache
{
    public static string Key(Guid userId) => $"user-state:{userId}";

    /// <summary>Invalida de inmediato tras desactivar, cambiar rol o contraseña.</summary>
    public static void Invalidate(IMemoryCache cache, Guid userId) => cache.Remove(Key(userId));
}
