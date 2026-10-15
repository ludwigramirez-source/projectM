using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProjectM.Api.Infrastructure.Auth;
using ProjectM.Api.Infrastructure.Notifications;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Api.Infrastructure.Realtime;
using ProjectM.Domain.Collaboration;
using ProjectM.Domain.Common;
using ProjectM.Domain.Projects;
using ProjectM.Domain.Tasks;

namespace ProjectM.Api.Infrastructure.Events;

/// <summary>Trabajo a ejecutar después de confirmar la transacción.</summary>
public record PostCommitWork(List<NotificationMessage> Notifications, List<(Guid ProjectId, Guid? TaskId, string Type)> ProjectChanges);

/// <summary>
/// Traduce eventos de dominio a historial (ActivityLog) y notificaciones, dentro de la misma
/// transacción que el cambio. Tras confirmar, entrega notificaciones por los canales y avisa
/// en tiempo real a quienes tienen abierto el proyecto.
/// </summary>
public class DomainEventDispatcher(
    ICurrentUser currentUser,
    AppClock clock,
    NotificationSender sender,
    IHubContext<NotificationsHub> hub,
    ILogger<DomainEventDispatcher> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PostCommitWork> BeforeSaveAsync(AppDbContext db, IReadOnlyList<IDomainEvent> events, CancellationToken ct)
    {
        var work = new PostCommitWork([], []);
        var actorId = currentUser.IsAuthenticated ? currentUser.Id : (Guid?)null;
        var actorName = currentUser.IsAuthenticated ? currentUser.Name : "Sistema";
        var now = clock.Now;

        void Log(Guid projectId, Guid? taskId, string type, string message, object? data = null)
        {
            db.Add(new ActivityLog
            {
                ProjectId = projectId, TaskId = taskId, ActorId = actorId, Type = type, Message = message,
                Data = data is null ? null : JsonSerializer.Serialize(data, Json), CreatedAt = now
            });
            work.ProjectChanges.Add((projectId, taskId, type));
        }

        void Notify(Guid userId, string type, string title, string? body, Guid? projectId, Guid? taskId)
        {
            if (actorId == userId) return; // nunca notificar al autor de su propia acción
            var link = taskId is not null ? $"/projects/{projectId}/tareas?task={taskId}" : projectId is not null ? $"/projects/{projectId}" : null;
            var n = new Notification
            {
                UserId = userId, Type = type, Title = title, Body = body, Link = link, ProjectId = projectId,
                TaskId = taskId, ActorId = actorId, CreatedAt = now
            };
            db.Add(n);
            work.Notifications.Add(new NotificationMessage(n.Id, userId, type, title, body, link, projectId, taskId, now));
        }

        foreach (var e in events)
        {
            switch (e)
            {
                case ProjectCreated p:
                    Log(p.ProjectId, null, ActivityTypes.ProjectCreated, $"abrió el proyecto «{p.Name}»");
                    break;

                case ProjectStatusChanged p when p.To is not ProjectStatus.Cerrado && p.From is not ProjectStatus.Cerrado:
                    Log(p.ProjectId, null, ActivityTypes.ProjectStatusChanged,
                        $"cambió el estado del proyecto de {p.From.Label()} a {p.To.Label()}", new { from = p.From, to = p.To });
                    break;

                case ProjectClosed p:
                {
                    var name = await ProjectNameAsync(db, p.ProjectId, ct);
                    Log(p.ProjectId, null, ActivityTypes.ProjectClosed,
                        p.Forced ? "cerró el proyecto de forma forzada (con tareas abiertas)" : "cerró el proyecto", new { forced = p.Forced });
                    foreach (var uid in await MemberIdsAsync(db, p.ProjectId, ct))
                        Notify(uid, NotificationTypes.ProjectClosed, $"El proyecto «{name}» fue cerrado", $"Por {actorName}", p.ProjectId, null);
                    break;
                }

                case ProjectReopened p:
                {
                    var name = await ProjectNameAsync(db, p.ProjectId, ct);
                    Log(p.ProjectId, null, ActivityTypes.ProjectReopened, $"reabrió el proyecto. Motivo: {p.Reason}", new { reason = p.Reason });
                    foreach (var uid in await MemberIdsAsync(db, p.ProjectId, ct))
                        Notify(uid, NotificationTypes.ProjectReopened, $"El proyecto «{name}» fue reabierto", p.Reason, p.ProjectId, null);
                    break;
                }

                case MemberAdded m:
                {
                    var name = await ProjectNameAsync(db, m.ProjectId, ct);
                    var who = await db.Users.Where(u => u.Id == m.UserId).Select(u => u.FullName).FirstOrDefaultAsync(ct);
                    Log(m.ProjectId, null, ActivityTypes.MemberAdded, $"agregó a {who} al equipo");
                    Notify(m.UserId, NotificationTypes.AddedToProject, $"Te agregaron al proyecto «{name}»", $"Por {actorName}", m.ProjectId, null);
                    break;
                }

                case TaskCreated t:
                    Log(t.ProjectId, t.TaskId, ActivityTypes.TaskCreated, $"creó la tarea «{t.Title}»");
                    break;

                case TaskDeleted t:
                    Log(t.ProjectId, t.TaskId, ActivityTypes.TaskDeleted, $"eliminó la tarea «{t.Title}»");
                    break;

                case TaskUpdated t:
                {
                    var title = await TaskTitleAsync(db, t.TaskId, ct);
                    Log(t.ProjectId, t.TaskId, ActivityTypes.TaskUpdated, $"actualizó {string.Join(", ", t.Fields)} de «{title}»", new { fields = t.Fields });
                    break;
                }

                case TaskStatusChanged t:
                {
                    var task = await TaskInfoAsync(db, t.TaskId, ct);
                    Log(t.ProjectId, t.TaskId, ActivityTypes.TaskStatusChanged,
                        $"movió «{task.Title}» de {t.From.Label()} a {t.To.Label()}", new { from = t.From, to = t.To });
                    if (task.AssigneeId is { } assignee)
                        Notify(assignee, NotificationTypes.TaskStatusChanged, $"«{task.Title}» pasó a {t.To.Label()}", $"Por {actorName}", t.ProjectId, t.TaskId);
                    break;
                }

                case TaskAssigned t:
                {
                    var task = await TaskInfoAsync(db, t.TaskId, ct);
                    var who = await db.Users.Where(u => u.Id == t.AssigneeId).Select(u => u.FullName).FirstOrDefaultAsync(ct);
                    Log(t.ProjectId, t.TaskId, ActivityTypes.TaskAssigned, $"asignó «{task.Title}» a {who}", new { assigneeId = t.AssigneeId });
                    Notify(t.AssigneeId, NotificationTypes.TaskAssigned, $"Te asignaron «{task.Title}»", $"Por {actorName}", t.ProjectId, t.TaskId);
                    break;
                }

                case AttachmentAdded a:
                {
                    var title = await TaskTitleAsync(db, a.TaskId, ct);
                    Log(a.ProjectId, a.TaskId, ActivityTypes.AttachmentAdded, $"adjuntó «{a.FileName}» en «{title}»");
                    break;
                }

                case CommentAdded c:
                {
                    var task = await TaskInfoAsync(db, c.TaskId, ct);
                    Log(c.ProjectId, c.TaskId, ActivityTypes.CommentAdded, $"comentó en «{task.Title}»", new { commentId = c.CommentId });
                    foreach (var uid in c.Mentions.Distinct())
                        Notify(uid, NotificationTypes.Mention, $"{actorName} te mencionó en «{task.Title}»", c.Excerpt, c.ProjectId, c.TaskId);
                    foreach (var uid in new[] { task.AssigneeId, task.CreatedById }.OfType<Guid>().Distinct().Except(c.Mentions))
                        Notify(uid, NotificationTypes.Comment, $"Nuevo comentario en «{task.Title}»", $"{actorName}: {c.Excerpt}", c.ProjectId, c.TaskId);
                    break;
                }
            }
        }
        return work;
    }

    public async Task AfterSaveAsync(PostCommitWork work, CancellationToken ct)
    {
        await sender.SendAsync(work.Notifications, ct);
        foreach (var change in work.ProjectChanges.DistinctBy(c => (c.ProjectId, c.TaskId)))
        {
            try
            {
                await hub.Clients.Group(NotificationsHub.ProjectGroup(change.ProjectId))
                    .SendAsync("projectChanged", new { change.ProjectId, change.TaskId, change.Type }, ct);
            }
            catch (Exception ex) { logger.LogWarning(ex, "No se pudo publicar el cambio del proyecto {Id}", change.ProjectId); }
        }
    }

    private record TaskInfo(string Title, Guid? AssigneeId, Guid CreatedById);

    /// <summary>Lee primero del change tracker (tareas recién creadas aún no están en BD).</summary>
    private static async Task<TaskInfo> TaskInfoAsync(AppDbContext db, Guid id, CancellationToken ct)
    {
        var local = db.Tasks.Local.FirstOrDefault(t => t.Id == id);
        if (local is not null) return new TaskInfo(local.Title, local.AssigneeId, local.CreatedById);
        return await db.Tasks.IgnoreQueryFilters().Where(t => t.Id == id)
            .Select(t => new TaskInfo(t.Title, t.AssigneeId, t.CreatedById)).FirstOrDefaultAsync(ct)
            ?? new TaskInfo("(tarea)", null, Guid.Empty);
    }

    private static async Task<string> TaskTitleAsync(AppDbContext db, Guid id, CancellationToken ct) =>
        (await TaskInfoAsync(db, id, ct)).Title;

    private static async Task<string> ProjectNameAsync(AppDbContext db, Guid id, CancellationToken ct) =>
        db.Projects.Local.FirstOrDefault(p => p.Id == id)?.Name
        ?? await db.Projects.Where(p => p.Id == id).Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "(proyecto)";

    private static Task<List<Guid>> MemberIdsAsync(AppDbContext db, Guid projectId, CancellationToken ct) =>
        db.ProjectMembers.Where(m => m.ProjectId == projectId).Select(m => m.UserId).ToListAsync(ct);
}
