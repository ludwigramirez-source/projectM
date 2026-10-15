using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

namespace ProjectM.IntegrationTests.Infrastructure;

/// <summary>
/// Levanta la API contra una base de datos efímera en el SQL Server del compose
/// (variable TEST_SQL_CONNECTION). La BD se elimina al terminar.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin_Test_123";

    private readonly string _dbName = $"ProjectM_Test_{Guid.NewGuid():N}";
    private static string BaseConnection =>
        Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION")
        ?? "Server=localhost,1433;User Id=sa;Password=Your_password123;TrustServerCertificate=True";

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
        builder.UseSetting("Storage:AttachmentsPath", Path.Combine(Path.GetTempPath(), _dbName));
    }

    public Task InitializeAsync()
    {
        _ = Server; // fuerza el arranque (migraciones + seed)
        return Task.CompletedTask;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(BaseConnection);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"IF DB_ID('{_dbName}') IS NOT NULL BEGIN ALTER DATABASE [{_dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_dbName}]; END";
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

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
