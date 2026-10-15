using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using ProjectM.IntegrationTests.Infrastructure;

namespace ProjectM.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Login_valido_devuelve_token_y_cookie_de_refresh_httponly()
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });

        await res.ShouldBeAsync(200);
        var body = await res.JsonAsync();
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("user").GetProperty("role").GetString().Should().Be("Admin");

        var cookie = res.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("pm_refresh="));
        cookie.Should().Contain("httponly").And.Contain("samesite=strict").And.Contain("path=/api/v1/auth");
    }

    [Fact]
    public async Task Login_con_contrasena_incorrecta_devuelve_401_generico()
    {
        var res = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email = ApiFactory.AdminEmail, password = "incorrecta-123" });
        await res.ShouldBeAsync(401, "CREDENCIALES_INVALIDAS");

        var unknown = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email = "nadie@test.local", password = "incorrecta-123" });
        await unknown.ShouldBeAsync(401, "CREDENCIALES_INVALIDAS");
    }

    [Fact]
    public async Task Login_invalido_devuelve_errores_de_validacion_por_campo()
    {
        var res = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = "no-es-correo", password = "" });
        await res.ShouldBeAsync(400, "VALIDACION");
        var errors = (await res.JsonAsync()).GetProperty("errors");
        errors.TryGetProperty("email", out _).Should().BeTrue();
        errors.TryGetProperty("password", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Cuenta_se_bloquea_tras_5_intentos_fallidos()
    {
        var admin = await factory.AdminClientAsync();
        var email = $"bloqueo-{Guid.NewGuid():N}@test.local";
        (await admin.PostAsJsonAsync("/api/v1/users",
            new { email, fullName = "Bloqueo", role = "Miembro", password = "Correcta_123" })).EnsureSuccessStatusCode();

        var client = factory.CreateClient();
        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Mala_clave_1" });

        var res = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Correcta_123" });
        await res.ShouldBeAsync(423, "CUENTA_BLOQUEADA");
    }

    [Fact]
    public async Task Refresh_rota_el_token_y_reutilizar_el_anterior_revoca_la_sesion()
    {
        var client = factory.CreateClient(new() { HandleCookies = false });
        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        var first = RefreshCookie(login);

        var r1 = await Refresh(client, first);
        await r1.ShouldBeAsync(200);
        var second = RefreshCookie(r1);
        second.Should().NotBe(first);

        // Reutilizar el token ya rotado => 401 y se revocan todas las sesiones (incluida la nueva).
        await (await Refresh(client, first)).ShouldBeAsync(401);
        await (await Refresh(client, second)).ShouldBeAsync(401);
    }

    [Fact]
    public async Task Logout_revoca_el_refresh_token()
    {
        var client = factory.CreateClient(new() { HandleCookies = false });
        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        var token = RefreshCookie(login);

        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Add("Cookie", $"pm_refresh={token}");
        await (await client.SendAsync(logout)).ShouldBeAsync(204);

        await (await Refresh(client, token)).ShouldBeAsync(401);
    }

    [Fact]
    public async Task Endpoints_protegidos_requieren_autenticacion()
    {
        var res = await factory.CreateClient().GetAsync("/api/v1/auth/me");
        await res.ShouldBeAsync(401);
    }

    [Fact]
    public async Task Me_devuelve_el_usuario_actual()
    {
        var (id, client) = await factory.CreateUserAsync("Gestor", "Gina Gestora");
        var me = await (await client.GetAsync("/api/v1/auth/me")).JsonAsync();
        me.GetProperty("id").GetGuid().Should().Be(id);
        me.GetProperty("role").GetString().Should().Be("Gestor");
        me.GetProperty("fullName").GetString().Should().Be("Gina Gestora");
    }

    [Fact]
    public async Task Cambiar_contrasena_exige_la_actual()
    {
        var (_, client) = await factory.CreateUserAsync("Miembro");
        var res = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = "No_es_la_actual_1", newPassword = "Nueva_Clave_123" });
        await res.ShouldBeAsync(400, "VALIDACION");

        var ok = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = "Usuario_Test_123", newPassword = "Nueva_Clave_123" });
        await ok.ShouldBeAsync(204);
    }

    private static string RefreshCookie(HttpResponseMessage res)
    {
        var header = res.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("pm_refresh="));
        return header.Split(';')[0]["pm_refresh=".Length..];
    }

    private static Task<HttpResponseMessage> Refresh(HttpClient client, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        req.Headers.Add("Cookie", $"pm_refresh={token}");
        return client.SendAsync(req);
    }
}

public class RateLimitTests(RateLimitedApiFactory factory) : IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task Login_aplica_rate_limiting_por_ip()
    {
        var client = factory.CreateClient();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 4; i++)
            last = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "x@test.local", password = "Clave_12345" });

        await last!.ShouldBeAsync(429, "DEMASIADAS_SOLICITUDES");
    }
}

[Collection(ApiCollection.Name)]
public class UserAdminTests(ApiFactory factory)
{
    [Theory]
    [InlineData("Gestor")]
    [InlineData("Miembro")]
    [InlineData("Observador")]
    public async Task Solo_Admin_gestiona_usuarios(string role)
    {
        var (_, client) = await factory.CreateUserAsync(role);
        await (await client.GetAsync("/api/v1/users")).ShouldBeAsync(403);
        var create = await client.PostAsJsonAsync("/api/v1/users",
            new { email = "otro@test.local", fullName = "Otro", role = "Admin", password = "Clave_Segura_1" });
        await create.ShouldBeAsync(403);
    }

    [Fact]
    public async Task Admin_lista_usuarios_paginados_y_filtra_por_rol()
    {
        await factory.CreateUserAsync("Observador");
        var admin = await factory.AdminClientAsync();
        var page = await (await admin.GetAsync("/api/v1/users?role=Observador&pageSize=2")).JsonAsync();
        page.GetProperty("pageSize").GetInt32().Should().Be(2);
        page.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(0);
        page.GetProperty("items").EnumerateArray()
            .Should().OnlyContain(u => u.GetProperty("role").GetString() == "Observador");
    }

    [Fact]
    public async Task Contrasena_debil_es_rechazada_en_espanol()
    {
        var admin = await factory.AdminClientAsync();
        var res = await admin.PostAsJsonAsync("/api/v1/users",
            new { email = $"debil-{Guid.NewGuid():N}@test.local", fullName = "Débil", role = "Miembro", password = "solominusculas" });
        await res.ShouldBeAsync(400, "VALIDACION");
        (await res.Content.ReadAsStringAsync()).Should().Contain("mayúscula");
    }

    [Fact]
    public async Task Usuario_desactivado_no_puede_iniciar_sesion_y_su_token_deja_de_valer()
    {
        var (id, client) = await factory.CreateUserAsync("Miembro");
        await (await client.GetAsync("/api/v1/auth/me")).ShouldBeAsync(200);

        var admin = await factory.AdminClientAsync();
        await (await admin.PostAsync($"/api/v1/users/{id}/deactivate", null)).ShouldBeAsync(204);

        await (await client.GetAsync("/api/v1/auth/me")).ShouldBeAsync(401);
    }

    [Fact]
    public async Task Admin_no_puede_desactivarse_a_si_mismo()
    {
        var admin = await factory.AdminClientAsync();
        var me = await (await admin.GetAsync("/api/v1/auth/me")).JsonAsync();
        var res = await admin.PostAsync($"/api/v1/users/{me.GetProperty("id").GetGuid()}/deactivate", null);
        await res.ShouldBeAsync(409, "AUTO_DEGRADACION");
    }

    [Fact]
    public async Task Cambio_de_rol_invalida_el_token_anterior()
    {
        var (id, client) = await factory.CreateUserAsync("Miembro");
        var admin = await factory.AdminClientAsync();
        var me = await (await client.GetAsync("/api/v1/auth/me")).JsonAsync();
        var res = await admin.PutAsJsonAsync($"/api/v1/users/{id}", new
        {
            email = me.GetProperty("email").GetString(),
            fullName = "Ahora Gestor",
            role = "Gestor",
            isActive = true
        });
        await res.ShouldBeAsync(200);
        (await res.JsonAsync()).GetProperty("role").GetString().Should().Be("Gestor");

        // El token emitido con el rol anterior queda inválido de inmediato.
        await (await client.GetAsync("/api/v1/auth/me")).ShouldBeAsync(401);
    }
}
