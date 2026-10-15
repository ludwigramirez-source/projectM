using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ProjectM.Api.Infrastructure;
using ProjectM.Api.Infrastructure.Auth;
using ProjectM.Api.Infrastructure.Authorization;
using ProjectM.Api.Infrastructure.Errors;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Api.Infrastructure.Validation;
using ProjectM.Domain.Common;
using ProjectM.Domain.Projects;
using ProjectM.Domain.Tasks;

namespace ProjectM.Api.Features.Closure;

public record ChecklistItemDto(Guid Id, string Text, bool IsRequired, bool IsDone, string? DoneByName, DateTimeOffset? DoneAt, string? Notes);
public record LessonDto(Guid? Id, string Category, string? WhatWentWell, string? WhatToImprove, string? Recommendation);
public record ClosureMetricsDto(int TotalTasks, int CompletedTasks, int OpenTasks, int Progress);
public record ClosureProblemDto(string Code, string Message);

public record ClosureDto(
    ProjectStatus Status,
    List<ChecklistItemDto> Checklist,
    string? Summary,
    List<string> Deliverables,
    List<LessonDto> Lessons,
    ClosureMetricsDto Metrics,
    DateOnly PlannedStart,
    DateOnly PlannedEnd,
    DateOnly? ActualStart,
    DateOnly? ActualEnd,
    bool IsForced,
    string? ForceJustification,
    string? ClosedByName,
    DateTimeOffset? ClosedAt,
    /// <summary>Lo que falta para un cierre normal (sin forzar).</summary>
    List<ClosureProblemDto> Problems);

public record UpdateClosureRequest(string? Summary, List<string>? Deliverables, List<LessonDto>? Lessons);
public record UpdateChecklistItemRequest(bool IsDone, string? Notes);
public record CloseProjectRequest(bool Force, string? Justification);
public record ReopenProjectRequest(string Reason);

public class UpdateClosureRequestValidator : AbstractValidator<UpdateClosureRequest>
{
    public UpdateClosureRequestValidator()
    {
        RuleFor(x => x.Summary).MaximumLength(4000);
        RuleFor(x => x.Deliverables).Must(d => d is null || d.Count <= 50).WithMessage("Máximo 50 entregables.");
        RuleForEach(x => x.Deliverables).NotEmpty().MaximumLength(300).WithMessage("Cada entregable admite máximo 300 caracteres.");
        RuleFor(x => x.Lessons).Must(l => l is null || l.Count <= 50).WithMessage("Máximo 50 lecciones.");
        RuleForEach(x => x.Lessons).ChildRules(l =>
        {
            l.RuleFor(x => x.Category).NotEmpty().MaximumLength(100);
            l.RuleFor(x => x.WhatWentWell).MaximumLength(2000);
            l.RuleFor(x => x.WhatToImprove).MaximumLength(2000);
            l.RuleFor(x => x.Recommendation).MaximumLength(2000);
        });
    }
}

public class UpdateChecklistItemRequestValidator : AbstractValidator<UpdateChecklistItemRequest>
{
    public UpdateChecklistItemRequestValidator() => RuleFor(x => x.Notes).MaximumLength(1000);
}

public class CloseProjectRequestValidator : AbstractValidator<CloseProjectRequest>
{
    public CloseProjectRequestValidator() => RuleFor(x => x.Justification).MaximumLength(2000);
}

public class ReopenProjectRequestValidator : AbstractValidator<ReopenProjectRequest>
{
    public ReopenProjectRequestValidator() =>
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(10).MaximumLength(1000)
            .WithMessage("Indica el motivo de la reapertura (mínimo 10 caracteres).");
}

public static class ClosureEndpoints
{
    public static RouteGroupBuilder MapClosureEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/projects/{projectId:guid}").WithTags("Cierre de proyecto");
        g.MapGet("/closure", Get);
        g.MapPost("/closure/start", Start);
        g.MapPut("/closure", Update).Validate<UpdateClosureRequest>();
        g.MapPut("/closure/checklist/{itemId:guid}", UpdateChecklistItem).Validate<UpdateChecklistItemRequest>();
        g.MapPost("/closure/cancel", Cancel);
        g.MapPost("/close", Close).Validate<CloseProjectRequest>();
        g.MapPost("/reopen", Reopen).Validate<ReopenProjectRequest>();
        return api;
    }

    private static Task<Project> LoadAsync(AppDbContext db, Guid projectId, CancellationToken ct) =>
        db.Projects
            .Include(p => p.ClosureChecklist).ThenInclude(i => i.DoneBy)
            .Include(p => p.Closure).ThenInclude(c => c!.Lessons)
            .Include(p => p.Closure).ThenInclude(c => c!.ClosedBy)
            .AsSplitQuery()
            .FirstAsync(p => p.Id == projectId, ct);

    private static async Task<ProjectMetrics> MetricsAsync(AppDbContext db, Guid projectId, CancellationToken ct)
    {
        var counts = await db.Tasks.Where(t => t.ProjectId == projectId)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), Completed = g.Count(t => t.Status == TaskItemStatus.Completada) })
            .FirstOrDefaultAsync(ct);
        return new ProjectMetrics(counts?.Total ?? 0, counts?.Completed ?? 0);
    }

    private static async Task<ClosureDto> ToDtoAsync(AppDbContext db, Project p, AppClock clock, CancellationToken ct)
    {
        var metrics = p.IsClosed && p.Closure is not null
            ? new ProjectMetrics(p.Closure.TotalTasks, p.Closure.CompletedTasks)
            : await MetricsAsync(db, p.Id, ct);
        var c = p.Closure;
        var problems = p.Status == ProjectStatus.EnCierre
            ? ClosureRules.Validate(p, metrics, force: false, justification: null)
                .Select(e => new ClosureProblemDto(e.Code, e.Message)).ToList()
            : [];

        return new ClosureDto(
            p.Status,
            p.ClosureChecklist.OrderBy(i => i.Position).Select(i =>
                new ChecklistItemDto(i.Id, i.Text, i.IsRequired, i.IsDone, i.DoneBy?.FullName, i.DoneAt, i.Notes)).ToList(),
            c?.Summary,
            c?.Deliverables ?? [],
            c?.Lessons.OrderBy(l => l.Position).Select(l =>
                new LessonDto(l.Id, l.Category, l.WhatWentWell, l.WhatToImprove, l.Recommendation)).ToList() ?? [],
            new ClosureMetricsDto(metrics.TotalTasks, metrics.CompletedTasks, metrics.OpenTasks, metrics.ProgressPercent),
            p.PlannedStart,
            p.PlannedEnd,
            p.ActualStart,
            p.ActualEnd ?? (p.Status == ProjectStatus.EnCierre ? clock.Today : null),
            c?.IsForced ?? false,
            c?.ForceJustification,
            c?.ClosedBy?.FullName,
            c?.ClosedAt,
            problems);
    }

    private static async Task<IResult> Get(Guid projectId, ProjectGuard guard, AppDbContext db, AppClock clock, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.View, ct);
        return Results.Ok(await ToDtoAsync(db, await LoadAsync(db, projectId, ct), clock, ct));
    }

    private static async Task<IResult> Start(Guid projectId, ProjectGuard guard, AppDbContext db, AppClock clock, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.Close, ct);
        var project = await LoadAsync(db, projectId, ct);
        var template = await db.ClosureChecklistTemplate.AsNoTracking().ToListAsync(ct);
        project.StartClosure(template);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToDtoAsync(db, project, clock, ct));
    }

    private static async Task<IResult> Update(Guid projectId, UpdateClosureRequest req, ProjectGuard guard, AppDbContext db,
        AppClock clock, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.Close, ct);
        var project = await LoadAsync(db, projectId, ct);
        EnsureInClosure(project);
        var closure = project.Closure!;
        closure.Summary = req.Summary?.Trim();
        if (req.Deliverables is not null)
            closure.Deliverables = req.Deliverables.Select(d => d.Trim()).Where(d => d.Length > 0).ToList();
        if (req.Lessons is not null)
        {
            db.LessonsLearned.RemoveRange(closure.Lessons);
            closure.Lessons = req.Lessons.Select((l, i) => new LessonLearned
            {
                ProjectId = projectId,
                Category = l.Category.Trim(),
                WhatWentWell = l.WhatWentWell?.Trim(),
                WhatToImprove = l.WhatToImprove?.Trim(),
                Recommendation = l.Recommendation?.Trim(),
                Position = i
            }).ToList();
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToDtoAsync(db, project, clock, ct));
    }

    private static async Task<IResult> UpdateChecklistItem(Guid projectId, Guid itemId, UpdateChecklistItemRequest req,
        ProjectGuard guard, AppDbContext db, ICurrentUser user, AppClock clock, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.Close, ct);
        var project = await LoadAsync(db, projectId, ct);
        EnsureInClosure(project);
        var item = project.ClosureChecklist.FirstOrDefault(i => i.Id == itemId) ?? throw AppException.NotFound("El ítem");
        if (req.IsDone != item.IsDone)
        {
            item.IsDone = req.IsDone;
            item.DoneById = req.IsDone ? user.Id : null;
            item.DoneAt = req.IsDone ? clock.Now : null;
        }
        item.Notes = req.Notes?.Trim();
        await db.SaveChangesAsync(ct);
        project = await LoadAsync(db, projectId, ct);
        return Results.Ok(await ToDtoAsync(db, project, clock, ct));
    }

    private static async Task<IResult> Cancel(Guid projectId, ProjectGuard guard, AppDbContext db, AppClock clock, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.Close, ct);
        var project = await LoadAsync(db, projectId, ct);
        project.CancelClosure();
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToDtoAsync(db, project, clock, ct));
    }

    private static async Task<IResult> Close(Guid projectId, CloseProjectRequest req, ProjectGuard guard, AppDbContext db,
        ICurrentUser user, AppClock clock, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.Close, ct);
        var project = await LoadAsync(db, projectId, ct);
        var metrics = await MetricsAsync(db, projectId, ct);
        project.Close(metrics, req.Force, req.Justification, user.Id, clock.Now, clock.Today);
        await db.SaveChangesAsync(ct);
        project = await LoadAsync(db, projectId, ct);
        return Results.Ok(await ToDtoAsync(db, project, clock, ct));
    }

    private static async Task<IResult> Reopen(Guid projectId, ReopenProjectRequest req, ProjectGuard guard, AppDbContext db,
        AppClock clock, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.Reopen, ct);
        var project = await LoadAsync(db, projectId, ct);
        project.Reopen(req.Reason);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToDtoAsync(db, project, clock, ct));
    }

    private static void EnsureInClosure(Project project)
    {
        if (project.Status != ProjectStatus.EnCierre)
            throw new DomainException("TRANSICION_INVALIDA", "El proyecto no está en proceso de cierre.");
    }
}
