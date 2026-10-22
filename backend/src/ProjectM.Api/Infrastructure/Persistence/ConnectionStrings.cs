using Npgsql;

namespace ProjectM.Api.Infrastructure.Persistence;

public static class ConnectionStrings
{
    /// <summary>
    /// Acepta la cadena de PostgreSQL en formato clave=valor (Npgsql) o en formato URI
    /// (<c>postgresql://usuario:clave@host:puerto/base</c>, el que muestra Supabase en "Connect").
    /// Las URIs se convierten a formato Npgsql y exigen SSL.
    /// </summary>
    public static string NormalizePostgres(string? value)
    {
        var raw = value?.Trim() ?? "";
        if (!raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return raw;

        var uri = new Uri(raw);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')) is { Length: > 0 } db ? db : "postgres",
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            SslMode = SslMode.Require
        };

        // Parámetros extra de la URI (?sslmode=...) se respetan si Npgsql los reconoce.
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0]);
            if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase) && kv.Length == 2 &&
                Enum.TryParse<SslMode>(Uri.UnescapeDataString(kv[1]).Replace("-", ""), true, out var mode))
                builder.SslMode = mode;
        }
        return builder.ConnectionString;
    }
}
