using Npgsql;

namespace ProjectM.Api.Infrastructure.Persistence;

public static class ConnectionStrings
{
    /// <summary>CA raíz de Supabase incluida en la imagen (certificado público; huella SHA-256 80:70:25:AD…:CA:FA).</summary>
    public const string SupabaseRootCertificate = "certs/supabase-prod-ca-2021.crt";

    /// <summary>
    /// Acepta la cadena de PostgreSQL en formato clave=valor (Npgsql) o URI
    /// (<c>postgresql://usuario:clave@host:puerto/base</c>, el que muestra Supabase en "Connect") y aplica la
    /// política de TLS:
    /// <list type="bullet">
    /// <item>Hosts locales (servicio <c>db</c> de Compose, localhost): sin cambios.</item>
    /// <item>Hosts remotos: nunca menos que <c>SSL Mode=Require</c> (no se permite rebajar a texto plano).</item>
    /// <item>Si hay CA raíz disponible (<paramref name="rootCertificate"/> o la de Supabase para *.supabase.com):
    /// <c>VerifyFull</c>, que valida la cadena y el nombre del servidor (evita MITM).</item>
    /// </list>
    /// </summary>
    public static string NormalizePostgres(string? value, string? rootCertificate = null)
    {
        var raw = value?.Trim() ?? "";
        if (raw.Length == 0) return raw;

        var builder = IsUri(raw) ? FromUri(raw) : new NpgsqlConnectionStringBuilder(raw);
        if (IsLocalHost(builder.Host)) return IsUri(raw) ? builder.ConnectionString : raw;

        if (builder.SslMode < SslMode.Require) builder.SslMode = SslMode.Require;

        var ca = ResolveRootCertificate(builder.Host, rootCertificate);
        if (ca is not null)
        {
            builder.SslMode = SslMode.VerifyFull;
            builder.RootCertificate = ca;
        }
        return builder.ConnectionString;
    }

    private static bool IsUri(string raw) =>
        raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
        raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

    private static NpgsqlConnectionStringBuilder FromUri(string raw)
    {
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

        // ?sslmode= de la URI: en hosts remotos solo puede subir el nivel (verify-ca / verify-full);
        // en hosts locales se respeta tal cual (desarrollo).
        var local = IsLocalHost(uri.Host);
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (Uri.UnescapeDataString(kv[0]).Equals("sslmode", StringComparison.OrdinalIgnoreCase) && kv.Length == 2 &&
                Enum.TryParse<SslMode>(Uri.UnescapeDataString(kv[1]).Replace("-", ""), true, out var mode) &&
                (local || mode > builder.SslMode))
                builder.SslMode = mode;
        }
        return builder;
    }

    private static bool IsLocalHost(string? host) =>
        string.IsNullOrEmpty(host) ||
        host.Equals("db", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host is "127.0.0.1" or "::1" ||
        host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveRootCertificate(string? host, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        if (host is null || !host.EndsWith(".supabase.com", StringComparison.OrdinalIgnoreCase)) return null;
        var bundled = Path.Combine(AppContext.BaseDirectory, SupabaseRootCertificate);
        return File.Exists(bundled) ? bundled : null;
    }
}
