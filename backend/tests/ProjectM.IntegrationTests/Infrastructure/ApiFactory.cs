using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace ProjectM.IntegrationTests.Infrastructure;

/// <summary>
/// Levanta la API contra una base de datos efímera en el PostgreSQL del compose
/// (variable TEST_SQL_CONNECTION, conexión al servidor sin base). La BD se elimina al terminar.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin_Test_123";

    private readonly string _dbName = $"projectm_test_{Guid.NewGuid():N}";
    private static string BaseConnection =>
        Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION")
        ?? throw new InvalidOperationException("Define TEST_SQL_CONNECTION (usa scripts/test.ps1, que la toma de .env).");

    public string ConnectionString => $"{BaseConnection};Database={_dbName}";

    /// <summary>Límite de intentos de login por minuto (alto por defecto para no interferir).</summary>
    protected virtual int LoginPermitLimit => 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Jwt:Key", "clave-de-pruebas-de-integracion-suficientemente-larga-1234567890");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("Seed:Demo", "false");
        builder.UseSetting("Auth:SecureCookies", "false");
        builder.UseSetting("Auth:LoginPermitLimitPerMinute", LoginPermitLimit.ToString());
        // Valores neutros para no interferir con el resto de pruebas; las fábricas especializadas los cambian.
        builder.UseSetting("Auth:LoginGlobalPermitLimitPerMinute", "100000");
        builder.UseSetting("Auth:UserWritePermitLimitPerMinute", "100000");
        builder.UseSetting("Auth:UploadPermitLimitPer10Minutes", "100000");
        builder.UseSetting("Auth:RefreshReuseGraceSeconds", "0");
        builder.UseSetting("Auth:ForcePasswordChange", "false");
        builder.UseSetting("Notifications:DueDateJob", "false");
        builder.UseSetting("Storage:AttachmentsPath", Path.Combine(Path.GetTempPath(), _dbName));
        foreach (var (key, value) in Overrides()) builder.UseSetting(key, value);
    }

    /// <summary>Configuración adicional de las fábricas especializadas (se aplica al final).</summary>
    protected virtual IEnumerable<(string Key, string Value)> Overrides() => [];

    public Task InitializeAsync()
    {
        _ = Server; // fuerza el arranque (migraciones + seed)
        return Task.CompletedTask;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection($"{BaseConnection};Database=postgres");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE)";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Cliente autenticado como el usuario indicado.</summary>
    public async Task<HttpClient> ClientForAsync(string email, string password)
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<LoginBody>(Json.Options);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return client;
    }

    public Task<HttpClient> AdminClientAsync() => ClientForAsync(AdminEmail, AdminPassword);

    /// <summary>Crea un usuario con el rol indicado y devuelve (id, cliente autenticado).</summary>
    public async Task<(Guid Id, HttpClient Client)> CreateUserAsync(string role, string? name = null)
    {
        var admin = await AdminClientAsync();
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@test.local";
        const string password = "Usuario_Test_123";
        var res = await admin.PostAsJsonAsync("/api/v1/users",
            new { email, fullName = name ?? $"{role} de prueba", role, password });
        res.EnsureSuccessStatusCode();
        var user = await res.Content.ReadFromJsonAsync<IdBody>(Json.Options);
        return (user!.Id, await ClientForAsync(email, password));
    }

    public record LoginBody(string AccessToken);
    public record IdBody(Guid Id);
}

public class RateLimitedApiFactory : ApiFactory
{
    protected override int LoginPermitLimit => 3;
}

/// <summary>Límite global de login bajo con el límite por IP alto (simula IPs rotadas o falsificadas).</summary>
public class GlobalLoginLimitApiFactory : ApiFactory
{
    protected override IEnumerable<(string Key, string Value)> Overrides() =>
        [("Auth:LoginGlobalPermitLimitPerMinute", "3")];
}

/// <summary>Límite bajo de escrituras y subidas por usuario.</summary>
public class UserLimitsApiFactory : ApiFactory
{
    protected override IEnumerable<(string Key, string Value)> Overrides() =>
        [("Auth:UserWritePermitLimitPerMinute", "2"), ("Auth:UploadPermitLimitPer10Minutes", "1")];
}

/// <summary>Configuración de producción para sesiones: cambio de contraseña obligatorio y margen de refresh.</summary>
public class ProductionAuthApiFactory : ApiFactory
{
    protected override IEnumerable<(string Key, string Value)> Overrides() =>
        [("Auth:ForcePasswordChange", "true"), ("Auth:RefreshReuseGraceSeconds", "30")];
}

/// <summary>Login solo a través del proxy del frontend (encabezado secreto).</summary>
public class ProxySecretApiFactory : ApiFactory
{
    public const string Secret = "secreto-del-proxy-de-pruebas";
    protected override IEnumerable<(string Key, string Value)> Overrides() => [("Proxy:SharedSecret", Secret)];
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
