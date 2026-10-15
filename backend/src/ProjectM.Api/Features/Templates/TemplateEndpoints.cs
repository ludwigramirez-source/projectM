using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ProjectM.Api.Infrastructure;
using ProjectM.Api.Infrastructure.Auth;
using ProjectM.Api.Infrastructure.Errors;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Api.Infrastructure.Validation;
using ProjectM.Domain.Projects;
using ProjectM.Domain.Tasks;

namespace ProjectM.Api.Features.Templates;

public record TemplateTaskDto(string ListName, string Title, string? Description, TaskPriority Priority, int StartOffsetDays, int DurationDays);
public record TemplateDto(Guid Id, string Name, string? Description, List<TemplateTaskDto> Tasks);
public record TemplateListItemDto(Guid Id, string Name, string? Description, int TaskCount, int ListCount);
public record SaveTemplateRequest(string Name, string? Description, List<TemplateTaskDto> Tasks);

public record ChecklistTemplateItemDto(Guid? Id, string Text, bool IsRequired);
public record SaveChecklistTemplateRequest(List<ChecklistTemplateItemDto> Items);

public class SaveTemplateRequestValidator : AbstractValidator<SaveTemplateRequest>
{
    public SaveTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150).WithMessage("El nombre es obligatorio (máx. 150).");
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Tasks).NotNull().Must(t => t.Count <= 300).WithMessage("Máximo 300 tareas por plantilla.");
        RuleForEach(x => x.Tasks).ChildRules(t =>
        {
            t.RuleFor(x => x.ListName).NotEmpty().MaximumLength(100).WithMessage("Cada tarea requiere una lista (máx. 100).");
            t.RuleFor(x => x.Title).NotEmpty().MaximumLength(200).WithMessage("Cada tarea requiere un título (máx. 200).");
            t.RuleFor(x => x.Description).MaximumLength(4000);
            t.RuleFor(x => x.StartOffsetDays).InclusiveBetween(0, 3650).WithMessage("El desfase debe estar entre 0 y 3650 días.");
            t.RuleFor(x => x.DurationDays).InclusiveBetween(1, 3650).WithMessage("La duración debe estar entre 1 y 3650 días.");
        });
    }
}

public class SaveChecklistTemplateRequestValidator : AbstractValidator<SaveChecklistTemplateRequest>
{
    public SaveChecklistTemplateRequestValidator()
    {
        RuleFor(x => x.Items).NotNull().Must(i => i.Count <= 50).WithMessage("Máximo 50 ítems.");
        RuleForEach(x => x.Items).ChildRules(i =>
            i.RuleFor(x => x.Text).NotEmpty().MaximumLength(300).WithMessage("Cada ítem requiere texto (máx. 300)."));
    }
}

public static class TemplateEndpoints
{
    public static RouteGroupBuilder MapTemplateEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/project-templates").WithTags("Plantillas");
        g.MapGet("/", List);
        g.MapGet("/{id:guid}", Get);
        g.MapPost("/", Create).RequireAuthorization(Policies.AdminOrGestor).Validate<SaveTemplateRequest>();
        g.MapPut("/{id:guid}", Update).RequireAuthorization(Policies.AdminOrGestor).Validate<SaveTemplateRequest>();
        g.MapDelete("/{id:guid}", Delete).RequireAuthorization(Policies.AdminOrGestor);

        var c = api.MapGroup("/closure-checklist-template").WithTags("Plantillas");
        c.MapGet("/", GetChecklist);
        c.MapPut("/", SaveChecklist).RequireAuthorization(Policies.Admin).Validate<SaveChecklistTemplateRequest>();
        return api;
    }

    private static async Task<IResult> List(AppDbContext db, CancellationToken ct) =>
        Results.Ok(await db.ProjectTemplates.AsNoTracking().OrderBy(t => t.Name)
            .Select(t => new TemplateListItemDto(t.Id, t.Name, t.Description, t.Tasks.Count,
                t.Tasks.Select(x => x.ListName).Distinct().Count()))
            .ToListAsync(ct));

    private static async Task<IResult> Get(Guid id, AppDbContext db, CancellationToken ct)
    {
        var t = await db.ProjectTemplates.AsNoTracking().Include(x => x.Tasks).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw AppException.NotFound("La plantilla");
        return Results.Ok(ToDto(t));
    }

    private static async Task<IResult> Create(SaveTemplateRequest req, AppDbContext db, AppClock clock, CancellationToken ct)
    {
        await EnsureUniqueName(db, req.Name, null, ct);
        var t = new ProjectTemplate { Name = req.Name.Trim(), Description = req.Description?.Trim(), CreatedAt = clock.Now };
        t.Tasks = MapTasks(t.Id, req.Tasks);
        db.ProjectTemplates.Add(t);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/project-templates/{t.Id}", ToDto(t));
    }

    private static async Task<IResult> Update(Guid id, SaveTemplateRequest req, AppDbContext db, CancellationToken ct)
    {
        var t = await db.ProjectTemplates.Include(x => x.Tasks).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw AppException.NotFound("La plantilla");
        await EnsureUniqueName(db, req.Name, id, ct);
        t.Name = req.Name.Trim();
        t.Description = req.Description?.Trim();
        db.ProjectTemplateTasks.RemoveRange(t.Tasks);
        t.Tasks = MapTasks(t.Id, req.Tasks);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(t));
    }

    private static async Task<IResult> Delete(Guid id, AppDbContext db, CancellationToken ct)
    {
        var deleted = await db.ProjectTemplates.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? throw AppException.NotFound("La plantilla") : Results.NoContent();
    }

    private static async Task<IResult> GetChecklist(AppDbContext db, CancellationToken ct) =>
        Results.Ok(await db.ClosureChecklistTemplate.AsNoTracking().OrderBy(i => i.Position)
            .Select(i => new ChecklistTemplateItemDto(i.Id, i.Text, i.IsRequired)).ToListAsync(ct));

    private static async Task<IResult> SaveChecklist(SaveChecklistTemplateRequest req, AppDbContext db, CancellationToken ct)
    {
        // Afecta solo a cierres que se inicien después (cada proyecto guarda su copia).
        await db.ClosureChecklistTemplate.ExecuteDeleteAsync(ct);
        db.ClosureChecklistTemplate.AddRange(req.Items.Select((i, idx) =>
            new ClosureChecklistTemplateItem { Text = i.Text.Trim(), IsRequired = i.IsRequired, Position = idx }));
        await db.SaveChangesAsync(ct);
        return await GetChecklist(db, ct);
    }

    private static List<ProjectTemplateTask> MapTasks(Guid templateId, List<TemplateTaskDto> tasks) =>
        tasks.Select((x, i) => new ProjectTemplateTask
        {
            TemplateId = templateId,
            ListName = x.ListName.Trim(),
            Title = x.Title.Trim(),
            Description = x.Description?.Trim(),
            Priority = x.Priority,
            StartOffsetDays = x.StartOffsetDays,
            DurationDays = x.DurationDays,
            Position = i
        }).ToList();

    private static TemplateDto ToDto(ProjectTemplate t) => new(t.Id, t.Name, t.Description,
        t.Tasks.OrderBy(x => x.Position).Select(x =>
            new TemplateTaskDto(x.ListName, x.Title, x.Description, x.Priority, x.StartOffsetDays, x.DurationDays)).ToList());

    private static async Task EnsureUniqueName(AppDbContext db, string name, Guid? exceptId, CancellationToken ct)
    {
        var n = name.Trim();
        if (await db.ProjectTemplates.AnyAsync(t => t.Name == n && t.Id != exceptId, ct))
            throw AppException.Validation("name", "Ya existe una plantilla con ese nombre.");
    }
}
