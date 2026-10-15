using System.Linq.Expressions;
using ProjectM.Domain.Tasks;

namespace ProjectM.Api.Features.Tasks;

/// <summary>Vista compacta de una tarea (listados de bloqueos, hitos, calendario).</summary>
public record TaskSummaryDto(
    Guid Id,
    Guid ProjectId,
    Guid ListId,
    Guid? ParentTaskId,
    string Title,
    TaskItemStatus Status,
    TaskPriority Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    Guid? AssigneeId,
    string? AssigneeName,
    string? BlockedReason)
{
    public static readonly Expression<Func<TaskItem, TaskSummaryDto>> Projection = t => new TaskSummaryDto(
        t.Id, t.ProjectId, t.ListId, t.ParentTaskId, t.Title, t.Status, t.Priority, t.StartDate, t.DueDate,
        t.AssigneeId, t.Assignee != null ? t.Assignee.FullName : null, t.BlockedReason);
}
