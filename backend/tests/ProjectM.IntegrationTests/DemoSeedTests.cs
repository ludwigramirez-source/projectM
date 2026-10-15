using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ProjectM.Api.Infrastructure.Seed;
using ProjectM.IntegrationTests.Infrastructure;

namespace ProjectM.IntegrationTests;

public class DemoApiFactory : ApiFactory
{
    public const string DemoPassword = "Demo_Test_2026";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Seed:Demo", "true");
        builder.UseSetting("Seed:DemoPassword", DemoPassword);
    }
}

public class DemoSeedTests(DemoApiFactory factory) : IClassFixture<DemoApiFactory>
{
    [Fact]
    public async Task Datos_demo_se_cargan_respetando_permisos_y_son_idempotentes()
    {
        var laura = await factory.ClientForAsync($"laura@{DemoSeeder.Domain}", DemoApiFactory.DemoPassword);
        var projects = await (await laura.GetAsync("/api/v1/projects")).JsonAsync();
        projects.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("name").GetString())
            .Should().BeEquivalentTo("Implementación CRM", "Renovación sitio web");

        var admin = await factory.AdminClientAsync();
        var all = await (await admin.GetAsync("/api/v1/projects")).JsonAsync();
        var statuses = all.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("status").GetString()).ToList();
        statuses.Should().Contain(["Planeacion", "Activo", "Cerrado"]);

        var closed = all.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("status").GetString() == "Cerrado");
        var acta = await (await admin.GetAsync($"/api/v1/projects/{closed.GetProperty("id")}/closure")).JsonAsync();
        acta.GetProperty("isForced").GetBoolean().Should().BeTrue();
        acta.GetProperty("lessons").GetArrayLength().Should().Be(2);

        var valeria = await factory.ClientForAsync($"valeria@{DemoSeeder.Domain}", DemoApiFactory.DemoPassword);
        var dash = await (await valeria.GetAsync("/api/v1/dashboard")).JsonAsync();
        dash.GetProperty("overdueTasks").GetInt32().Should().BeGreaterThan(0);

        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedAsync();
        var again = await (await admin.GetAsync("/api/v1/projects")).JsonAsync();
        again.GetProperty("totalCount").GetInt32().Should().Be(all.GetProperty("totalCount").GetInt32());
    }
}
