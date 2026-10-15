using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using ProjectM.Api.Infrastructure.Notifications;
using ProjectM.IntegrationTests.Infrastructure;

namespace ProjectM.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CollaborationTests(ApiFactory factory)
{
    private static Guid FirstList(ProjectScenario s) =>
        s.Project.GetProperty("lists").EnumerateArray().First().GetProperty("id").GetGuid();

    private static async Task<Guid> NewTask(HttpClient c, Guid listId, string title, Guid? assigneeId = null, string? dueDate = null)
    {
        var res = await c.PostAsJsonAsync("/api/v1/tasks", new { listId, title, assigneeId, dueDate });
        await res.ShouldBeAsync(201);
        return (await res.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<List<JsonElement>> Notifications(HttpClient c) =>
        (await (await c.GetAsync("/api/v1/notifications")).JsonAsync()).GetProperty("items").EnumerateArray().ToList();

    [Fact]
    public async Task Mencion_en_comentario_notifica_al_mencionado_y_no_al_autor()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var taskId = await NewTask(s.Gestor, FirstList(s), "Revisar contrato");

        var res = await s.Gestor.PostAsJsonAsync($"/api/v1/tasks/{taskId}/comments",
            new { body = "@Mario Miembro ¿puedes revisarlo hoy?", mentionIds = new[] { s.MiembroId, Guid.NewGuid() } });
        await res.ShouldBeAsync(201);
        var comment = await res.JsonAsync();
        comment.GetProperty("mentions").EnumerateArray().Select(x => x.GetGuid()).Should().Equal(s.MiembroId);

        var mine = await Notifications(s.Miembro);
        mine.Should().Contain(n => n.GetProperty("type").GetString() == "Mention" &&
                                   n.GetProperty("link").GetString()!.Contains(taskId.ToString()));
        (await Notifications(s.Gestor)).Should().NotContain(n => n.GetProperty("type").GetString() == "Mention");

        var count = await (await s.Miembro.GetAsync("/api/v1/notifications/unread-count")).JsonAsync();
        count.GetProperty("count").GetInt32().Should().BeGreaterThan(0);
        await (await s.Miembro.PostAsync("/api/v1/notifications/read-all", null)).ShouldBeAsync(204);
        count = await (await s.Miembro.GetAsync("/api/v1/notifications/unread-count")).JsonAsync();
        count.GetProperty("count").GetInt32().Should().Be(0);

        var list = await (await s.Miembro.GetAsync($"/api/v1/tasks?projectId={s.ProjectId}")).JsonAsync();
        list.GetProperty("items")[0].GetProperty("commentCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Permisos_de_comentarios()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var taskId = await NewTask(s.Miembro, FirstList(s), "Tarea");
        await (await s.Observador.PostAsJsonAsync($"/api/v1/tasks/{taskId}/comments", new { body = "hola" })).ShouldBeAsync(403);
        await (await s.Observador.GetAsync($"/api/v1/tasks/{taskId}/comments")).ShouldBeAsync(200);
        await (await s.Ajeno.GetAsync($"/api/v1/tasks/{taskId}/comments")).ShouldBeAsync(404);

        var c = await (await s.Miembro.PostAsJsonAsync($"/api/v1/tasks/{taskId}/comments", new { body = "original" })).JsonAsync();
        var url = $"/api/v1/comments/{c.GetProperty("id")}";
        await (await s.OtroGestor.PutAsJsonAsync(url, new { body = "editado por otro" })).ShouldBeAsync(403);
        var edited = await (await s.Miembro.PutAsJsonAsync(url, new { body = "editado" })).JsonAsync();
        edited.GetProperty("editedAt").ValueKind.Should().Be(JsonValueKind.String);
        await (await s.Miembro.PostAsJsonAsync($"/api/v1/tasks/{taskId}/comments", new { body = "  " })).ShouldBeAsync(400, "VALIDACION");

        // El gestor del proyecto puede moderar.
        await (await s.Gestor.DeleteAsync(url)).ShouldBeAsync(204);
        var after = await (await s.Miembro.GetAsync($"/api/v1/tasks/{taskId}/comments")).JsonAsync();
        after.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Asignar_y_cambiar_estado_notifica_al_responsable()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var taskId = await NewTask(s.Gestor, FirstList(s), "Preparar demo", s.MiembroId);
        (await Notifications(s.Miembro)).Should().Contain(n => n.GetProperty("type").GetString() == "TaskAssigned");

        await (await s.Gestor.PatchAsJsonAsync($"/api/v1/tasks/{taskId}/status", new { status = "EnRevision" })).ShouldBeAsync(200);
        (await Notifications(s.Miembro)).Should().Contain(n => n.GetProperty("type").GetString() == "TaskStatusChanged" &&
                                                              n.GetProperty("title").GetString()!.Contains("En revisión"));

        // El propio responsable no recibe aviso de sus acciones.
        var before = (await Notifications(s.Miembro)).Count;
        await (await s.Miembro.PatchAsJsonAsync($"/api/v1/tasks/{taskId}/status", new { status = "Completada" })).ShouldBeAsync(200);
        (await Notifications(s.Miembro)).Count.Should().Be(before);
    }

    [Fact]
    public async Task Notificaciones_son_privadas()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        await NewTask(s.Gestor, FirstList(s), "Privada", s.MiembroId);
        var notification = (await Notifications(s.Miembro)).First();
        await (await s.Ajeno.PostAsync($"/api/v1/notifications/{notification.GetProperty("id")}/read", null)).ShouldBeAsync(404);
        await (await s.Miembro.PostAsync($"/api/v1/notifications/{notification.GetProperty("id")}/read", null)).ShouldBeAsync(204);
    }

    [Fact]
    public async Task Historial_registra_el_ciclo_de_vida()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var taskId = await NewTask(s.Miembro, FirstList(s), "Con historial");
        var task = await (await s.Miembro.GetAsync($"/api/v1/tasks/{taskId}")).JsonAsync();
        await (await s.Miembro.PutAsJsonAsync($"/api/v1/tasks/{taskId}", new
        {
            title = "Con historial v2", priority = "Alta", dueDate = "2026-11-30", rowVersion = task.GetProperty("rowVersion").GetString()
        })).ShouldBeAsync(200);
        await (await s.Miembro.PatchAsJsonAsync($"/api/v1/tasks/{taskId}/status", new { status = "Completada" })).ShouldBeAsync(200);

        var taskActivity = (await (await s.Gestor.GetAsync($"/api/v1/tasks/{taskId}/activity")).JsonAsync()).EnumerateArray().ToList();
        taskActivity.Select(a => a.GetProperty("type").GetString()).Should()
            .Contain(["TaskCreated", "TaskUpdated", "TaskStatusChanged"]);
        taskActivity.Single(a => a.GetProperty("type").GetString() == "TaskUpdated").GetProperty("message").GetString()
            .Should().Contain("el título").And.Contain("la prioridad").And.Contain("el vencimiento");
        await (await s.Miembro.DeleteAsync($"/api/v1/tasks/{taskId}")).ShouldBeAsync(204);

        await s.PrepareClosureAsync();
        await (await s.Gestor.PostAsJsonAsync(s.Url("/close"), new { force = false })).ShouldBeAsync(200);

        var project = await (await s.Observador.GetAsync(s.Url("/activity"))).JsonAsync();
        var types = project.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("type").GetString()).ToList();
        types.Should().Contain(["ProjectCreated", "ProjectStatusChanged", "TaskDeleted", "ProjectClosed", "MemberAdded"]);
        project.GetProperty("items")[0].GetProperty("actorName").GetString().Should().Be("Gestora Principal");

        (await Notifications(s.Observador)).Should().Contain(n => n.GetProperty("type").GetString() == "ProjectClosed");
    }

    [Fact]
    public async Task Job_de_vencimientos_avisa_una_sola_vez()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1).ToString("yyyy-MM-dd");
        var overdue = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2).ToString("yyyy-MM-dd");
        await NewTask(s.Gestor, FirstList(s), "Vence mañana", s.MiembroId, tomorrow);
        await NewTask(s.Gestor, FirstList(s), "Ya vencida", s.MiembroId, overdue);

        using (var scope = factory.Services.CreateScope())
            await DueDateNotifier.RunOnceAsync(scope.ServiceProvider, default);
        using (var scope = factory.Services.CreateScope())
            await DueDateNotifier.RunOnceAsync(scope.ServiceProvider, default);

        var mine = await Notifications(s.Miembro);
        mine.Count(n => n.GetProperty("type").GetString() == "TaskDueSoon" && n.GetProperty("title").GetString()!.Contains("Vence mañana"))
            .Should().Be(1);
        mine.Count(n => n.GetProperty("type").GetString() == "TaskOverdue" && n.GetProperty("title").GetString()!.Contains("Ya vencida"))
            .Should().Be(1);
    }

    [Fact]
    public async Task SignalR_entrega_notificaciones_y_cambios_del_proyecto_en_tiempo_real()
    {
        var s = await ProjectScenario.CreateAsync(factory, withTemplate: false);
        var token = s.Miembro.DefaultRequestHeaders.Authorization!.Parameter!;
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/notifications"), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .Build();

        var notification = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var changed = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>("notification", n => notification.TrySetResult(n));
        connection.On<JsonElement>("projectChanged", c => changed.TrySetResult(c));
        await connection.StartAsync();
        await connection.InvokeAsync("JoinProject", s.ProjectId);

        await NewTask(s.Gestor, FirstList(s), "En vivo", s.MiembroId);

        var n = await notification.Task.WaitAsync(TimeSpan.FromSeconds(10));
        n.GetProperty("type").GetString().Should().Be("TaskAssigned");
        var c = await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        c.GetProperty("projectId").GetGuid().Should().Be(s.ProjectId);

        // Un usuario ajeno no puede suscribirse al proyecto.
        var ajenoToken = s.Ajeno.DefaultRequestHeaders.Authorization!.Parameter!;
        await using var intruder = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/notifications"), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(ajenoToken);
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .Build();
        await intruder.StartAsync();
        var join = () => intruder.InvokeAsync("JoinProject", s.ProjectId);
        await join.Should().ThrowAsync<HubException>();
    }
}
