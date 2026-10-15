using System.Net.Http.Json;
using FluentAssertions;
using ProjectM.IntegrationTests.Infrastructure;

namespace ProjectM.IntegrationTests;

[Collection(ApiCollection.Name)]
public class DashboardTests(ApiFactory factory)
{
    [Fact]
    public async Task Dashboard_agrega_solo_proyectos_visibles()
    {
        var s = await ProjectScenario.CreateAsync(factory); // 8 tareas de plantilla
        var listId = s.Project.GetProperty("lists").EnumerateArray().First().GetProperty("id").GetGuid();
        var overdue = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3).ToString("yyyy-MM-dd");
        await (await s.Gestor.PostAsJsonAsync("/api/v1/tasks", new { listId, title = "Atrasada", assigneeId = s.MiembroId, dueDate = overdue })).ShouldBeAsync(201);

        var d = await (await s.Gestor.GetAsync($"/api/v1/dashboard?projectId={s.ProjectId}")).JsonAsync();
        d.GetProperty("openTasks").GetInt32().Should().Be(9);
        d.GetProperty("overdueTasks").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        d.GetProperty("tasksByStatus").GetProperty("Pendiente").GetInt32().Should().Be(9);
        d.GetProperty("projectsByStatus").GetProperty("Activo").GetInt32().Should().Be(1);
        var mario = d.GetProperty("workload").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "Mario Miembro");
        mario.GetProperty("overdue").GetInt32().Should().Be(1);
        d.GetProperty("overdue").EnumerateArray().Should().Contain(t => t.GetProperty("title").GetString() == "Atrasada");

        var mine = await (await s.Miembro.GetAsync("/api/v1/dashboard")).JsonAsync();
        mine.GetProperty("myOpenTasks").GetInt32().Should().Be(1);

        var ajeno = await (await s.Ajeno.GetAsync("/api/v1/dashboard")).JsonAsync();
        ajeno.GetProperty("overdue").EnumerateArray().Should().NotContain(t => t.GetProperty("title").GetString() == "Atrasada");
        await (await s.Ajeno.GetAsync($"/api/v1/dashboard?projectId={s.ProjectId}")).ShouldBeAsync(404);
    }
}
