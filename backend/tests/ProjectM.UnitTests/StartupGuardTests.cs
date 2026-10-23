using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ProjectM.Api.Infrastructure;

namespace ProjectM.UnitTests;

public class StartupGuardTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "ProjectM.Api";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Theory]
    [InlineData("Jwt:Key", "reemplazar-por-una-clave-aleatoria-larga-de-al-menos-32-caracteres")]
    [InlineData("Seed:AdminPassword", "Admin_Cambiar_123!")]
    [InlineData("Seed:DemoPassword", "Demo_Cambiar_123!")]
    public void Rechaza_valores_de_ejemplo_del_repositorio_en_produccion(string key, string value)
    {
        var act = () => StartupGuard.RejectPublicPlaceholders(Config((key, value)), new Env("Production"));
        act.Should().Throw<InvalidOperationException>().WithMessage($"{key}*");
    }

    [Fact]
    public void Rechaza_la_contrasena_de_base_de_datos_de_ejemplo()
    {
        var config = Config(("ConnectionStrings:Default", "Host=db;Username=postgres;Password=Cambiar_Esta_Clave_123"));
        var act = () => StartupGuard.RejectPublicPlaceholders(config, new Env("Production"));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Permite_valores_propios_en_produccion()
    {
        var config = Config(("Jwt:Key", Convert.ToBase64String(new byte[48])), ("Seed:AdminPassword", "Una_Clave_Propia_2026"));
        var act = () => StartupGuard.RejectPublicPlaceholders(config, new Env("Production"));
        act.Should().NotThrow();
    }

    [Fact]
    public void En_Development_no_bloquea()
    {
        var act = () => StartupGuard.RejectPublicPlaceholders(Config(("Seed:AdminPassword", "Admin_Cambiar_123!")), new Env("Development"));
        act.Should().NotThrow();
    }
}
