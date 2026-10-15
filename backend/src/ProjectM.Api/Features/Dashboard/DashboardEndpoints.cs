using Microsoft.EntityFrameworkCore;
using ProjectM.Api.Features.Tasks;
using ProjectM.Api.Infrastructure;
using ProjectM.Api.Infrastructure.Auth;
using ProjectM.Api.Infrastructure.Authorization;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Domain.Projects;
using ProjectM.Domain.Tasks;

namespace ProjectM.Api.Features.Dashboard;

public record WorkloadDto(Guid? UserId, string Name, int Pendiente, int EnProgreso, int EnRevision, int Bloqueada, int Overdue, int Total);
public record OverdueTaskDto(Guid Id, Guid ProjectId, string ProjectName, string Title, DateOnly DueDate, string? AssigneeName, int DaysLate);
public record UpcomingMilestoneDto(Guid Id, Guid ProjectId, string ProjectName, string Name, DateOnly DueDate);

public record DashboardDto(
    int ActiveProjects,
    int OpenTasks,
    int OverdueTasks,
    int CompletedLast7Days,
    int MyOpenTasks,
    Dictionary<TaskItemStatus, int> TasksByStatus,
    Dictionary<ProjectStatus, int> ProjectsByStatus,
    List<WorkloadDto> Workload,
    List<OverdueTaskDto> Overdue,
    List<UpcomingMilestoneDto> UpcomingMilestones);

public static class DashboardEndpoints
{
    public static RouteGroupBuilder MapDashboardEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", Get).WithTags("Dashboard");
        return api;
    }

    /// <summary>Métricas agregadas sobre los proyectos visibles para el usuario (opcionalmente uno solo).</summary>
    private static async Task<IResult> Get(Guid? projectId, ProjectGuard guard, AppDbContext db, ICurrentUser user,
        AppClock clock, CancellationToken ct)
    {
        var today = clock.Today;
        var projects = guard.VisibleProjects();
        if (projectId is not null)
        {
            await guard.EnsureAsync(projectId.Value, ProjectPermission.View, ct);
            projects = projects.Where(p => p.Id == projectId);
        }
        var projectIds = projects.Select(p => p.Id);
        // Tareas (y subtareas) de proyectos no cerrados: los cerrados no generan carga ni vencimientos.
        var tasks = db.Tasks.AsNoTracking().Where(t => projectIds.Contains(t.ProjectId));
        var live = tasks.Where(t => t.Project.Status != ProjectStatus.Cerrado);
        var open = live.Where(t => t.Status != TaskItemStatus.Completada);

        var byStatus = await live.GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var projectStatus = await projects.AsNoTracking().GroupBy(p => p.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);

        var workloadRows = await open
            .GroupBy(t => new { t.AssigneeId, Name = t.Assignee != null ? t.Assignee.FullName : null })
            .Select(g => new
            {
                g.Key.AssigneeId,
                g.Key.Name,
                Pendiente = g.Count(t => t.Status == TaskItemStatus.Pendiente),
                EnProgreso = g.Count(t => t.Status == TaskItemStatus.EnProgreso),
                EnRevision = g.Count(t => t.Status == TaskItemStatus.EnRevision),
                Bloqueada = g.Count(t => t.Status == TaskItemStatus.Bloqueada),
                Overdue = g.Count(t => t.DueDate < today),
                Total = g.Count()
            }).ToListAsync(ct);

        var overdue = await open.Where(t => t.DueDate < today).OrderBy(t => t.DueDate).Take(10)
            .Select(t => new { t.Id, t.ProjectId, ProjectName = t.Project.Name, t.Title, DueDate = t.DueDate!.Value,
                AssigneeName = t.Assignee != null ? t.Assignee.FullName : null })
            .ToListAsync(ct);

        var horizon = today.AddDays(14);
        var milestones = await db.Milestones.AsNoTracking()
            .Where(m => projectIds.Contains(m.ProjectId) && m.CompletedAt == null && m.DueDate >= today && m.DueDate <= horizon)
            .OrderBy(m => m.DueDate).Take(10)
            .Join(db.Projects, m => m.ProjectId, p => p.Id, (m, p) => new UpcomingMilestoneDto(m.Id, p.Id, p.Name, m.Name, m.DueDate))
            .ToListAsync(ct);

        var weekAgo = clock.Now.AddDays(-7);
        var uid = user.Id;
        var dto = new DashboardDto(
            ActiveProjects: projectStatus.Where(p => p.Key is ProjectStatus.Activo).Sum(p => p.Count),
            OpenTasks: await open.CountAsync(ct),
            OverdueTasks: await open.CountAsync(t => t.DueDate < today, ct),
            CompletedLast7Days: await tasks.CountAsync(t => t.CompletedAt >= weekAgo, ct),
            MyOpenTasks: await open.CountAsync(t => t.AssigneeId == uid, ct),
            TasksByStatus: Enum.GetValues<TaskItemStatus>().ToDictionary(s => s, s => byStatus.FirstOrDefault(x => x.Key == s)?.Count ?? 0),
            ProjectsByStatus: Enum.GetValues<ProjectStatus>().ToDictionary(s => s, s => projectStatus.FirstOrDefault(x => x.Key == s)?.Count ?? 0),
            Workload: workloadRows
                .Select(w => new WorkloadDto(w.AssigneeId, w.Name ?? "Sin asignar", w.Pendiente, w.EnProgreso, w.EnRevision, w.Bloqueada, w.Overdue, w.Total))
                .OrderBy(w => w.UserId is null).ThenByDescending(w => w.Total).ToList(),
            Overdue: overdue.Select(t => new OverdueTaskDto(t.Id, t.ProjectId, t.ProjectName, t.Title, t.DueDate, t.AssigneeName,
                today.DayNumber - t.DueDate.DayNumber)).ToList(),
            UpcomingMilestones: milestones);
        return Results.Ok(dto);
    }
}
