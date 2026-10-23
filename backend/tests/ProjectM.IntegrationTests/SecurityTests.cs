using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectM.Domain.Users;
using ProjectM.IntegrationTests.Infrastructure;

namespace ProjectM.IntegrationTests;

/// <summary>Controles de la auditoría de seguridad (docs/SECURITY_AUDIT.md): SEC-01, 02, 03, 04, 07.</summary>
public class ForcedPasswordChangeTests(ProductionAuthApiFactory factory) : IClassFixture<ProductionAuthApiFactory>
{
    private async Task<(HttpClient Client, string Token, System.Text.Json.JsonElement Body)> LoginAsync(string email, string password)
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        await res.ShouldBeAsync(200);
        var body = await res.JsonAsync();
        var token = body.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, token, body);
    }

    [Fact]
    public async Task Admin_sembrado_debe_cambiar_la_contrasena_antes_de_usar_la_API()
    {
        var (client, _, body) = await LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        body.GetProperty("user").GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();

        // Todo fuera de /auth está bloqueado...
        await (await client.GetAsync("/api/v1/projects")).ShouldBeAsync(403, "CAMBIO_CONTRASENA_REQUERIDO");
        await (await client.GetAsync("/api/v1/users")).ShouldBeAsync(403, "CAMBIO_CONTRASENA_REQUERIDO");
        // ...salvo ver la sesión y cambiar la contraseña.
        await (await client.GetAsync("/api/v1/auth/me")).ShouldBeAsync(200);

        var changed = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = ApiFactory.AdminPassword, newPassword = "Admin_Nueva_Clave_2026" });
        await changed.ShouldBeAsync(200);
        var renewed = await changed.JsonAsync();
        renewed.GetProperty("user").GetProperty("mustChangePassword").GetBoolean().Should().BeFalse();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", renewed.GetProperty("accessToken").GetString());
        await (await client.GetAsync("/api/v1/projects")).ShouldBeAsync(200);

        // Un usuario creado por el Admin también debe cambiarla (el Admin conoce su contraseña inicial).
        var email = $"nuevo-{Guid.NewGuid():N}@test.local";
        (await client.PostAsJsonAsync("/api/v1/users",
            new { email, fullName = "Nuevo", role = "Miembro", password = "Inicial_Clave_123" })).EnsureSuccessStatusCode();
        var (member, _, memberBody) = await LoginAsync(email, "Inicial_Clave_123");
        memberBody.GetProperty("user").GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
        await (await member.GetAsync("/api/v1/dashboard")).ShouldBeAsync(403, "CAMBIO_CONTRASENA_REQUERIDO");
    }

    [Fact]
    public async Task Refresh_concurrente_con_el_mismo_token_no_bifurca_la_sesion()
    {
        var email = $"carrera-{Guid.NewGuid():N}@test.local";
        await CreateActiveUserAsync(email, "Clave_Carrera_123");

        var client = factory.CreateClient(new() { HandleCookies = false });
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Clave_Carrera_123" });
        var token = RefreshCookie(login);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Refresh(client, token)));
        var codes = results.Select(r => (int)r.StatusCode).ToList();
        codes.Count(c => c == 200).Should().Be(1, "solo una petición puede rotar el token");
        codes.Where(c => c != 200).Should().OnlyContain(c => c == 409, "las demás son una carrera benigna (REFRESH_EN_CURSO)");

        // La sesión ganadora sigue viva (no se revocó todo por la carrera).
        var winner = RefreshCookie(results.Single(r => (int)r.StatusCode == 200));
        await (await Refresh(client, winner)).ShouldBeAsync(200);
    }

    /// <summary>Crea un usuario activo sin cambio obligatorio (directo con Identity, sin pasar por la API).</summary>
    private async Task CreateActiveUserAsync(string email, string password)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser
        {
            Id = Guid.NewGuid(), Email = email, UserName = email, FullName = "Carrera", EmailConfirmed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        await users.AddToRoleAsync(user, "Miembro");
    }

    private static string RefreshCookie(HttpResponseMessage res) =>
        res.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("pm_refresh=")).Split(';')[0]["pm_refresh=".Length..];

    private static Task<HttpResponseMessage> Refresh(HttpClient client, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        req.Headers.Add("Cookie", $"pm_refresh={token}");
        return client.SendAsync(req);
    }
}

public class GlobalLoginLimitTests(GlobalLoginLimitApiFactory factory) : IClassFixture<GlobalLoginLimitApiFactory>
{
    [Fact]
    public async Task Login_tiene_un_tope_global_aunque_cambie_la_IP()
    {
        // Cada intento simula una IP distinta (X-Forwarded-For); el límite por IP no alcanza, el global sí.
        HttpResponseMessage? last = null;
        for (var i = 0; i < 4; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = JsonContent.Create(new { email = "spray@test.local", password = "Clave_12345" })
            };
            req.Headers.Add("X-Forwarded-For", $"203.0.113.{i + 1}");
            last = await factory.CreateClient().SendAsync(req);
        }
        await last!.ShouldBeAsync(429, "DEMASIADAS_SOLICITUDES");
    }
}

public class UserLimitsTests(UserLimitsApiFactory factory) : IClassFixture<UserLimitsApiFactory>
{
    [Fact]
    public async Task Consultas_costosas_y_escrituras_tienen_limite_por_usuario()
    {
        var (_, client) = await factory.CreateUserAsync("Miembro");
        await (await client.GetAsync("/api/v1/dashboard")).ShouldBeAsync(200);
        await (await client.GetAsync("/api/v1/dashboard")).ShouldBeAsync(200);
        await (await client.GetAsync("/api/v1/dashboard")).ShouldBeAsync(429, "DEMASIADAS_SOLICITUDES");

        // El límite es por usuario: otro usuario no se ve afectado.
        var (_, other) = await factory.CreateUserAsync("Miembro");
        await (await other.GetAsync("/api/v1/dashboard")).ShouldBeAsync(200);
    }
}

public class ProxySecretTests(ProxySecretApiFactory factory) : IClassFixture<ProxySecretApiFactory>
{
    [Fact]
    public async Task Login_directo_sin_el_encabezado_del_proxy_es_rechazado()
    {
        var direct = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        await direct.ShouldBeAsync(403);

        var viaProxy = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })
        };
        viaProxy.Headers.Add("X-Proxy-Secret", ProxySecretApiFactory.Secret);
        await (await factory.CreateClient().SendAsync(viaProxy)).ShouldBeAsync(200);
    }

    [Fact]
    public async Task El_resto_de_la_API_sigue_accesible_directamente()
    {
        // /health y los endpoints no-auth (SignalR, adjuntos) no requieren el encabezado.
        await (await factory.CreateClient().GetAsync("/health")).ShouldBeAsync(200);
        await (await factory.CreateClient().GetAsync("/api/v1/projects")).ShouldBeAsync(401);
    }
}
