using ProjectM.Domain.Common;
using ProjectM.Domain.Tasks;
using ProjectM.Domain.Users;

namespace ProjectM.Domain.Projects;

public class Milestone : Entity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateOnly DueDate { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<TaskItem> Tasks { get; set; } = [];
}

public class Risk : Entity
{
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>1 = baja, 2 = media, 3 = alta.</summary>
    public int Probability { get; set; } = 2;
    public int Impact { get; set; } = 2;
    public int Severity => Probability * Impact;
    public RiskStatus Status { get; set; } = RiskStatus.Abierto;
    public Guid? OwnerId { get; set; }
    public AppUser? Owner { get; set; }
    public string? Mitigation { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Plantilla de tareas iniciales para la apertura de proyectos.</summary>
public class ProjectTemplate : Entity
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<ProjectTemplateTask> Tasks { get; set; } = [];
}

public class ProjectTemplateTask : Entity
{
    public Guid TemplateId { get; set; }
    public string ListName { get; set; } = "General";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    /// <summary>Días desde el inicio planeado del proyecto.</summary>
    public int StartOffsetDays { get; set; }
    public int DurationDays { get; set; } = 1;
    public int Position { get; set; }
}
