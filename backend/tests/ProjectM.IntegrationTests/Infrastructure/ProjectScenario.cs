using System.Net.Http.Json;
using System.Text.Json;

namespace ProjectM.IntegrationTests.Infrastructure;

/// <summary>
/// Escenario típico: un espacio y un proyecto activo gestionado por un Gestor, con un Miembro y
/// un Observador en el equipo, más un Miembro ajeno y otro Gestor miembro (no responsable).
/// </summary>
public class ProjectScenario
{
    public required HttpClient Admin { get; init; }
    public required HttpClient Gestor { get; init; }
    public required Guid GestorId { get; init; }
    public required HttpClient OtroGestor { get; init; }
    public required HttpClient Miembro { get; init; }
    public required Guid MiembroId { get; init; }
    public required HttpClient Observador { get; init; }
    public required HttpClient Ajeno { get; init; }
    public required Guid SpaceId { get; init; }
    public required Guid ProjectId { get; init; }
    public required JsonElement Project { get; init; }

    public static async Task<ProjectScenario> CreateAsync(ApiFactory f, bool withTemplate = true, bool activate = true)
    {
        var admin = await f.AdminClientAsync();
        var (gestorId, gestor) = await f.CreateUserAsync("Gestor", "Gestora Principal");
        var (otroGestorId, otroGestor) = await f.CreateUserAsync("Gestor", "Otro Gestor");
        var (miembroId, miembro) = await f.CreateUserAsync("Miembro", "Mario Miembro");
        var (observadorId, observador) = await f.CreateUserAsync("Observador", "Olga Observadora");
        var (_, ajeno) = await f.CreateUserAsync("Miembro", "Alguien Ajeno");

        var space = await (await admin.PostAsJsonAsync("/api/v1/spaces",
            new { name = $"Espacio {Guid.NewGuid():N}", color = "primary" })).JsonAsync();
        var spaceId = space.GetProperty("id").GetGuid();

        Guid? templateId = null;
        if (withTemplate)
        {
            var templates = await (await gestor.GetAsync("/api/v1/project-templates")).JsonAsync();
            templateId = templates.EnumerateArray().First(t => t.GetProperty("name").GetString() == "Proyecto estándar")
                .GetProperty("id").GetGuid();
        }

        var res = await gestor.PostAsJsonAsync("/api/v1/projects", new
        {
            spaceId,
            name = "Implementación ERP",
            clientName = "Cliente Demo S.A.",
            objective = "Implementar el ERP",
            scope = "Módulos de finanzas e inventario",
            plannedStart = "2026-10-01",
            plannedEnd = "2026-12-31",
            budget = 50000,
            currency = "USD",
            memberIds = new[] { miembroId, observadorId, otroGestorId },
            templateId,
            activateNow = activate
        });
        await res.ShouldBeAsync(201);
        var project = await res.JsonAsync();

        return new ProjectScenario
        {
            Admin = admin, Gestor = gestor, GestorId = gestorId, OtroGestor = otroGestor, Miembro = miembro,
            MiembroId = miembroId, Observador = observador, Ajeno = ajeno, SpaceId = spaceId,
            ProjectId = project.GetProperty("id").GetGuid(), Project = project
        };
    }

    public string Url(string suffix = "") => $"/api/v1/projects/{ProjectId}{suffix}";

    /// <summary>Lleva el proyecto a "En cierre" con checklist obligatorio completo y acta con resumen.</summary>
    public async Task PrepareClosureAsync()
    {
        var closure = await (await Gestor.GetAsync(Url("/closure"))).JsonAsync();
        if (closure.GetProperty("status").GetString() != "EnCierre")
        {
            await (await Gestor.PostAsync(Url("/closure/start"), null)).ShouldBeAsync(200);
            closure = await (await Gestor.GetAsync(Url("/closure"))).JsonAsync();
        }
        foreach (var item in closure.GetProperty("checklist").EnumerateArray().Where(i => i.GetProperty("isRequired").GetBoolean()))
        {
            await (await Gestor.PutAsJsonAsync(Url($"/closure/checklist/{item.GetProperty("id").GetGuid()}"),
                new { isDone = true, notes = (string?)null })).ShouldBeAsync(200);
        }
        await (await Gestor.PutAsJsonAsync(Url("/closure"), new
        {
            summary = "Se implementaron los módulos acordados.",
            deliverables = new[] { "Manual de usuario", "Sistema en producción" },
            lessons = new[] { new { category = "Planificación", whatWentWell = "Buena comunicación", whatToImprove = "Estimaciones", recommendation = "Usar buffers" } }
        })).ShouldBeAsync(200);
    }
}
