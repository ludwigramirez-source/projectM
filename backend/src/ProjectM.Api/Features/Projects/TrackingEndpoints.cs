using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ProjectM.Api.Features.Tasks;
using ProjectM.Api.Infrastructure;
using ProjectM.Api.Infrastructure.Authorization;
using ProjectM.Api.Infrastructure.Errors;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Api.Infrastructure.Validation;
using ProjectM.Domain.Projects;
using ProjectM.Domain.Tasks;

namespace ProjectM.Api.Features.Projects;

public record MilestoneDto(Guid Id, string Name, string? Description, DateOnly DueDate, DateTimeOffset? CompletedAt,
    int TotalTasks, int CompletedTasks);
public record SaveMilestoneRequest(string Name, string? Description, DateOnly DueDate, bool? Completed);

public record RiskDto(Guid Id, string Title, string? Description, int Probability, int Impact, int Severity,
    RiskStatus Status, Guid? OwnerId, string? OwnerName, string? Mitigation, DateTimeOffset CreatedAt);
public record SaveRiskRequest(string Title, string? Description, int Probability, int Impact, RiskStatus? Status,
    Guid? OwnerId, string? Mitigation);

public record BlockersDto(List<TaskSummaryDto> BlockedTasks, List<TaskSummaryDto> OverdueTasks, List<RiskDto> OpenRisks);

public class SaveMilestoneRequestValidator : AbstractValidator<SaveMilestoneRequest>
{
    public SaveMilestoneRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150).WithMessage("El nombre es obligatorio (máx. 150).");
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public class SaveRiskRequestValidator : AbstractValidator<SaveRiskRequest>
{
    public SaveRiskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200).WithMessage("El título es obligatorio (máx. 200).");
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Mitigation).MaximumLength(2000);
        RuleFor(x => x.Probability).InclusiveBetween(1, 3).WithMessage("La probabilidad debe estar entre 1 y 3.");
        RuleFor(x => x.Impact).InclusiveBetween(1, 3).WithMessage("El impacto debe estar entre 1 y 3.");
    }
}

public static class TrackingEndpoints
{
    public static RouteGroupBuilder MapTrackingEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/projects/{projectId:guid}").WithTags("Seguimiento");
        g.MapGet("/milestones", ListMilestones);
        g.MapPost("/milestones", CreateMilestone).Validate<SaveMilestoneRequest>();
        g.MapPut("/milestones/{id:guid}", UpdateMilestone).Validate<SaveMilestoneRequest>();
        g.MapDelete("/milestones/{id:guid}", DeleteMilestone);

        g.MapGet("/risks", ListRisks);
        g.MapPost("/risks", CreateRisk).Validate<SaveRiskRequest>();
        g.MapPut("/risks/{id:guid}", UpdateRisk).Validate<SaveRiskRequest>();
        g.MapDelete("/risks/{id:guid}", DeleteRisk);

        g.MapGet("/blockers", Blockers);
        return api;
    }

    private static IQueryable<MilestoneDto> MilestoneQuery(AppDbContext db, Guid projectId, Guid? id = null) =>
        db.Milestones.AsNoTracking().Where(m => m.ProjectId == projectId && (id == null || m.Id == id)).OrderBy(m => m.DueDate)
            .Select(m => new MilestoneDto(m.Id, m.Name, m.Description, m.DueDate, m.CompletedAt,
                m.Tasks.Count(t => !t.IsDeleted),
                m.Tasks.Count(t => !t.IsDeleted && t.Status == TaskItemStatus.Completada)));

    private static IQueryable<RiskDto> RiskQuery(AppDbContext db, Guid projectId, Guid? id = null, bool openOnly = false) =>
        db.Risks.AsNoTracking()
            .Where(r => r.ProjectId == projectId && (id == null || r.Id == id) && (!openOnly || r.Status == RiskStatus.Abierto))
            .OrderBy(r => r.Status).ThenByDescending(r => r.Probability * r.Impact)
            .Select(r => new RiskDto(r.Id, r.Title, r.Description, r.Probability, r.Impact, r.Probability * r.Impact,
                r.Status, r.OwnerId, r.Owner != null ? r.Owner.FullName : null, r.Mitigation, r.CreatedAt));

    // ---------------- Hitos ----------------

    private static async Task<IResult> ListMilestones(Guid projectId, ProjectGuard guard, AppDbContext db, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.View, ct);
        return Results.Ok(await MilestoneQuery(db, projectId).ToListAsync(ct));
    }

    private static async Task<IResult> CreateMilestone(Guid projectId, SaveMilestoneRequest req, ProjectGuard guard,
        AppDbContext db, AppClock clock, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.Manage, ct);
        var m = new Milestone
        {
            ProjectId = projectId,
            Name = req.Name.Trim(),
            Description = req.Description?.Trim(),
            DueDate = req.DueDate,
            CompletedAt = req.Completed == true ? clock.Now : null
        };
        db.Milestones.Add(m);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/projects/{projectId}/milestones/{m.Id}",
            await MilestoneQuery(db, projectId, m.Id).FirstAsync(ct));
    }

    private static async Task<IResult> UpdateMilestone(Guid projectId, Guid id, SaveMilestoneRequest req, ProjectGuard guard,
        AppDbContext db, AppClock clock, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.Manage, ct);
        var m = await db.Milestones.FirstOrDefaultAsync(x => x.Id == id && x.ProjectId == projectId, ct)
                ?? throw AppException.NotFound("El hito");
        m.Name = req.Name.Trim();
        m.Description = req.Description?.Trim();
        m.DueDate = req.DueDate;
        if (req.Completed is not null) m.CompletedAt = req.Completed.Value ? m.CompletedAt ?? clock.Now : null;
        await db.SaveChangesAsync(ct);
        return Results.Ok(await MilestoneQuery(db, projectId, id).FirstAsync(ct));
    }

    private static async Task<IResult> DeleteMilestone(Guid projectId, Guid id, ProjectGuard guard, AppDbContext db, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.Manage, ct);
        var m = await db.Milestones.FirstOrDefaultAsync(x => x.Id == id && x.ProjectId == projectId, ct)
                ?? throw AppException.NotFound("El hito");
        // FK sin cascada: desvincular tareas antes de borrar.
        await db.Tasks.IgnoreQueryFilters().Where(t => t.MilestoneId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.MilestoneId, (Guid?)null), ct);
        db.Milestones.Remove(m);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ---------------- Riesgos ----------------

    private static async Task<IResult> ListRisks(Guid projectId, ProjectGuard guard, AppDbContext db, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.View, ct);
        return Results.Ok(await RiskQuery(db, projectId).ToListAsync(ct));
    }

    private static async Task<IResult> CreateRisk(Guid projectId, SaveRiskRequest req, ProjectGuard guard,
        AppDbContext db, AppClock clock, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.Manage, ct);
        await EnsureOwnerIsMember(db, projectId, req.OwnerId, ct);
        var r = new Risk { ProjectId = projectId, CreatedAt = clock.Now };
        Apply(r, req);
        db.Risks.Add(r);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/projects/{projectId}/risks/{r.Id}", await RiskQuery(db, projectId, r.Id).FirstAsync(ct));
    }

    private static async Task<IResult> UpdateRisk(Guid projectId, Guid id, SaveRiskRequest req, ProjectGuard guard,
        AppDbContext db, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.Manage, ct);
        var r = await db.Risks.FirstOrDefaultAsync(x => x.Id == id && x.ProjectId == projectId, ct)
                ?? throw AppException.NotFound("El riesgo");
        await EnsureOwnerIsMember(db, projectId, req.OwnerId, ct);
        Apply(r, req);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await RiskQuery(db, projectId, id).FirstAsync(ct));
    }

    private static async Task<IResult> DeleteRisk(Guid projectId, Guid id, ProjectGuard guard, AppDbContext db, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.Manage, ct);
        var deleted = await db.Risks.Where(x => x.Id == id && x.ProjectId == projectId).ExecuteDeleteAsync(ct);
        return deleted == 0 ? throw AppException.NotFound("El riesgo") : Results.NoContent();
    }

    private static void Apply(Risk r, SaveRiskRequest req)
    {
        r.Title = req.Title.Trim();
        r.Description = req.Description?.Trim();
        r.Probability = req.Probability;
        r.Impact = req.Impact;
        r.Status = req.Status ?? r.Status;
        r.OwnerId = req.OwnerId;
        r.Mitigation = req.Mitigation?.Trim();
    }

    private static async Task EnsureOwnerIsMember(AppDbContext db, Guid projectId, Guid? ownerId, CancellationToken ct)
    {
        if (ownerId is not null && !await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == ownerId, ct))
            throw AppException.Validation("ownerId", "El responsable debe ser miembro del proyecto.");
    }

    // ---------------- Riesgos y bloqueos ----------------

    private static async Task<IResult> Blockers(Guid projectId, ProjectGuard guard, AppDbContext db, AppClock clock, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.View, ct);
        var today = clock.Today;
        var tasks = db.Tasks.AsNoTracking().Where(t => t.ProjectId == projectId);
        var blocked = await tasks.Where(t => t.Status == TaskItemStatus.Bloqueada)
            .OrderBy(t => t.DueDate).Select(TaskSummaryDto.Projection).Take(100).ToListAsync(ct);
        var overdue = await tasks.Where(t => t.Status != TaskItemStatus.Completada && t.DueDate < today)
            .OrderBy(t => t.DueDate).Select(TaskSummaryDto.Projection).Take(100).ToListAsync(ct);
        var risks = await RiskQuery(db, projectId, openOnly: true).ToListAsync(ct);
        return Results.Ok(new BlockersDto(blocked, overdue, risks));
    }
}
