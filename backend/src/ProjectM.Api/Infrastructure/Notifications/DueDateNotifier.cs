using Microsoft.EntityFrameworkCore;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Domain.Collaboration;
using ProjectM.Domain.Projects;
using ProjectM.Domain.Tasks;

namespace ProjectM.Api.Infrastructure.Notifications;

/// <summary>
/// Revisa cada hora las tareas abiertas: avisa al responsable un día antes del vencimiento y
/// una vez cuando la tarea queda vencida. Es idempotente (no duplica avisos).
/// </summary>
public class DueDateNotifier(IServiceScopeFactory scopes, ILogger<DueDateNotifier> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await RunOnceAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error revisando vencimientos");
            }
        } while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    public static async Task<int> RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<AppClock>();
        var sender = services.GetRequiredService<NotificationSender>();
        var today = clock.Today;
        var tomorrow = today.AddDays(1);
        var weekAgo = today.AddDays(-7);
        var now = clock.Now;

        var candidates = await db.Tasks.AsNoTracking()
            .Where(t => t.AssigneeId != null && t.Status != TaskItemStatus.Completada && t.DueDate != null &&
                        t.Project.Status != ProjectStatus.Cerrado &&
                        (t.DueDate == tomorrow || (t.DueDate < today && t.DueDate >= weekAgo)))
            .Select(t => new { t.Id, t.ProjectId, t.Title, t.DueDate, AssigneeId = t.AssigneeId!.Value })
            .ToListAsync(ct);

        var created = new List<Notification>();
        foreach (var t in candidates)
        {
            var dueSoon = t.DueDate == tomorrow;
            var type = dueSoon ? NotificationTypes.TaskDueSoon : NotificationTypes.TaskOverdue;
            // Un aviso "por vencer" por día y un único aviso de "vencida" por tarea.
            var since = dueSoon ? now.AddHours(-20) : DateTimeOffset.MinValue;
            if (await db.Notifications.AnyAsync(n => n.TaskId == t.Id && n.UserId == t.AssigneeId && n.Type == type && n.CreatedAt >= since, ct))
                continue;
            var n = new Notification
            {
                UserId = t.AssigneeId,
                Type = type,
                Title = dueSoon ? $"«{t.Title}» vence mañana" : $"«{t.Title}» está vencida",
                Body = $"Vencimiento: {t.DueDate:dd/MM/yyyy}",
                Link = $"/projects/{t.ProjectId}/tareas?task={t.Id}",
                ProjectId = t.ProjectId,
                TaskId = t.Id,
                CreatedAt = now
            };
            db.Notifications.Add(n);
            created.Add(n);
        }

        if (created.Count == 0) return 0;
        await db.SaveChangesAsync(ct);
        await sender.SendAsync(created.Select(n =>
            new NotificationMessage(n.Id, n.UserId, n.Type, n.Title, n.Body, n.Link, n.ProjectId, n.TaskId, n.CreatedAt)), ct);
        return created.Count;
    }
}
