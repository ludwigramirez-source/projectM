using ProjectM.Domain.Common;
using ProjectM.Domain.Projects;
using ProjectM.Domain.Users;

namespace ProjectM.Domain.Tasks;

/// <summary>Estado de una tarea. "Vencida" no es un estado: se deriva de la fecha de vencimiento.</summary>
public enum TaskItemStatus
{
    Pendiente,
    EnProgreso,
    EnRevision,
    Completada,
    Bloqueada
}

public enum TaskPriority
{
    Baja,
    Normal,
    Alta,
    Urgente
}

public class TaskList : Entity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string Name { get; set; } = "";
    public int Position { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<TaskItem> Tasks { get; set; } = [];
}

public class TaskItem : Entity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public Guid ListId { get; set; }
    public TaskList List { get; set; } = null!;
    public Guid? ParentTaskId { get; set; }
    public TaskItem? ParentTask { get; set; }
    public Guid? MilestoneId { get; set; }
    public Milestone? Milestone { get; set; }

    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public Guid? AssigneeId { get; set; }
    public AppUser? Assignee { get; set; }
    public TaskItemStatus Status { get; private set; } = TaskItemStatus.Pendiente;
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? BlockedReason { get; private set; }
    /// <summary>Orden fraccional dentro de su lista/columna (Kanban).</summary>
    public double Position { get; set; }

    public Guid CreatedById { get; set; }
    public AppUser CreatedBy { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public List<TaskItem> Subtasks { get; set; } = [];
    public List<TaskTag> Tags { get; set; } = [];
    public List<CustomFieldValue> CustomFieldValues { get; set; } = [];
    public List<Attachment> Attachments { get; set; } = [];
    public List<Collaboration.Comment> Comments { get; set; } = [];

    public bool IsOpen => Status != TaskItemStatus.Completada;
    public bool IsOverdue(DateOnly today) => IsOpen && DueDate is not null && DueDate < today;

    /// <summary>
    /// Cambia el estado aplicando las reglas: Completada fija CompletedAt (y salir de ella lo limpia);
    /// Bloqueada exige motivo.
    /// </summary>
    public void ChangeStatus(TaskItemStatus status, string? blockedReason, DateTimeOffset now)
    {
        if (status == TaskItemStatus.Bloqueada && string.IsNullOrWhiteSpace(blockedReason))
            throw new DomainException("MOTIVO_BLOQUEO_REQUERIDO", "Indica el motivo del bloqueo.", DomainErrorKind.Validation);

        var from = Status;
        Status = status;
        BlockedReason = status == TaskItemStatus.Bloqueada ? Guard.Required(blockedReason, "El motivo del bloqueo", 500) : null;
        CompletedAt = status == TaskItemStatus.Completada ? (from == TaskItemStatus.Completada ? CompletedAt : now) : null;
        UpdatedAt = now;
        if (from != status) Raise(new TaskStatusChanged(Id, ProjectId, from, status));
    }

    public void SetDates(DateOnly? start, DateOnly? due)
    {
        Guard.DateRange(start, due, "La fecha de vencimiento");
        StartDate = start;
        DueDate = due;
    }

    public void Assign(Guid? assigneeId)
    {
        if (AssigneeId == assigneeId) return;
        AssigneeId = assigneeId;
        if (assigneeId is not null) Raise(new TaskAssigned(Id, ProjectId, assigneeId.Value));
    }
}

public record TaskStatusChanged(Guid TaskId, Guid ProjectId, TaskItemStatus From, TaskItemStatus To) : IDomainEvent;
public record TaskAssigned(Guid TaskId, Guid ProjectId, Guid AssigneeId) : IDomainEvent;
