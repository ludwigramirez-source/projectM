using ProjectM.Domain.Common;
using ProjectM.Domain.Users;

namespace ProjectM.Domain.Projects;

/// <summary>Ítem del checklist de cierre global (configurable por Admin).</summary>
public class ClosureChecklistTemplateItem : Entity
{
    public string Text { get; set; } = "";
    public bool IsRequired { get; set; } = true;
    public int Position { get; set; }
}

/// <summary>Copia del checklist en el proyecto al iniciar el cierre.</summary>
public class ProjectClosureChecklistItem : Entity
{
    public Guid ProjectId { get; set; }
    public string Text { get; set; } = "";
    public bool IsRequired { get; set; }
    public int Position { get; set; }
    public bool IsDone { get; set; }
    public Guid? DoneById { get; set; }
    public AppUser? DoneBy { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Acta de cierre: resumen, entregables, fechas reales vs planeadas y lecciones aprendidas.</summary>
public class ProjectClosure
{
    public Guid ProjectId { get; set; }
    public string? Summary { get; set; }
    public List<string> Deliverables { get; set; } = [];
    public DateOnly? PlannedStart { get; set; }
    public DateOnly? PlannedEnd { get; set; }
    public DateOnly? ActualStart { get; set; }
    public DateOnly? ActualEnd { get; set; }
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int OpenTasksAtClose { get; set; }
    public bool IsForced { get; set; }
    public string? ForceJustification { get; set; }
    public Guid? ClosedById { get; set; }
    public AppUser? ClosedBy { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public List<LessonLearned> Lessons { get; set; } = [];
}

public class LessonLearned : Entity
{
    public Guid ProjectId { get; set; }
    public string Category { get; set; } = "General";
    public string? WhatWentWell { get; set; }
    public string? WhatToImprove { get; set; }
    public string? Recommendation { get; set; }
    public int Position { get; set; }
}

public static class ClosureRules
{
    public const int MinJustificationLength = 20;

    /// <summary>Devuelve todas las razones que impiden cerrar (vacío = se puede cerrar).</summary>
    public static List<DomainException> Validate(Project project, ProjectMetrics metrics, bool force, string? justification)
    {
        var problems = new List<DomainException>();
        var closure = project.Closure;

        var pending = project.ClosureChecklist.Where(i => i.IsRequired && !i.IsDone).ToList();
        if (pending.Count > 0)
            problems.Add(new DomainException("CHECKLIST_INCOMPLETO",
                $"Faltan {pending.Count} ítem(s) obligatorio(s) del checklist de cierre."));

        if (string.IsNullOrWhiteSpace(closure?.Summary))
            problems.Add(new DomainException("ACTA_INCOMPLETA", "El acta de cierre requiere un resumen.",
                DomainErrorKind.Validation));

        if (metrics.OpenTasks > 0)
        {
            if (!force)
                problems.Add(new DomainException("TAREAS_ABIERTAS",
                    $"Quedan {metrics.OpenTasks} tarea(s) abierta(s). Complétalas o usa el cierre forzado con justificación."));
            else if (string.IsNullOrWhiteSpace(justification) || justification.Trim().Length < MinJustificationLength)
                problems.Add(new DomainException("JUSTIFICACION_REQUERIDA",
                    $"El cierre forzado requiere una justificación de al menos {MinJustificationLength} caracteres.",
                    DomainErrorKind.Validation));
        }
        return problems;
    }
}
