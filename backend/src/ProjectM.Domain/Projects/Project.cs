using ProjectM.Domain.Common;
using ProjectM.Domain.Tasks;
using ProjectM.Domain.Users;

namespace ProjectM.Domain.Projects;

public class Project : Entity
{
    /// <summary>Número secuencial (código visible PRJ-0001).</summary>
    public int Number { get; set; }
    /// <summary>Columna calculada en BD a partir de <see cref="Number"/>.</summary>
    public string Code { get; private set; } = "";

    public Guid SpaceId { get; set; }
    public Space Space { get; set; } = null!;
    public string Name { get; set; } = "";
    public Guid? ClientId { get; set; }
    public Client? Client { get; set; }
    public string? Objective { get; set; }
    public string? Scope { get; set; }
    public DateOnly PlannedStart { get; set; }
    public DateOnly PlannedEnd { get; set; }
    public DateOnly? ActualStart { get; set; }
    public DateOnly? ActualEnd { get; set; }
    public decimal? Budget { get; set; }
    public string Currency { get; set; } = "USD";
    public Guid ManagerId { get; set; }
    public AppUser Manager { get; set; } = null!;
    public ProjectStatus Status { get; private set; } = ProjectStatus.Planeacion;
    public ProjectHealth Health { get; set; } = ProjectHealth.EnTiempo;
    public Guid? TemplateId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedById { get; set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public Guid? ClosedById { get; private set; }
    public byte[] RowVersion { get; set; } = [];

    public List<ProjectMember> Members { get; set; } = [];
    public List<TaskList> Lists { get; set; } = [];
    public List<Milestone> Milestones { get; set; } = [];
    public List<Risk> Risks { get; set; } = [];
    public List<ProjectClosureChecklistItem> ClosureChecklist { get; set; } = [];
    public ProjectClosure? Closure { get; set; }

    public bool IsClosed => Status == ProjectStatus.Cerrado;

    // ---------------- Ciclo de vida ----------------

    public void Activate(DateOnly today)
    {
        Transition(ProjectStatus.Activo, ProjectStatus.Planeacion);
        ActualStart ??= today;
    }

    public void Pause() => Transition(ProjectStatus.EnPausa, ProjectStatus.Activo);

    public void Resume() => Transition(ProjectStatus.Activo, ProjectStatus.EnPausa);

    /// <summary>Inicia el cierre: copia el checklist configurable (si aún no existe) y crea el borrador del acta.</summary>
    public void StartClosure(IEnumerable<ClosureChecklistTemplateItem> template)
    {
        Transition(ProjectStatus.EnCierre, ProjectStatus.Activo, ProjectStatus.EnPausa);
        if (ClosureChecklist.Count == 0)
        {
            ClosureChecklist.AddRange(template.OrderBy(t => t.Position).Select(t => new ProjectClosureChecklistItem
            {
                ProjectId = Id,
                Text = t.Text,
                IsRequired = t.IsRequired,
                Position = t.Position
            }));
        }
        Closure ??= new ProjectClosure { ProjectId = Id };
    }

    public void CancelClosure() => Transition(ProjectStatus.Activo, ProjectStatus.EnCierre);

    /// <summary>
    /// Cierra el proyecto. Reglas: checklist obligatorio completo, acta con resumen y
    /// sin tareas abiertas, salvo cierre forzado con justificación (≥ 20 caracteres).
    /// </summary>
    public void Close(ProjectMetrics metrics, bool force, string? justification, Guid closedBy,
        DateTimeOffset now, DateOnly today)
    {
        if (Status != ProjectStatus.EnCierre)
            throw new DomainException("TRANSICION_INVALIDA",
                "Para cerrar el proyecto primero debes iniciar el proceso de cierre.");

        var problems = ClosureRules.Validate(this, metrics, force, justification);
        if (problems.Count > 0) throw problems[0];

        var closure = Closure!;
        closure.PlannedStart = PlannedStart;
        closure.PlannedEnd = PlannedEnd;
        closure.ActualStart = ActualStart;
        closure.ActualEnd = today;
        closure.TotalTasks = metrics.TotalTasks;
        closure.CompletedTasks = metrics.CompletedTasks;
        closure.OpenTasksAtClose = metrics.OpenTasks;
        closure.IsForced = force && metrics.OpenTasks > 0;
        closure.ForceJustification = closure.IsForced ? justification!.Trim() : null;
        closure.ClosedById = closedBy;
        closure.ClosedAt = now;

        ActualEnd = today;
        ClosedAt = now;
        ClosedById = closedBy;
        var from = Status;
        Status = ProjectStatus.Cerrado;
        Raise(new ProjectStatusChanged(Id, from, Status));
        Raise(new ProjectClosed(Id, closure.IsForced));
    }

    /// <summary>Reabre un proyecto cerrado (solo Admin, validado en la capa de autorización).</summary>
    public void Reopen(string? reason)
    {
        if (Status != ProjectStatus.Cerrado)
            throw new DomainException("TRANSICION_INVALIDA", "Solo se puede reabrir un proyecto cerrado.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
            throw new DomainException("MOTIVO_REQUERIDO", "Indica el motivo de la reapertura (mínimo 10 caracteres).",
                DomainErrorKind.Validation);
        Status = ProjectStatus.Activo;
        ActualEnd = null;
        ClosedAt = null;
        ClosedById = null;
        Raise(new ProjectStatusChanged(Id, ProjectStatus.Cerrado, Status));
        Raise(new ProjectReopened(Id, reason.Trim()));
    }

    /// <summary>Un proyecto cerrado es de solo lectura.</summary>
    public void EnsureEditable()
    {
        if (IsClosed) throw ProjectClosedException();
    }

    public static DomainException ProjectClosedException() =>
        new("PROYECTO_CERRADO", "El proyecto está cerrado y es de solo lectura. Un Admin puede reabrirlo.");

    private void Transition(ProjectStatus to, params ProjectStatus[] allowedFrom)
    {
        if (Status == ProjectStatus.Cerrado) throw ProjectClosedException();
        if (!allowedFrom.Contains(Status))
            throw new DomainException("TRANSICION_INVALIDA",
                $"No se puede pasar de {Status.Label()} a {to.Label()}.");
        var from = Status;
        Status = to;
        Raise(new ProjectStatusChanged(Id, from, to));
    }

    /// <summary>Solo para seed/pruebas: fija el estado sin pasar por las reglas.</summary>
    public void ForceStatusForSeed(ProjectStatus status, DateTimeOffset? closedAt = null, Guid? closedBy = null)
    {
        Status = status;
        ClosedAt = closedAt;
        ClosedById = closedBy;
    }
}

public class ProjectMember
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public DateTimeOffset AddedAt { get; set; }
}

/// <summary>Métricas de tareas usadas para avance y validación de cierre.</summary>
public record ProjectMetrics(int TotalTasks, int CompletedTasks)
{
    public int OpenTasks => TotalTasks - CompletedTasks;
    public int ProgressPercent => TotalTasks == 0 ? 0 : (int)Math.Round(CompletedTasks * 100.0 / TotalTasks);
}

public static class ProjectStatusExtensions
{
    public static string Label(this ProjectStatus s) => s switch
    {
        ProjectStatus.Planeacion => "Planeación",
        ProjectStatus.Activo => "Activo",
        ProjectStatus.EnPausa => "En pausa",
        ProjectStatus.EnCierre => "En cierre",
        ProjectStatus.Cerrado => "Cerrado",
        _ => s.ToString()
    };
}

public record ProjectStatusChanged(Guid ProjectId, ProjectStatus From, ProjectStatus To) : IDomainEvent;
public record ProjectClosed(Guid ProjectId, bool Forced) : IDomainEvent;
public record ProjectReopened(Guid ProjectId, string Reason) : IDomainEvent;
