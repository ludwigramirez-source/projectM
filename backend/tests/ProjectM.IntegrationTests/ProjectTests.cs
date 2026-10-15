using System.Net.Http.Json;
using FluentAssertions;
using ProjectM.IntegrationTests.Infrastructure;

namespace ProjectM.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ProjectTests(ApiFactory factory)
{
    [Fact]
    public async Task Apertura_con_plantilla_crea_listas_tareas_y_activa_el_proyecto()
    {
        var s = await ProjectScenario.CreateAsync(factory);
        var p = s.Project;

        p.GetProperty("status").GetString().Should().Be("Activo");
        p.GetProperty("code").GetString().Should().MatchRegex(@"^PRJ-\d{4,}$");
        p.GetProperty("clientName").GetString().Should().Be("Cliente Demo S.A.");
        p.GetProperty("actualStart").GetString().Should().NotBeNull();
        p.GetProperty("lists").EnumerateArray().Select(l => l.GetProperty("name").GetString())
            .Should().Equal("Inicio", "Planificación", "Ejecución", "Cierre");
        p.GetProperty("lists").EnumerateArray().Sum(l => l.GetProperty("taskCount").GetInt32()).Should().Be(8);
        p.GetProperty("members").EnumerateArray().Should().Contain(m => m.GetProperty("isManager").GetBoolean());
        p.GetProperty("permissions").GetProperty("canManage").GetBoolean().Should().BeTrue();

        var summary = await (await s.Gestor.GetAsync(s.Url("/summary"))).JsonAsync();
        summary.GetProperty("totalTasks").GetInt32().Should().Be(8);
        summary.GetProperty("progress").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Proyecto_sin_activar_queda_en_planeacion_con_lista_general()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false, activate: false);
        s.Project.GetProperty("status").GetString().Should().Be("Planeacion");
        s.Project.GetProperty("lists").EnumerateArray().Single().GetProperty("name").GetString().Should().Be("General");

        var activated = await (await s.Gestor.PostAsync(s.Url("/activate"), null)).JsonAsync();
        activated.GetProperty("status").GetString().Should().Be("Activo");
        await (await s.Gestor.PostAsync(s.Url("/activate"), null)).ShouldBeAsync(409, "TRANSICION_INVALIDA");
    }

    [Theory]
    [InlineData("Miembro")]
    [InlineData("Observador")]
    public async Task Solo_Admin_y_Gestor_pueden_abrir_proyectos(string role)
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var (_, client) = await factory.CreateUserAsync(role);
        var res = await client.PostAsJsonAsync("/api/v1/projects", new
        {
            spaceId = s.SpaceId, name = "No permitido", plannedStart = "2026-10-01", plannedEnd = "2026-10-31", activateNow = false
        });
        await res.ShouldBeAsync(403);
    }

    [Fact]
    public async Task Usuario_ajeno_no_ve_el_proyecto()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        await (await s.Ajeno.GetAsync(s.Url())).ShouldBeAsync(404, "NO_ENCONTRADO");
        await (await s.Ajeno.GetAsync(s.Url("/summary"))).ShouldBeAsync(404);
        var list = await (await s.Ajeno.GetAsync("/api/v1/projects")).JsonAsync();
        list.GetProperty("items").EnumerateArray().Should().NotContain(p => p.GetProperty("id").GetGuid() == s.ProjectId);
    }

    [Fact]
    public async Task Observador_y_miembro_ven_pero_no_gestionan()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        foreach (var client in new[] { s.Observador, s.Miembro, s.OtroGestor })
        {
            var p = await (await client.GetAsync(s.Url())).JsonAsync();
            p.GetProperty("permissions").GetProperty("canManage").GetBoolean().Should().BeFalse();
            await (await client.PostAsJsonAsync(s.Url("/milestones"), new { name = "Hito", dueDate = "2026-11-01" }))
                .ShouldBeAsync(403, "PROHIBIDO");
            await (await client.PostAsync(s.Url("/closure/start"), null)).ShouldBeAsync(403);
        }

        var observer = await (await s.Observador.GetAsync(s.Url())).JsonAsync();
        observer.GetProperty("permissions").GetProperty("canEditTasks").GetBoolean().Should().BeFalse();
        var member = await (await s.Miembro.GetAsync(s.Url())).JsonAsync();
        member.GetProperty("permissions").GetProperty("canEditTasks").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Edicion_con_version_desactualizada_devuelve_409()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var original = s.Project.GetProperty("rowVersion").GetString();
        object Body(string name, string? rowVersion) => new
        {
            spaceId = s.SpaceId, name, plannedStart = "2026-10-01", plannedEnd = "2026-12-31",
            managerId = s.GestorId, health = "EnRiesgo", rowVersion
        };

        var ok = await s.Gestor.PutAsJsonAsync(s.Url(), Body("Nombre nuevo", original));
        await ok.ShouldBeAsync(200);
        (await ok.JsonAsync()).GetProperty("health").GetString().Should().Be("EnRiesgo");

        await (await s.Gestor.PutAsJsonAsync(s.Url(), Body("Otro nombre", original))).ShouldBeAsync(409, "CONFLICTO_CONCURRENCIA");
    }

    [Fact]
    public async Task Fechas_invalidas_son_rechazadas()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var res = await s.Gestor.PostAsJsonAsync("/api/v1/projects", new
        {
            spaceId = s.SpaceId, name = "Fechas", plannedStart = "2026-10-31", plannedEnd = "2026-10-01", activateNow = false
        });
        await res.ShouldBeAsync(400, "VALIDACION");
        (await res.JsonAsync()).GetProperty("errors").TryGetProperty("plannedEnd", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Hitos_y_riesgos_aparecen_en_seguimiento()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        await (await s.Gestor.PostAsJsonAsync(s.Url("/milestones"), new { name = "Go-live", dueDate = "2026-12-15" })).ShouldBeAsync(201);
        await (await s.Gestor.PostAsJsonAsync(s.Url("/risks"), new
        {
            title = "Retraso del proveedor", probability = 3, impact = 3, ownerId = s.MiembroId, mitigation = "Plan B"
        })).ShouldBeAsync(201);
        await (await s.Gestor.PostAsJsonAsync(s.Url("/risks"), new { title = "Fuera del equipo", probability = 1, impact = 1, ownerId = Guid.NewGuid() }))
            .ShouldBeAsync(400, "VALIDACION");

        var summary = await (await s.Miembro.GetAsync(s.Url("/summary"))).JsonAsync();
        summary.GetProperty("openRisks").GetInt32().Should().Be(1);
        summary.GetProperty("highRisks").GetInt32().Should().Be(1);
        summary.GetProperty("milestones").GetArrayLength().Should().Be(1);

        var blockers = await (await s.Observador.GetAsync(s.Url("/blockers"))).JsonAsync();
        blockers.GetProperty("openRisks").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Arbol_de_navegacion_muestra_solo_proyectos_visibles()
    {
        var s = await ProjectScenario.CreateAsync(factory);
        var tree = await (await s.Miembro.GetAsync("/api/v1/spaces/tree")).JsonAsync();
        var project = tree.EnumerateArray().SelectMany(sp => sp.GetProperty("projects").EnumerateArray())
            .Single(p => p.GetProperty("id").GetGuid() == s.ProjectId);
        project.GetProperty("lists").GetArrayLength().Should().Be(4);

        var ajeno = await (await s.Ajeno.GetAsync("/api/v1/spaces/tree")).JsonAsync();
        ajeno.EnumerateArray().SelectMany(sp => sp.GetProperty("projects").EnumerateArray())
            .Should().NotContain(p => p.GetProperty("id").GetGuid() == s.ProjectId);
    }
}

[Collection(ApiCollection.Name)]
public class ProjectClosureTests(ApiFactory factory)
{
    [Fact]
    public async Task Cierre_completo_con_tareas_abiertas_requiere_forzar_con_justificacion()
    {
        var s = await ProjectScenario.CreateAsync(factory); // 8 tareas abiertas de la plantilla

        await (await s.Gestor.PostAsync(s.Url("/closure/start"), null)).ShouldBeAsync(200);
        await (await s.Gestor.PostAsJsonAsync(s.Url("/close"), new { force = false })).ShouldBeAsync(409, "CHECKLIST_INCOMPLETO");

        await s.PrepareClosureAsync();
        var closure = await (await s.Gestor.GetAsync(s.Url("/closure"))).JsonAsync();
        closure.GetProperty("problems").EnumerateArray().Select(p => p.GetProperty("code").GetString())
            .Should().Equal("TAREAS_ABIERTAS");

        await (await s.Gestor.PostAsJsonAsync(s.Url("/close"), new { force = false })).ShouldBeAsync(409, "TAREAS_ABIERTAS");
        await (await s.Gestor.PostAsJsonAsync(s.Url("/close"), new { force = true, justification = "corta" }))
            .ShouldBeAsync(400, "JUSTIFICACION_REQUERIDA");

        var closed = await s.Gestor.PostAsJsonAsync(s.Url("/close"),
            new { force = true, justification = "El cliente aceptó cerrar con las tareas pendientes fuera de alcance." });
        await closed.ShouldBeAsync(200);
        var acta = await closed.JsonAsync();
        acta.GetProperty("status").GetString().Should().Be("Cerrado");
        acta.GetProperty("isForced").GetBoolean().Should().BeTrue();
        acta.GetProperty("metrics").GetProperty("openTasks").GetInt32().Should().Be(8);
        acta.GetProperty("deliverables").GetArrayLength().Should().Be(2);
        acta.GetProperty("lessons").GetArrayLength().Should().Be(1);
        acta.GetProperty("closedByName").GetString().Should().Be("Gestora Principal");
        acta.GetProperty("actualEnd").GetString().Should().NotBeNull();
    }

    [Fact]
    public async Task Cierre_sin_tareas_abiertas_no_requiere_forzar()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        await s.PrepareClosureAsync();
        var res = await s.Gestor.PostAsJsonAsync(s.Url("/close"), new { force = false });
        await res.ShouldBeAsync(200);
        (await res.JsonAsync()).GetProperty("isForced").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Proyecto_cerrado_es_solo_lectura_y_solo_Admin_lo_reabre()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        await s.PrepareClosureAsync();
        await (await s.Gestor.PostAsJsonAsync(s.Url("/close"), new { force = false })).ShouldBeAsync(200);

        // Lectura permitida, escritura bloqueada incluso para el Admin.
        var detail = await (await s.Miembro.GetAsync(s.Url())).JsonAsync();
        detail.GetProperty("status").GetString().Should().Be("Cerrado");
        detail.GetProperty("permissions").GetProperty("canEditTasks").GetBoolean().Should().BeFalse();
        await (await s.Gestor.PostAsJsonAsync(s.Url("/milestones"), new { name = "x", dueDate = "2026-12-01" }))
            .ShouldBeAsync(409, "PROYECTO_CERRADO");
        await (await s.Admin.PostAsJsonAsync(s.Url("/risks"), new { title = "x", probability = 1, impact = 1 }))
            .ShouldBeAsync(409, "PROYECTO_CERRADO");
        await (await s.Gestor.PostAsync(s.Url("/pause"), null)).ShouldBeAsync(409, "PROYECTO_CERRADO");

        var adminView = await (await s.Admin.GetAsync(s.Url())).JsonAsync();
        adminView.GetProperty("permissions").GetProperty("canReopen").GetBoolean().Should().BeTrue();

        await (await s.Gestor.PostAsJsonAsync(s.Url("/reopen"), new { reason = "Necesitamos un ajuste adicional" })).ShouldBeAsync(403);
        await (await s.Admin.PostAsJsonAsync(s.Url("/reopen"), new { reason = "corto" })).ShouldBeAsync(400, "VALIDACION");

        var reopened = await s.Admin.PostAsJsonAsync(s.Url("/reopen"), new { reason = "Necesitamos un ajuste adicional" });
        await reopened.ShouldBeAsync(200);
        (await reopened.JsonAsync()).GetProperty("status").GetString().Should().Be("Activo");
        await (await s.Gestor.PostAsJsonAsync(s.Url("/milestones"), new { name = "Ajuste", dueDate = "2026-12-20" })).ShouldBeAsync(201);
    }

    [Fact]
    public async Task Cancelar_cierre_vuelve_a_activo()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        await (await s.Gestor.PostAsync(s.Url("/closure/start"), null)).ShouldBeAsync(200);
        var res = await s.Gestor.PostAsync(s.Url("/closure/cancel"), null);
        (await res.JsonAsync()).GetProperty("status").GetString().Should().Be("Activo");
    }

    [Fact]
    public async Task Checklist_de_cierre_global_solo_lo_edita_Admin()
    {
        var (_, gestor) = await factory.CreateUserAsync("Gestor");
        await (await gestor.PutAsJsonAsync("/api/v1/closure-checklist-template",
            new { items = new[] { new { text = "x", isRequired = true } } })).ShouldBeAsync(403);
        var items = await (await gestor.GetAsync("/api/v1/closure-checklist-template")).JsonAsync();
        items.GetArrayLength().Should().BeGreaterThan(0);
    }
}
