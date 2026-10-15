using ProjectM.Domain.Common;
using ProjectM.Domain.Tasks;
using ProjectM.Domain.Users;

namespace ProjectM.Domain.Collaboration;

public class Comment : Entity
{
    public Guid TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;
    public Guid AuthorId { get; set; }
    public AppUser Author { get; set; } = null!;
    public string Body { get; set; } = "";
    /// <summary>Usuarios mencionados con @ (deben ser miembros del proyecto).</summary>
    public List<Guid> Mentions { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>Historial inmutable de lo ocurrido en un proyecto y sus tareas.</summary>
public class ActivityLog : Entity
{
    public Guid ProjectId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? ActorId { get; set; }
    public AppUser? Actor { get; set; }
    public string Type { get; set; } = "";
    /// <summary>Descripción legible en español (sin el nombre del actor).</summary>
    public string Message { get; set; } = "";
    /// <summary>Detalle estructurado en JSON (estados, campos, motivos).</summary>
    public string? Data { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Notificación dentro de la app (fuente de verdad para cualquier canal: in-app, correo, WhatsApp).</summary>
public class Notification : Entity
{
    public Guid UserId { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    /// <summary>Ruta del SPA a abrir (p. ej. /projects/{id}/tareas?task={id}).</summary>
    public string? Link { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? ActorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

public static class ActivityTypes
{
    public const string ProjectCreated = "ProjectCreated";
    public const string TaskCreated = "TaskCreated";
    public const string TaskUpdated = "TaskUpdated";
    public const string TaskStatusChanged = "TaskStatusChanged";
    public const string TaskAssigned = "TaskAssigned";
    public const string TaskDeleted = "TaskDeleted";
    public const string CommentAdded = "CommentAdded";
    public const string AttachmentAdded = "AttachmentAdded";
    public const string ProjectStatusChanged = "ProjectStatusChanged";
    public const string ProjectClosed = "ProjectClosed";
    public const string ProjectReopened = "ProjectReopened";
    public const string MemberAdded = "MemberAdded";
}

public static class NotificationTypes
{
    public const string TaskAssigned = "TaskAssigned";
    public const string TaskStatusChanged = "TaskStatusChanged";
    public const string Mention = "Mention";
    public const string Comment = "Comment";
    public const string TaskDueSoon = "TaskDueSoon";
    public const string TaskOverdue = "TaskOverdue";
    public const string ProjectClosed = "ProjectClosed";
    public const string ProjectReopened = "ProjectReopened";
    public const string AddedToProject = "AddedToProject";
}

// Eventos adicionales (los de proyecto y estado/asignación de tarea viven junto a sus entidades).
public record TaskCreated(Guid TaskId, Guid ProjectId, string Title) : IDomainEvent;
public record TaskUpdated(Guid TaskId, Guid ProjectId, IReadOnlyList<string> Fields) : IDomainEvent;
public record TaskDeleted(Guid TaskId, Guid ProjectId, string Title) : IDomainEvent;
public record CommentAdded(Guid CommentId, Guid TaskId, Guid ProjectId, IReadOnlyList<Guid> Mentions, string Excerpt) : IDomainEvent;
public record AttachmentAdded(Guid TaskId, Guid ProjectId, string FileName) : IDomainEvent;
public record MemberAdded(Guid ProjectId, Guid UserId) : IDomainEvent;
public record ProjectCreated(Guid ProjectId, string Name) : IDomainEvent;

public static class TaskStatusLabels
{
    public static string Label(this TaskItemStatus s) => s switch
    {
        TaskItemStatus.Pendiente => "Pendiente",
        TaskItemStatus.EnProgreso => "En progreso",
        TaskItemStatus.EnRevision => "En revisión",
        TaskItemStatus.Completada => "Completada",
        TaskItemStatus.Bloqueada => "Bloqueada",
        _ => s.ToString()
    };
}
