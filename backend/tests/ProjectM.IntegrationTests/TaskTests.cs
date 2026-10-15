using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ProjectM.IntegrationTests.Infrastructure;

namespace ProjectM.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TaskTests(ApiFactory factory)
{
    private static Guid FirstList(ProjectScenario s) =>
        s.Project.GetProperty("lists").EnumerateArray().First().GetProperty("id").GetGuid();

    private static async Task<JsonElement> CreateTask(HttpClient c, Guid listId, string title, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["listId"] = listId, ["title"] = title };
        if (extra is not null)
            foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        var res = await c.PostAsJsonAsync("/api/v1/tasks", body);
        await res.ShouldBeAsync(201);
        return await res.JsonAsync();
    }

    [Fact]
    public async Task Miembro_crea_tarea_completa_con_subtarea_etiquetas_y_campos()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var listId = FirstList(s);

        var field = await (await s.Gestor.PostAsJsonAsync(s.Url("/custom-fields"),
            new { name = "Esfuerzo", type = "Numero", isRequired = false })).JsonAsync();
        var select = await (await s.Gestor.PostAsJsonAsync(s.Url("/custom-fields"),
            new { name = "Ambiente", type = "Seleccion", options = new[] { "Dev", "Prod" }, isRequired = false })).JsonAsync();
        var tag = await (await s.Miembro.PostAsJsonAsync("/api/v1/tags", new { name = $"backend-{Guid.NewGuid():N}"[..20], color = "primary" })).JsonAsync();

        var task = await CreateTask(s.Miembro, listId, "Configurar servidor", new
        {
            description = "Instalar y configurar",
            assigneeId = s.MiembroId,
            priority = "Alta",
            startDate = "2026-10-10",
            dueDate = "2026-10-20",
            tagIds = new[] { tag.GetProperty("id").GetGuid() },
            customFields = new Dictionary<string, string>
            {
                [field.GetProperty("id").GetString()!] = "8",
                [select.GetProperty("id").GetString()!] = "Prod"
            }
        });
        var taskId = task.GetProperty("id").GetGuid();
        task.GetProperty("assigneeName").GetString().Should().Be("Mario Miembro");
        task.GetProperty("tags").GetArrayLength().Should().Be(1);
        task.GetProperty("customFields").EnumerateArray().Select(f => f.GetProperty("value").GetString())
            .Should().BeEquivalentTo("8", "Prod");

        await CreateTask(s.Miembro, listId, "Subtarea 1", new { parentTaskId = taskId });
        var detail = await (await s.Observador.GetAsync($"/api/v1/tasks/{taskId}")).JsonAsync();
        detail.GetProperty("subtasks").GetArrayLength().Should().Be(1);
        detail.GetProperty("canEdit").GetBoolean().Should().BeFalse();

        var list = await (await s.Miembro.GetAsync($"/api/v1/tasks?projectId={s.ProjectId}")).JsonAsync();
        var row = list.GetProperty("items").EnumerateArray().Single();
        row.GetProperty("subtaskCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Observador_no_crea_tareas_y_ajeno_no_las_ve()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var listId = FirstList(s);
        await (await s.Observador.PostAsJsonAsync("/api/v1/tasks", new { listId, title = "x" })).ShouldBeAsync(403);
        var task = await CreateTask(s.Miembro, listId, "Privada");
        await (await s.Ajeno.GetAsync($"/api/v1/tasks/{task.GetProperty("id")}")).ShouldBeAsync(404);
        await (await s.Ajeno.PostAsJsonAsync("/api/v1/tasks", new { listId, title = "x" })).ShouldBeAsync(404);
        await (await s.Ajeno.GetAsync($"/api/v1/tasks?projectId={s.ProjectId}")).ShouldBeAsync(404);
    }

    [Fact]
    public async Task Reglas_de_cambio_de_estado()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var task = await CreateTask(s.Miembro, FirstList(s), "Tarea con estados");
        var url = $"/api/v1/tasks/{task.GetProperty("id")}/status";

        await (await s.Miembro.PatchAsJsonAsync(url, new { status = "Bloqueada" })).ShouldBeAsync(400, "MOTIVO_BLOQUEO_REQUERIDO");

        var blocked = await (await s.Miembro.PatchAsJsonAsync(url, new { status = "Bloqueada", blockedReason = "Sin accesos" })).JsonAsync();
        blocked.GetProperty("blockedReason").GetString().Should().Be("Sin accesos");

        var done = await (await s.Miembro.PatchAsJsonAsync(url, new { status = "Completada" })).JsonAsync();
        done.GetProperty("completedAt").ValueKind.Should().Be(JsonValueKind.String);
        done.GetProperty("blockedReason").ValueKind.Should().Be(JsonValueKind.Null);

        var summary = await (await s.Gestor.GetAsync(s.Url("/summary"))).JsonAsync();
        summary.GetProperty("progress").GetInt32().Should().Be(100);

        var reopened = await (await s.Miembro.PatchAsJsonAsync(url, new { status = "EnProgreso" })).JsonAsync();
        reopened.GetProperty("completedAt").ValueKind.Should().Be(JsonValueKind.Null);
        await (await s.Observador.PatchAsJsonAsync(url, new { status = "Completada" })).ShouldBeAsync(403);
    }

    [Fact]
    public async Task Validaciones_de_subtareas_responsable_y_campos()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var listId = FirstList(s);
        var parent = await CreateTask(s.Miembro, listId, "Padre");
        var child = await CreateTask(s.Miembro, listId, "Hija", new { parentTaskId = parent.GetProperty("id").GetGuid() });

        var nested = await s.Miembro.PostAsJsonAsync("/api/v1/tasks", new { listId, title = "Nieta", parentTaskId = child.GetProperty("id").GetGuid() });
        await nested.ShouldBeAsync(400, "VALIDACION");

        var (outsiderId, _) = await factory.CreateUserAsync("Miembro");
        await (await s.Miembro.PostAsJsonAsync("/api/v1/tasks", new { listId, title = "x", assigneeId = outsiderId })).ShouldBeAsync(400, "VALIDACION");
        await (await s.Miembro.PostAsJsonAsync("/api/v1/tasks", new { listId, title = "x", startDate = "2026-10-10", dueDate = "2026-10-01" }))
            .ShouldBeAsync(400, "VALIDACION");

        var number = await (await s.Gestor.PostAsJsonAsync(s.Url("/custom-fields"), new { name = "Horas", type = "Numero", isRequired = false })).JsonAsync();
        var res = await s.Miembro.PutAsJsonAsync($"/api/v1/tasks/{parent.GetProperty("id")}/custom-fields/{number.GetProperty("id")}", new { value = "muchas" });
        await res.ShouldBeAsync(400, "CAMPO_INVALIDO");
        await (await s.Gestor.PostAsJsonAsync(s.Url("/custom-fields"), new { name = "Sel", type = "Seleccion", options = Array.Empty<string>(), isRequired = false }))
            .ShouldBeAsync(400, "VALIDACION");

        // Campo obligatorio: no impide crear, pero sí completar mientras esté vacío.
        var required = await (await s.Gestor.PostAsJsonAsync(s.Url("/custom-fields"), new { name = "Ticket", type = "Texto", isRequired = true })).JsonAsync();
        var quick = await CreateTask(s.Miembro, listId, "Alta rápida");
        var statusUrl = $"/api/v1/tasks/{quick.GetProperty("id")}/status";
        await (await s.Miembro.PatchAsJsonAsync(statusUrl, new { status = "Completada" })).ShouldBeAsync(400, "CAMPOS_REQUERIDOS");
        await (await s.Miembro.PutAsJsonAsync($"/api/v1/tasks/{quick.GetProperty("id")}/custom-fields/{required.GetProperty("id")}", new { value = "INC-42" })).ShouldBeAsync(200);
        await (await s.Miembro.PatchAsJsonAsync(statusUrl, new { status = "Completada" })).ShouldBeAsync(200);
        await (await s.Miembro.PutAsJsonAsync($"/api/v1/tasks/{quick.GetProperty("id")}/custom-fields/{required.GetProperty("id")}", new { value = "" }))
            .ShouldBeAsync(400, "CAMPO_REQUERIDO");
    }

    [Fact]
    public async Task Mover_reordena_y_cambia_estado()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var listId = FirstList(s);
        var a = (await CreateTask(s.Miembro, listId, "A")).GetProperty("id").GetGuid();
        var b = (await CreateTask(s.Miembro, listId, "B")).GetProperty("id").GetGuid();
        var c = (await CreateTask(s.Miembro, listId, "C")).GetProperty("id").GetGuid();

        // C entre A y B, y a la columna "En revisión".
        var moved = await (await s.Miembro.PatchAsJsonAsync($"/api/v1/tasks/{c}/move",
            new { status = "EnRevision", beforeId = a, afterId = b })).JsonAsync();
        moved.GetProperty("status").GetString().Should().Be("EnRevision");

        var list = await (await s.Miembro.GetAsync($"/api/v1/tasks?projectId={s.ProjectId}")).JsonAsync();
        list.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("title").GetString()).Should().Equal("A", "C", "B");

        // Al inicio de todo.
        await (await s.Miembro.PatchAsJsonAsync($"/api/v1/tasks/{b}/move", new { afterId = a })).ShouldBeAsync(200);
        list = await (await s.Miembro.GetAsync($"/api/v1/tasks?projectId={s.ProjectId}")).JsonAsync();
        list.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("title").GetString()).Should().Equal("B", "A", "C");
    }

    [Fact]
    public async Task Repetidos_reordenamientos_en_el_mismo_hueco_rebalancean()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var listId = FirstList(s);
        var first = (await CreateTask(s.Miembro, listId, "Primera")).GetProperty("id").GetGuid();
        var last = (await CreateTask(s.Miembro, listId, "Última")).GetProperty("id").GetGuid();
        var moving = new List<Guid>();
        for (var i = 0; i < 4; i++) moving.Add((await CreateTask(s.Miembro, listId, $"M{i}")).GetProperty("id").GetGuid());

        // Inserta siempre justo después de "Primera": reduce el hueco a la mitad cada vez.
        var before = first;
        for (var i = 0; i < 60; i++)
        {
            var id = moving[i % moving.Count];
            if (id == before) continue;
            await (await s.Miembro.PatchAsJsonAsync($"/api/v1/tasks/{id}/move", new { beforeId = first, afterId = NextAfter(first) })).ShouldBeAsync(200);
            Guid NextAfter(Guid _) => last;
            last = id;
        }
        var list = await (await s.Miembro.GetAsync($"/api/v1/tasks?projectId={s.ProjectId}")).JsonAsync();
        var positions = list.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("position").GetDouble()).ToList();
        positions.Should().OnlyHaveUniqueItems().And.BeInAscendingOrder();
    }

    [Fact]
    public async Task Edicion_concurrente_de_tarea_devuelve_409()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var t = await CreateTask(s.Miembro, FirstList(s), "Concurrente");
        var id = t.GetProperty("id").GetGuid();
        var version = t.GetProperty("rowVersion").GetString();
        object Body(string title) => new { title, priority = "Normal", rowVersion = version };

        await (await s.Miembro.PutAsJsonAsync($"/api/v1/tasks/{id}", Body("Primera edición"))).ShouldBeAsync(200);
        await (await s.Gestor.PutAsJsonAsync($"/api/v1/tasks/{id}", Body("Segunda edición"))).ShouldBeAsync(409, "CONFLICTO_CONCURRENCIA");
    }

    [Fact]
    public async Task Adjuntos_se_suben_descargan_y_validan()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var t = await CreateTask(s.Miembro, FirstList(s), "Con adjunto");
        var id = t.GetProperty("id").GetGuid();

        static MultipartFormDataContent File(string name, string content)
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", name);
            return form;
        }

        var up = await s.Miembro.PostAsync($"/api/v1/tasks/{id}/attachments", File("notas.txt", "hola mundo"));
        await up.ShouldBeAsync(201);
        var att = await up.JsonAsync();
        att.GetProperty("contentType").GetString().Should().Be("text/plain");

        var down = await s.Observador.GetAsync($"/api/v1/attachments/{att.GetProperty("id")}/download");
        await down.ShouldBeAsync(200);
        (await down.Content.ReadAsStringAsync()).Should().Be("hola mundo");
        down.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");

        await (await s.Miembro.PostAsync($"/api/v1/tasks/{id}/attachments", File("virus.exe", "MZ"))).ShouldBeAsync(400, "VALIDACION");
        await (await s.Observador.PostAsync($"/api/v1/tasks/{id}/attachments", File("x.txt", "x"))).ShouldBeAsync(403);
        await (await s.Ajeno.GetAsync($"/api/v1/attachments/{att.GetProperty("id")}/download")).ShouldBeAsync(404);

        await (await s.Miembro.DeleteAsync($"/api/v1/attachments/{att.GetProperty("id")}")).ShouldBeAsync(204);
        var detail = await (await s.Miembro.GetAsync($"/api/v1/tasks/{id}")).JsonAsync();
        detail.GetProperty("attachments").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Eliminar_tarea_oculta_sus_subtareas()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var listId = FirstList(s);
        var parent = (await CreateTask(s.Miembro, listId, "Padre")).GetProperty("id").GetGuid();
        var child = (await CreateTask(s.Miembro, listId, "Hija", new { parentTaskId = parent })).GetProperty("id").GetGuid();

        await (await s.Miembro.DeleteAsync($"/api/v1/tasks/{parent}")).ShouldBeAsync(204);
        await (await s.Miembro.GetAsync($"/api/v1/tasks/{child}")).ShouldBeAsync(404);
        var list = await (await s.Miembro.GetAsync($"/api/v1/tasks?projectId={s.ProjectId}&includeSubtasks=true")).JsonAsync();
        list.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Mis_tareas_filtra_por_responsable_en_todos_los_proyectos()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var listId = FirstList(s);
        await CreateTask(s.Gestor, listId, "Para Mario", new { assigneeId = s.MiembroId });
        await CreateTask(s.Gestor, listId, "Para nadie");
        var mine = await (await s.Miembro.GetAsync("/api/v1/tasks?mine=true")).JsonAsync();
        mine.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("title").GetString()).Should().Equal("Para Mario");
    }

    [Fact]
    public async Task Proyecto_cerrado_bloquea_cambios_en_tareas_y_cierre_normal_tras_completar()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var task = await CreateTask(s.Miembro, FirstList(s), "Única");
        var id = task.GetProperty("id").GetGuid();

        await s.PrepareClosureAsync();
        await (await s.Gestor.PostAsJsonAsync(s.Url("/close"), new { force = false })).ShouldBeAsync(409, "TAREAS_ABIERTAS");

        await (await s.Miembro.PatchAsJsonAsync($"/api/v1/tasks/{id}/status", new { status = "Completada" })).ShouldBeAsync(200);
        var closed = await s.Gestor.PostAsJsonAsync(s.Url("/close"), new { force = false });
        await closed.ShouldBeAsync(200);
        (await closed.JsonAsync()).GetProperty("metrics").GetProperty("progress").GetInt32().Should().Be(100);

        await (await s.Miembro.PatchAsJsonAsync($"/api/v1/tasks/{id}/status", new { status = "EnProgreso" })).ShouldBeAsync(409, "PROYECTO_CERRADO");
        await (await s.Miembro.PostAsJsonAsync("/api/v1/tasks", new { listId = FirstList(s), title = "Nueva" })).ShouldBeAsync(409, "PROYECTO_CERRADO");
        await (await s.Miembro.GetAsync($"/api/v1/tasks/{id}")).ShouldBeAsync(200);
    }
}
