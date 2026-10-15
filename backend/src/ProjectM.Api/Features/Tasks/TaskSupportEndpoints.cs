using FluentValidation;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectM.Api.Features.Projects;
using ProjectM.Api.Infrastructure;
using ProjectM.Api.Infrastructure.Auth;
using ProjectM.Api.Infrastructure.Authorization;
using ProjectM.Api.Infrastructure.Errors;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Api.Infrastructure.Storage;
using ProjectM.Api.Infrastructure.Validation;
using ProjectM.Domain.Tasks;
using ProjectM.Domain.Users;

namespace ProjectM.Api.Features.Tasks;

public record SaveListRequest(string Name);
public record ReorderListsRequest(List<Guid> Ids);
public record SaveTagRequest(string Name, string? Color);
public record CustomFieldDto(Guid Id, string Name, CustomFieldType Type, List<string> Options, bool IsRequired, int Position);
public record SaveCustomFieldRequest(string Name, CustomFieldType Type, List<string>? Options, bool IsRequired);

public class SaveListRequestValidator : AbstractValidator<SaveListRequest>
{
    public SaveListRequestValidator() =>
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("El nombre es obligatorio (máx. 100).");
}

public class SaveTagRequestValidator : AbstractValidator<SaveTagRequest>
{
    public SaveTagRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50).WithMessage("El nombre es obligatorio (máx. 50).");
        RuleFor(x => x.Color).Must(c => c is null || Spaces.SaveSpaceRequestValidator.Colors.Contains(c)).WithMessage("Color no válido.");
    }
}

public class SaveCustomFieldRequestValidator : AbstractValidator<SaveCustomFieldRequest>
{
    public SaveCustomFieldRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("El nombre es obligatorio (máx. 100).");
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Options).Must(o => o is { Count: > 0 } && o.All(v => !string.IsNullOrWhiteSpace(v)) && o.Distinct().Count() == o.Count)
            .When(x => x.Type == CustomFieldType.Seleccion)
            .WithMessage("Una selección requiere al menos una opción y sin repetidos.");
        RuleFor(x => x.Options).Must(o => o is null || o.Count <= 50).WithMessage("Máximo 50 opciones.");
        RuleForEach(x => x.Options).MaximumLength(100);
    }
}

public static class TaskSupportEndpoints
{
    public static RouteGroupBuilder MapTaskSupportEndpoints(this RouteGroupBuilder api)
    {
        // Listas
        var p = api.MapGroup("/projects/{projectId:guid}").WithTags("Listas y campos");
        p.MapGet("/lists", Lists);
        p.MapPost("/lists", CreateList).Validate<SaveListRequest>();
        p.MapPost("/lists/reorder", ReorderLists);
        api.MapPut("/lists/{id:guid}", RenameList).WithTags("Listas y campos").Validate<SaveListRequest>();
        api.MapDelete("/lists/{id:guid}", DeleteList).WithTags("Listas y campos");

        // Campos personalizados
        p.MapGet("/custom-fields", Fields);
        p.MapPost("/custom-fields", CreateField).Validate<SaveCustomFieldRequest>();
        api.MapPut("/custom-fields/{id:guid}", UpdateField).WithTags("Listas y campos").Validate<SaveCustomFieldRequest>();
        api.MapDelete("/custom-fields/{id:guid}", DeleteField).WithTags("Listas y campos");
        api.MapPut("/tasks/{taskId:guid}/custom-fields/{fieldId:guid}", SetFieldValue).WithTags("Tareas");

        // Etiquetas (globales)
        var t = api.MapGroup("/tags").WithTags("Etiquetas");
        t.MapGet("/", Tags);
        t.MapPost("/", CreateTag).Validate<SaveTagRequest>();
        t.MapPut("/{id:guid}", UpdateTag).RequireAuthorization(Policies.AdminOrGestor).Validate<SaveTagRequest>();
        t.MapDelete("/{id:guid}", DeleteTag).RequireAuthorization(Policies.AdminOrGestor);

        // Adjuntos
        api.MapGet("/tasks/{taskId:guid}/attachments", Attachments).WithTags("Adjuntos");
        api.MapPost("/tasks/{taskId:guid}/attachments", Upload).WithTags("Adjuntos").DisableAntiforgery();
        api.MapGet("/attachments/{id:guid}/download", Download).WithTags("Adjuntos");
        api.MapDelete("/attachments/{id:guid}", DeleteAttachment).WithTags("Adjuntos");
        return api;
    }

    // ---------------- Listas ----------------

    private static IQueryable<Projects.ListDto> ListQuery(AppDbContext db, Guid projectId, Guid? id = null) =>
        db.TaskLists.AsNoTracking().Where(l => l.ProjectId == projectId && (id == null || l.Id == id)).OrderBy(l => l.Position)
            .Select(l => new Projects.ListDto(l.Id, l.Name, l.Position, l.Tasks.Count(x => !x.IsDeleted)));

    private static async Task<IResult> Lists(Guid projectId, ProjectGuard guard, AppDbContext db, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.View, ct);
        return Results.Ok(await ListQuery(db, projectId).ToListAsync(ct));
    }

    private static async Task<IResult> CreateList(Guid projectId, SaveListRequest req, ProjectGuard guard, AppDbContext db,
        AppClock clock, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.Manage, ct);
        var list = new TaskList
        {
            ProjectId = projectId,
            Name = req.Name.Trim(),
            Position = await db.TaskLists.CountAsync(l => l.ProjectId == projectId, ct),
            CreatedAt = clock.Now
        };
        db.TaskLists.Add(list);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/lists/{list.Id}", new Projects.ListDto(list.Id, list.Name, list.Position, 0));
    }

    private static async Task<IResult> RenameList(Guid id, SaveListRequest req, ProjectGuard guard, AppDbContext db, CancellationToken ct)
    {
        var list = await db.TaskLists.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw AppException.NotFound("La lista");
        await guard.EnsureAsync(list.ProjectId, ProjectPermission.Manage, ct);
        list.Name = req.Name.Trim();
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ListQuery(db, list.ProjectId, id).FirstAsync(ct));
    }

    private static async Task<IResult> DeleteList(Guid id, ProjectGuard guard, AppDbContext db, CancellationToken ct)
    {
        var list = await db.TaskLists.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw AppException.NotFound("La lista");
        await guard.EnsureAsync(list.ProjectId, ProjectPermission.Manage, ct);
        if (await db.Tasks.AnyAsync(t => t.ListId == id, ct))
            throw AppException.Conflict("LISTA_CON_TAREAS", "Mueve o elimina las tareas antes de eliminar la lista.");
        if (await db.TaskLists.CountAsync(l => l.ProjectId == list.ProjectId, ct) == 1)
            throw AppException.Conflict("ULTIMA_LISTA", "El proyecto debe tener al menos una lista.");
        // Tareas eliminadas (soft delete) también pertenecen a la lista: se borran físicamente con ella.
        await db.Tasks.IgnoreQueryFilters().Where(t => t.ListId == id && t.ParentTaskId != null).ExecuteDeleteAsync(ct);
        await db.Tasks.IgnoreQueryFilters().Where(t => t.ListId == id).ExecuteDeleteAsync(ct);
        db.TaskLists.Remove(list);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ReorderLists(Guid projectId, ReorderListsRequest req, ProjectGuard guard, AppDbContext db, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.Manage, ct);
        var lists = await db.TaskLists.Where(l => l.ProjectId == projectId).ToListAsync(ct);
        if (lists.Count != req.Ids.Count || lists.Any(l => !req.Ids.Contains(l.Id)))
            throw AppException.Validation("ids", "Debes enviar todas las listas del proyecto.");
        foreach (var l in lists) l.Position = req.Ids.IndexOf(l.Id);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ListQuery(db, projectId).ToListAsync(ct));
    }

    // ---------------- Campos personalizados ----------------

    private static async Task<IResult> Fields(Guid projectId, ProjectGuard guard, AppDbContext db, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.View, ct);
        return Results.Ok(await db.CustomFields.AsNoTracking().Where(f => f.ProjectId == projectId).OrderBy(f => f.Position)
            .Select(f => new CustomFieldDto(f.Id, f.Name, f.Type, f.Options, f.IsRequired, f.Position)).ToListAsync(ct));
    }

    private static async Task<IResult> CreateField(Guid projectId, SaveCustomFieldRequest req, ProjectGuard guard, AppDbContext db, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.Manage, ct);
        var f = new CustomFieldDefinition
        {
            ProjectId = projectId,
            Name = req.Name.Trim(),
            Type = req.Type,
            Options = req.Type == CustomFieldType.Seleccion ? req.Options!.Select(o => o.Trim()).ToList() : [],
            IsRequired = req.IsRequired,
            Position = await db.CustomFields.CountAsync(x => x.ProjectId == projectId, ct)
        };
        db.CustomFields.Add(f);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/custom-fields/{f.Id}", new CustomFieldDto(f.Id, f.Name, f.Type, f.Options, f.IsRequired, f.Position));
    }

    private static async Task<IResult> UpdateField(Guid id, SaveCustomFieldRequest req, ProjectGuard guard, AppDbContext db, CancellationToken ct)
    {
        var f = await db.CustomFields.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("El campo");
        await guard.EnsureAsync(f.ProjectId, ProjectPermission.Manage, ct);
        if (req.Type != f.Type && await db.CustomFieldValues.AnyAsync(v => v.FieldId == id, ct))
            throw AppException.Conflict("CAMPO_CON_VALORES", "No se puede cambiar el tipo de un campo que ya tiene valores.");
        f.Name = req.Name.Trim();
        f.Type = req.Type;
        f.IsRequired = req.IsRequired;
        f.Options = req.Type == CustomFieldType.Seleccion ? req.Options!.Select(o => o.Trim()).ToList() : [];
        if (f.Type == CustomFieldType.Seleccion)
        {
            // Valores que ya no son opciones válidas se eliminan.
            var options = f.Options;
            await db.CustomFieldValues.Where(v => v.FieldId == id && !options.Contains(v.TextValue!)).ExecuteDeleteAsync(ct);
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(new CustomFieldDto(f.Id, f.Name, f.Type, f.Options, f.IsRequired, f.Position));
    }

    private static async Task<IResult> DeleteField(Guid id, ProjectGuard guard, AppDbContext db, CancellationToken ct)
    {
        var f = await db.CustomFields.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("El campo");
        await guard.EnsureAsync(f.ProjectId, ProjectPermission.Manage, ct);
        await db.CustomFieldValues.Where(v => v.FieldId == id).ExecuteDeleteAsync(ct);
        db.CustomFields.Remove(f);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetFieldValue(Guid taskId, Guid fieldId, SetCustomFieldRequest req, ProjectGuard guard,
        AppDbContext db, AppClock clock, TaskQueries queries, CancellationToken ct)
    {
        var ctx = await guard.EnsureForTaskAsync(taskId, ProjectPermission.EditTasks, ct);
        var field = await db.CustomFields.FirstOrDefaultAsync(f => f.Id == fieldId && f.ProjectId == ctx.ProjectId, ct)
                    ?? throw AppException.NotFound("El campo");
        var parsed = field.Parse(taskId, req.Value);
        await db.CustomFieldValues.Where(v => v.TaskId == taskId && v.FieldId == fieldId).ExecuteDeleteAsync(ct);
        if (parsed is not null) db.CustomFieldValues.Add(parsed);
        await db.Tasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.UpdatedAt, clock.Now), ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await queries.DetailAsync(taskId, true, ct));
    }

    // ---------------- Etiquetas ----------------

    private static async Task<IResult> Tags(AppDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Tags.AsNoTracking().OrderBy(t => t.Name).Select(t => new TagDto(t.Id, t.Name, t.Color)).ToListAsync(ct));

    private static async Task<IResult> CreateTag(SaveTagRequest req, AppDbContext db, ICurrentUser user, CancellationToken ct)
    {
        if (user.IsInRole(Roles.Observador)) throw AppException.Forbidden();
        var name = req.Name.Trim();
        var existing = await db.Tags.FirstOrDefaultAsync(t => t.Name == name, ct);
        if (existing is not null) return Results.Ok(new TagDto(existing.Id, existing.Name, existing.Color));
        var tag = new Tag { Name = name, Color = req.Color ?? "secondary" };
        db.Tags.Add(tag);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/tags/{tag.Id}", new TagDto(tag.Id, tag.Name, tag.Color));
    }

    private static async Task<IResult> UpdateTag(Guid id, SaveTagRequest req, AppDbContext db, CancellationToken ct)
    {
        var tag = await db.Tags.FindAsync([id], ct) ?? throw AppException.NotFound("La etiqueta");
        var name = req.Name.Trim();
        if (await db.Tags.AnyAsync(t => t.Name == name && t.Id != id, ct))
            throw AppException.Validation("name", "Ya existe una etiqueta con ese nombre.");
        tag.Name = name;
        tag.Color = req.Color ?? tag.Color;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new TagDto(tag.Id, tag.Name, tag.Color));
    }

    private static async Task<IResult> DeleteTag(Guid id, AppDbContext db, CancellationToken ct)
    {
        var deleted = await db.Tags.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? throw AppException.NotFound("La etiqueta") : Results.NoContent();
    }

    // ---------------- Adjuntos ----------------

    private static async Task<IResult> Attachments(Guid taskId, ProjectGuard guard, AppDbContext db, CancellationToken ct)
    {
        await guard.EnsureForTaskAsync(taskId, ProjectPermission.View, ct);
        return Results.Ok(await db.Attachments.AsNoTracking().Where(a => a.TaskId == taskId).OrderByDescending(a => a.CreatedAt)
            .Select(a => new AttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.UploadedBy.FullName, a.CreatedAt))
            .ToListAsync(ct));
    }

    private static async Task<IResult> Upload(Guid taskId, IFormFile? file, ProjectGuard guard, AppDbContext db, IFileStorage storage,
        IOptions<StorageOptions> options, ICurrentUser user, AppClock clock, CancellationToken ct)
    {
        var ctx = await guard.EnsureForTaskAsync(taskId, ProjectPermission.EditTasks, ct);
        if (file is null || file.Length == 0) throw AppException.Validation("file", "Selecciona un archivo.");
        if (file.Length > options.Value.MaxFileSizeBytes)
            throw AppException.Validation("file", $"El archivo supera el máximo de {options.Value.MaxFileSizeMb} MB.");

        var fileName = Path.GetFileName(file.FileName).Trim();
        if (fileName.Length is 0 or > 255) throw AppException.Validation("file", "Nombre de archivo no válido.");
        var extension = Path.GetExtension(fileName);
        if (!StorageOptions.AllowedExtensions.Contains(extension))
            throw AppException.Validation("file", $"Tipo de archivo no permitido ({extension}).");

        // El tipo MIME se deriva de la extensión (lista blanca), no del encabezado enviado por el cliente.
        new FileExtensionContentTypeProvider().TryGetContentType(fileName, out var contentType);

        await using var stream = file.OpenReadStream();
        var key = await storage.SaveAsync(stream, ct);
        var attachment = new Attachment
        {
            TaskId = taskId,
            FileName = fileName,
            ContentType = contentType ?? "application/octet-stream",
            SizeBytes = file.Length,
            StorageKey = key,
            UploadedById = user.Id,
            CreatedAt = clock.Now
        };
        attachment.Raise(new Domain.Collaboration.AttachmentAdded(taskId, ctx.ProjectId, fileName));
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/attachments/{attachment.Id}/download",
            new AttachmentDto(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, user.Name, attachment.CreatedAt));
    }

    private static async Task<IResult> Download(Guid id, ProjectGuard guard, AppDbContext db, IFileStorage storage, CancellationToken ct)
    {
        var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("El adjunto");
        await guard.EnsureForTaskAsync(a.TaskId, ProjectPermission.View, ct);
        Stream stream;
        try { stream = await storage.OpenReadAsync(a.StorageKey, ct); }
        catch (FileNotFoundException) { throw AppException.NotFound("El archivo"); }
        return Results.File(stream, a.ContentType, a.FileName, enableRangeProcessing: true);
    }

    private static async Task<IResult> DeleteAttachment(Guid id, ProjectGuard guard, AppDbContext db, CancellationToken ct)
    {
        var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("El adjunto");
        await guard.EnsureForTaskAsync(a.TaskId, ProjectPermission.EditTasks, ct);
        a.IsDeleted = true; // el archivo se conserva para el historial
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
