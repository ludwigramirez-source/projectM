using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ProjectM.Api.Features.Projects;
using ProjectM.Api.Infrastructure;
using ProjectM.Api.Infrastructure.Auth;
using ProjectM.Api.Infrastructure.Authorization;
using ProjectM.Api.Infrastructure.Errors;
using ProjectM.Api.Infrastructure.Pagination;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Api.Infrastructure.Validation;
using ProjectM.Domain.Tasks;

namespace ProjectM.Api.Features.Tasks;

public static class TaskEndpoints
{
    public static RouteGroupBuilder MapTaskEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/tasks").WithTags("Tareas");
        g.MapGet("/", List);
        g.MapPost("/", Create).Validate<CreateTaskRequest>();
        g.MapGet("/{id:guid}", Get);
        g.MapPut("/{id:guid}", Update).Validate<UpdateTaskRequest>();
        g.MapDelete("/{id:guid}", Delete);
        g.MapPatch("/{id:guid}/status", ChangeStatus).Validate<ChangeStatusRequest>();
        g.MapPatch("/{id:guid}/move", Move).Validate<MoveTaskRequest>();
        g.MapGet("/{id:guid}/subtasks", Subtasks);
        g.MapPut("/{id:guid}/tags", SetTags).Validate<SetTagsRequest>();
        return api;
    }

    public static Expression<Func<TaskItem, TaskListItemDto>> ListProjection(DateOnly today) => t => new TaskListItemDto(
        t.Id, t.ProjectId, t.Project.Name, t.ListId, t.List.Name, t.ParentTaskId, t.Title, t.Status, t.Priority,
        t.StartDate, t.DueDate, t.AssigneeId, t.Assignee != null ? t.Assignee.FullName : null, t.MilestoneId, t.Position,
        t.Subtasks.Count(s => !s.IsDeleted),
        t.Subtasks.Count(s => !s.IsDeleted && s.Status == TaskItemStatus.Completada),
        t.Attachments.Count(a => !a.IsDeleted),
        0,
        t.Status != TaskItemStatus.Completada && t.DueDate < today,
        t.Tags.Select(tt => new TagDto(tt.Tag.Id, tt.Tag.Name, tt.Tag.Color)).ToList());

    // ---------------- Consultas ----------------

    private static async Task<IResult> List(ProjectGuard guard, AppDbContext db, ICurrentUser user, AppClock clock,
        Guid? projectId, Guid? listId, TaskItemStatus[]? status, Guid? assigneeId, bool? mine, TaskPriority? priority,
        Guid? tagId, DateOnly? dueFrom, DateOnly? dueTo, bool? overdue, string? q, Guid? parentId, bool? includeSubtasks,
        Guid? milestoneId, string? sort, int? page, int? pageSize, CancellationToken ct)
    {
        var today = clock.Today;
        IQueryable<TaskItem> tasks;
        if (projectId is not null)
        {
            await guard.EnsureAsync(projectId.Value, ProjectPermission.View, ct);
            tasks = db.Tasks.Where(t => t.ProjectId == projectId);
        }
        else
        {
            var visible = guard.VisibleProjects().Select(p => p.Id);
            tasks = db.Tasks.Where(t => visible.Contains(t.ProjectId));
        }
        tasks = tasks.AsNoTracking();

        if (listId is not null) tasks = tasks.Where(t => t.ListId == listId);
        if (status is { Length: > 0 }) tasks = tasks.Where(t => status.Contains(t.Status));
        if (mine == true) assigneeId = user.Id;
        if (assigneeId is not null) tasks = tasks.Where(t => t.AssigneeId == assigneeId);
        if (priority is not null) tasks = tasks.Where(t => t.Priority == priority);
        if (tagId is not null) tasks = tasks.Where(t => t.Tags.Any(x => x.TagId == tagId));
        if (milestoneId is not null) tasks = tasks.Where(t => t.MilestoneId == milestoneId);
        if (dueFrom is not null) tasks = tasks.Where(t => t.DueDate >= dueFrom);
        if (dueTo is not null) tasks = tasks.Where(t => t.DueDate <= dueTo);
        if (overdue == true) tasks = tasks.Where(t => t.Status != TaskItemStatus.Completada && t.DueDate < today);
        if (!string.IsNullOrWhiteSpace(q)) tasks = tasks.Where(t => t.Title.Contains(q.Trim()));
        if (parentId is not null) tasks = tasks.Where(t => t.ParentTaskId == parentId);
        else if (includeSubtasks != true) tasks = tasks.Where(t => t.ParentTaskId == null);

        tasks = (sort ?? "position") switch
        {
            "dueDate" => tasks.OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenBy(t => t.Position),
            "-dueDate" => tasks.OrderByDescending(t => t.DueDate).ThenBy(t => t.Position),
            "priority" => tasks.OrderBy(t => t.Priority == TaskPriority.Urgente ? 0 : t.Priority == TaskPriority.Alta ? 1
                : t.Priority == TaskPriority.Normal ? 2 : 3).ThenBy(t => t.Position),
            "title" => tasks.OrderBy(t => t.Title),
            "-createdAt" => tasks.OrderByDescending(t => t.CreatedAt),
            "createdAt" => tasks.OrderBy(t => t.CreatedAt),
            _ => tasks.OrderBy(t => t.Position)
        };

        var (p, s) = Paging.Normalize(page, pageSize ?? 100);
        var total = await tasks.CountAsync(ct);
        var items = await tasks.Skip((p - 1) * s).Take(s).Select(ListProjection(today)).ToListAsync(ct);
        return Results.Ok(new PagedResult<TaskListItemDto>(items, p, s, total));
    }

    private static async Task<IResult> Get(Guid id, ProjectGuard guard, TaskQueries queries, CancellationToken ct)
    {
        var ctx = await guard.EnsureForTaskAsync(id, ProjectPermission.View, ct);
        return Results.Ok(await queries.DetailAsync(id, guard.PermissionsFor(ctx).CanEditTasks, ct));
    }

    private static async Task<IResult> Subtasks(Guid id, ProjectGuard guard, AppDbContext db, AppClock clock, CancellationToken ct)
    {
        await guard.EnsureForTaskAsync(id, ProjectPermission.View, ct);
        return Results.Ok(await db.Tasks.AsNoTracking().Where(t => t.ParentTaskId == id).OrderBy(t => t.Position)
            .Select(ListProjection(clock.Today)).ToListAsync(ct));
    }

    // ---------------- Escritura ----------------

    private static async Task<IResult> Create(CreateTaskRequest req, AppDbContext db, ProjectGuard guard, ICurrentUser user,
        AppClock clock, TaskQueries queries, CancellationToken ct)
    {
        var list = await db.TaskLists.AsNoTracking().FirstOrDefaultAsync(l => l.Id == req.ListId, ct)
                   ?? throw AppException.Validation("listId", "La lista no existe.");
        await guard.EnsureAsync(list.ProjectId, ProjectPermission.EditTasks, ct);

        var now = clock.Now;
        var task = new TaskItem
        {
            ProjectId = list.ProjectId,
            ListId = list.Id,
            Title = req.Title.Trim(),
            Description = req.Description,
            Priority = req.Priority ?? TaskPriority.Normal,
            CreatedById = user.Id,
            CreatedAt = now,
            UpdatedAt = now
        };

        if (req.ParentTaskId is not null)
        {
            var parent = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == req.ParentTaskId, ct);
            if (parent is null || parent.ProjectId != list.ProjectId)
                throw AppException.Validation("parentTaskId", "La tarea padre no existe en este proyecto.");
            if (parent.ParentTaskId is not null)
                throw AppException.Validation("parentTaskId", "Las subtareas no pueden tener subtareas (un solo nivel).");
            task.ParentTaskId = parent.Id;
            task.ListId = parent.ListId;
        }

        await TaskRules.ValidateRefsAsync(db, list.ProjectId, req.AssigneeId, req.MilestoneId, ct);
        task.Assign(req.AssigneeId);
        task.MilestoneId = req.MilestoneId;
        task.SetDates(req.StartDate, req.DueDate);
        if (req.Status is not null and not TaskItemStatus.Pendiente) task.ChangeStatus(req.Status.Value, req.BlockedReason, now);
        task.Position = await TaskOrdering.NextPositionAsync(db, task.ListId, ct);

        if (req.TagIds is { Count: > 0 })
        {
            var tags = await db.Tags.Where(t => req.TagIds.Contains(t.Id)).Select(t => t.Id).ToListAsync(ct);
            task.Tags = tags.Select(tagId => new TaskTag { TaskId = task.Id, TagId = tagId }).ToList();
        }

        var fields = await db.CustomFields.Where(f => f.ProjectId == list.ProjectId).ToListAsync(ct);
        foreach (var field in fields)
        {
            string? raw = null;
            req.CustomFields?.TryGetValue(field.Id, out raw);
            var value = field.Parse(task.Id, raw, enforceRequired: false);
            if (value is not null) task.CustomFieldValues.Add(value);
        }

        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/tasks/{task.Id}", await queries.DetailAsync(task.Id, true, ct));
    }

    private static async Task<IResult> Update(Guid id, UpdateTaskRequest req, AppDbContext db, ProjectGuard guard,
        AppClock clock, TaskQueries queries, CancellationToken ct)
    {
        var ctx = await guard.EnsureForTaskAsync(id, ProjectPermission.EditTasks, ct);
        var task = await db.Tasks.FirstAsync(t => t.Id == id, ct);
        ConcurrencyHelper.SetOriginalRowVersion(db, task, req.RowVersion);
        await TaskRules.ValidateRefsAsync(db, ctx.ProjectId, req.AssigneeId, req.MilestoneId, ct);

        task.Title = req.Title.Trim();
        task.Description = req.Description;
        task.Assign(req.AssigneeId);
        task.Priority = req.Priority;
        task.SetDates(req.StartDate, req.DueDate);
        task.MilestoneId = req.MilestoneId;
        task.UpdatedAt = clock.Now;
        await db.SaveChangesAsync(ct);
        return Results.Ok(await queries.DetailAsync(id, true, ct));
    }

    private static async Task<IResult> ChangeStatus(Guid id, ChangeStatusRequest req, AppDbContext db, ProjectGuard guard,
        AppClock clock, TaskQueries queries, CancellationToken ct)
    {
        await guard.EnsureForTaskAsync(id, ProjectPermission.EditTasks, ct);
        var task = await db.Tasks.FirstAsync(t => t.Id == id, ct);
        if (req.Status == TaskItemStatus.Completada) await TaskRules.EnsureRequiredFieldsAsync(db, task, ct);
        task.ChangeStatus(req.Status, req.BlockedReason, clock.Now);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await queries.DetailAsync(id, true, ct));
    }

    private static async Task<IResult> Move(Guid id, MoveTaskRequest req, AppDbContext db, ProjectGuard guard,
        AppClock clock, CancellationToken ct)
    {
        var ctx = await guard.EnsureForTaskAsync(id, ProjectPermission.EditTasks, ct);
        var task = await db.Tasks.FirstAsync(t => t.Id == id, ct);

        if (req.ListId is not null && req.ListId != task.ListId)
        {
            if (!await db.TaskLists.AnyAsync(l => l.Id == req.ListId && l.ProjectId == ctx.ProjectId, ct))
                throw AppException.Validation("listId", "La lista no pertenece al proyecto.");
            task.ListId = req.ListId.Value;
            // Las subtareas acompañan a su tarea padre.
            await db.Tasks.Where(t => t.ParentTaskId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ListId, req.ListId.Value), ct);
        }
        if (req.Status is not null && req.Status != task.Status)
        {
            if (req.Status == TaskItemStatus.Completada) await TaskRules.EnsureRequiredFieldsAsync(db, task, ct);
            task.ChangeStatus(req.Status.Value, req.BlockedReason, clock.Now);
        }

        task.Position = await TaskOrdering.PositionBetweenAsync(db, ctx.ProjectId, req.BeforeId, req.AfterId, id, ct);
        task.UpdatedAt = clock.Now;
        await db.SaveChangesAsync(ct);

        var item = await db.Tasks.AsNoTracking().Where(t => t.Id == id).Select(ListProjection(clock.Today)).FirstAsync(ct);
        return Results.Ok(item);
    }

    private static async Task<IResult> Delete(Guid id, AppDbContext db, ProjectGuard guard, AppClock clock, CancellationToken ct)
    {
        await guard.EnsureForTaskAsync(id, ProjectPermission.EditTasks, ct);
        var now = clock.Now;
        await db.Tasks.Where(t => t.Id == id || t.ParentTaskId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDeleted, true).SetProperty(t => t.UpdatedAt, now), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetTags(Guid id, SetTagsRequest req, AppDbContext db, ProjectGuard guard,
        TaskQueries queries, CancellationToken ct)
    {
        await guard.EnsureForTaskAsync(id, ProjectPermission.EditTasks, ct);
        var valid = await db.Tags.Where(t => req.TagIds.Contains(t.Id)).Select(t => t.Id).ToListAsync(ct);
        await db.TaskTags.Where(t => t.TaskId == id).ExecuteDeleteAsync(ct);
        db.TaskTags.AddRange(valid.Select(tagId => new TaskTag { TaskId = id, TagId = tagId }));
        await db.SaveChangesAsync(ct);
        return Results.Ok(await queries.DetailAsync(id, true, ct));
    }
}

public static class TaskRules
{
    /// <summary>Una tarea no se puede completar con campos personalizados obligatorios vacíos.</summary>
    public static async Task EnsureRequiredFieldsAsync(AppDbContext db, TaskItem task, CancellationToken ct)
    {
        var missing = await db.CustomFields
            .Where(f => f.ProjectId == task.ProjectId && f.IsRequired &&
                        !db.CustomFieldValues.Any(v => v.FieldId == f.Id && v.TaskId == task.Id))
            .Select(f => f.Name).ToListAsync(ct);
        if (missing.Count > 0)
            throw AppException.BadRequest("CAMPOS_REQUERIDOS",
                $"Completa los campos obligatorios antes de cerrar la tarea: {string.Join(", ", missing)}.");
    }

    /// <summary>El responsable debe ser miembro del proyecto y el hito debe pertenecer a él.</summary>
    public static async Task ValidateRefsAsync(AppDbContext db, Guid projectId, Guid? assigneeId, Guid? milestoneId, CancellationToken ct)
    {
        if (assigneeId is not null &&
            !await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == assigneeId, ct))
            throw AppException.Validation("assigneeId", "El responsable debe ser miembro del proyecto.");
        if (milestoneId is not null && !await db.Milestones.AnyAsync(m => m.Id == milestoneId && m.ProjectId == projectId, ct))
            throw AppException.Validation("milestoneId", "El hito no pertenece al proyecto.");
    }
}

/// <summary>Orden fraccional: insertar entre dos vecinas sin renumerar todo (salvo cuando el hueco se agota).</summary>
public static class TaskOrdering
{
    public const double Step = 1000;
    private const double MinGap = 1e-6;

    public static async Task<double> NextPositionAsync(AppDbContext db, Guid listId, CancellationToken ct)
    {
        var max = await db.Tasks.IgnoreQueryFilters().Where(t => t.ListId == listId).MaxAsync(t => (double?)t.Position, ct);
        return (max ?? 0) + Step;
    }

    public static double Between(double? before, double? after) => (before, after) switch
    {
        (null, null) => Step,
        (null, { } a) => a - Step,
        ({ } b, null) => b + Step,
        ({ } b, { } a) => (b + a) / 2
    };

    public static async Task<double> PositionBetweenAsync(AppDbContext db, Guid projectId, Guid? beforeId, Guid? afterId,
        Guid movingId, CancellationToken ct)
    {
        var ids = new[] { beforeId, afterId }.Where(x => x is not null).Select(x => x!.Value).ToList();
        var positions = await db.Tasks.Where(t => ids.Contains(t.Id) && t.ProjectId == projectId)
            .ToDictionaryAsync(t => t.Id, t => t.Position, ct);
        if (positions.Count != ids.Count) throw AppException.Validation("beforeId", "Las tareas vecinas no pertenecen al proyecto.");

        double? before = beforeId is null ? null : positions[beforeId.Value];
        double? after = afterId is null ? null : positions[afterId.Value];
        if (before is null || after is null || Math.Abs(after.Value - before.Value) >= MinGap)
            return Between(before, after);

        // Hueco agotado: renumerar el proyecto conservando el orden y recalcular.
        var all = await db.Tasks.Where(t => t.ProjectId == projectId && t.Id != movingId)
            .OrderBy(t => t.Position).ToListAsync(ct);
        for (var i = 0; i < all.Count; i++) all[i].Position = (i + 1) * Step;
        var b = all.First(t => t.Id == beforeId).Position;
        var a = all.First(t => t.Id == afterId).Position;
        return Between(b, a);
    }
}

public class TaskQueries(AppDbContext db, AppClock clock)
{
    public async Task<TaskDetailDto> DetailAsync(Guid id, bool canEdit, CancellationToken ct)
    {
        var today = clock.Today;
        var t = await db.Tasks.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id, x.ProjectId, ProjectName = x.Project.Name, x.ListId, ListName = x.List.Name, x.ParentTaskId,
                ParentTitle = x.ParentTask != null ? x.ParentTask.Title : null, x.Title, x.Description, x.Status,
                x.BlockedReason, x.Priority, x.StartDate, x.DueDate, x.CompletedAt, x.AssigneeId,
                AssigneeName = x.Assignee != null ? x.Assignee.FullName : null, x.MilestoneId,
                MilestoneName = x.Milestone != null ? x.Milestone.Name : null, CreatedByName = x.CreatedBy.FullName,
                x.CreatedAt, x.UpdatedAt, x.RowVersion,
                Tags = x.Tags.Select(tt => new TagDto(tt.Tag.Id, tt.Tag.Name, tt.Tag.Color)).ToList(),
                Subtasks = x.Subtasks.Where(s => !s.IsDeleted).OrderBy(s => s.Position)
                    .Select(s => new SubtaskDto(s.Id, s.Title, s.Status, s.AssigneeId,
                        s.Assignee != null ? s.Assignee.FullName : null, s.DueDate)).ToList(),
                Values = x.CustomFieldValues.ToList(),
                Attachments = x.Attachments.Where(a => !a.IsDeleted).OrderByDescending(a => a.CreatedAt)
                    .Select(a => new AttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.UploadedBy.FullName, a.CreatedAt))
                    .ToList()
            })
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct) ?? throw AppException.NotFound("La tarea");

        var fields = await db.CustomFields.AsNoTracking().Where(f => f.ProjectId == t.ProjectId).OrderBy(f => f.Position).ToListAsync(ct);
        var custom = fields.Select(f => new CustomFieldValueDto(f.Id, f.Name, f.Type, f.Options, f.IsRequired,
            t.Values.FirstOrDefault(v => v.FieldId == f.Id)?.AsString())).ToList();

        return new TaskDetailDto(t.Id, t.ProjectId, t.ProjectName, t.ListId, t.ListName, t.ParentTaskId, t.ParentTitle,
            t.Title, t.Description, t.Status, t.BlockedReason, t.Priority, t.StartDate, t.DueDate, t.CompletedAt,
            t.AssigneeId, t.AssigneeName, t.MilestoneId, t.MilestoneName, t.CreatedByName, t.CreatedAt, t.UpdatedAt,
            t.Status != TaskItemStatus.Completada && t.DueDate < today, Convert.ToBase64String(t.RowVersion),
            t.Tags, t.Subtasks, custom, t.Attachments, canEdit);
    }
}
