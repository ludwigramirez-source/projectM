using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ProjectM.Api.Infrastructure;
using ProjectM.Api.Infrastructure.Auth;
using ProjectM.Api.Infrastructure.Authorization;
using ProjectM.Api.Infrastructure.Errors;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Api.Infrastructure.Validation;

namespace ProjectM.Api.Features.Tasks;

public record CalendarMilestoneDto(Guid Id, Guid ProjectId, string ProjectName, string Name, DateOnly DueDate, DateTimeOffset? CompletedAt);
public record CalendarDto(List<TaskListItemDto> Tasks, List<CalendarMilestoneDto> Milestones);
public record SetDatesRequest(DateOnly? StartDate, DateOnly? DueDate);

public class SetDatesRequestValidator : AbstractValidator<SetDatesRequest>
{
    public SetDatesRequestValidator() =>
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.StartDate)
            .When(x => x.StartDate is not null && x.DueDate is not null)
            .WithMessage("La fecha de vencimiento no puede ser anterior a la de inicio.");
}

public static class CalendarEndpoints
{
    public const int MaxRangeDays = 62;

    public static RouteGroupBuilder MapCalendarEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/calendar", Calendar).WithTags("Calendario");
        api.MapPatch("/tasks/{id:guid}/dates", SetDates).WithTags("Tareas").Validate<SetDatesRequest>();
        return api;
    }

    /// <summary>Tareas cuyo rango [inicio, vencimiento] se cruza con [from, to], más los hitos del rango.</summary>
    private static async Task<IResult> Calendar(DateOnly from, DateOnly to, Guid? projectId, bool? mine, Guid? assigneeId,
        ProjectGuard guard, AppDbContext db, ICurrentUser user, AppClock clock, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > MaxRangeDays)
            throw AppException.Validation("to", $"El rango debe ser válido y de máximo {MaxRangeDays} días.");

        IQueryable<Guid> projectIds;
        if (projectId is not null)
        {
            await guard.EnsureAsync(projectId.Value, ProjectPermission.View, ct);
            projectIds = db.Projects.Where(p => p.Id == projectId).Select(p => p.Id);
        }
        else projectIds = guard.VisibleProjects().Select(p => p.Id);

        if (mine == true) assigneeId = user.Id;
        var tasks = db.Tasks.AsNoTracking()
            .Where(t => projectIds.Contains(t.ProjectId) && (t.DueDate != null || t.StartDate != null))
            .Where(t => (t.StartDate ?? t.DueDate) <= to && (t.DueDate ?? t.StartDate) >= from);
        if (assigneeId is not null) tasks = tasks.Where(t => t.AssigneeId == assigneeId);

        var items = await tasks.OrderBy(t => t.DueDate).ThenBy(t => t.Position).Take(1000)
            .Select(TaskEndpoints.ListProjection(clock.Today)).ToListAsync(ct);

        var milestones = await db.Milestones.AsNoTracking()
            .Where(m => projectIds.Contains(m.ProjectId) && m.DueDate >= from && m.DueDate <= to)
            .Join(db.Projects, m => m.ProjectId, p => p.Id, (m, p) => new CalendarMilestoneDto(m.Id, p.Id, p.Name, m.Name, m.DueDate, m.CompletedAt))
            .ToListAsync(ct);

        return Results.Ok(new CalendarDto(items, milestones));
    }

    /// <summary>Reprogramación rápida (arrastrar en el calendario).</summary>
    private static async Task<IResult> SetDates(Guid id, SetDatesRequest req, ProjectGuard guard, AppDbContext db,
        AppClock clock, CancellationToken ct)
    {
        await guard.EnsureForTaskAsync(id, ProjectPermission.EditTasks, ct);
        var task = await db.Tasks.FirstAsync(t => t.Id == id, ct);
        task.SetDates(req.StartDate, req.DueDate);
        task.UpdatedAt = clock.Now;
        await db.SaveChangesAsync(ct);
        return Results.Ok(await db.Tasks.AsNoTracking().Where(t => t.Id == id)
            .Select(TaskEndpoints.ListProjection(clock.Today)).FirstAsync(ct));
    }
}
