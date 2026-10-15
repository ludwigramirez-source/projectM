using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ProjectM.Api.Infrastructure;
using ProjectM.Api.Infrastructure.Auth;
using ProjectM.Api.Infrastructure.Authorization;
using ProjectM.Api.Infrastructure.Errors;
using ProjectM.Api.Infrastructure.Pagination;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Api.Infrastructure.Validation;
using ProjectM.Domain.Collaboration;

namespace ProjectM.Api.Features.Collaboration;

public record CommentDto(Guid Id, Guid TaskId, Guid AuthorId, string AuthorName, string Body, List<Guid> Mentions,
    DateTimeOffset CreatedAt, DateTimeOffset? EditedAt, bool CanEdit);
public record SaveCommentRequest(string Body, List<Guid>? MentionIds);
public record ActivityDto(Guid Id, Guid ProjectId, Guid? TaskId, Guid? ActorId, string ActorName, string Type, string Message,
    string? Data, DateTimeOffset CreatedAt);
public record NotificationDto(Guid Id, string Type, string Title, string? Body, string? Link, Guid? ProjectId, Guid? TaskId,
    DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public class SaveCommentRequestValidator : AbstractValidator<SaveCommentRequest>
{
    public SaveCommentRequestValidator()
    {
        RuleFor(x => x.Body).NotEmpty().WithMessage("El comentario no puede estar vacío.")
            .MaximumLength(5000).WithMessage("El comentario admite máximo 5000 caracteres.");
        RuleFor(x => x.MentionIds).Must(m => m is null || m.Count <= 20).WithMessage("Máximo 20 menciones.");
    }
}

public static class CollaborationEndpoints
{
    public static RouteGroupBuilder MapCollaborationEndpoints(this RouteGroupBuilder api)
    {
        var c = api.MapGroup("").WithTags("Comentarios");
        c.MapGet("/tasks/{taskId:guid}/comments", ListComments);
        c.MapPost("/tasks/{taskId:guid}/comments", AddComment).Validate<SaveCommentRequest>();
        c.MapPut("/comments/{id:guid}", EditComment).Validate<SaveCommentRequest>();
        c.MapDelete("/comments/{id:guid}", DeleteComment);

        var a = api.MapGroup("").WithTags("Actividad");
        a.MapGet("/projects/{projectId:guid}/activity", ProjectActivity);
        a.MapGet("/tasks/{taskId:guid}/activity", TaskActivity);

        var n = api.MapGroup("/notifications").WithTags("Notificaciones");
        n.MapGet("/", ListNotifications);
        n.MapGet("/unread-count", UnreadCount);
        n.MapPost("/{id:guid}/read", MarkRead);
        n.MapPost("/read-all", MarkAllRead);
        return api;
    }

    // ---------------- Comentarios ----------------

    private static IQueryable<CommentDto> CommentQuery(IQueryable<Comment> source, Guid userId, bool canEdit) =>
        source.AsNoTracking().Select(c => new CommentDto(c.Id, c.TaskId, c.AuthorId, c.Author.FullName, c.Body,
            c.Mentions, c.CreatedAt, c.EditedAt, canEdit && c.AuthorId == userId));

    private static async Task<IResult> ListComments(Guid taskId, ProjectGuard guard, AppDbContext db, ICurrentUser user,
        int? page, int? pageSize, CancellationToken ct)
    {
        var ctx = await guard.EnsureForTaskAsync(taskId, ProjectPermission.View, ct);
        var canEdit = guard.PermissionsFor(ctx).CanEditTasks;
        var (p, s) = Paging.Normalize(page, pageSize ?? 50);
        var query = db.Comments.AsNoTracking().Where(c => c.TaskId == taskId);
        var total = await query.CountAsync(ct);
        // Los más recientes primero en la página; el cliente los muestra en orden cronológico.
        var ids = await query.OrderByDescending(c => c.CreatedAt).Skip((p - 1) * s).Take(s).Select(c => c.Id).ToListAsync(ct);
        var items = await CommentQuery(db.Comments.Where(c => ids.Contains(c.Id)).OrderBy(c => c.CreatedAt), user.Id, canEdit).ToListAsync(ct);
        return Results.Ok(new PagedResult<CommentDto>(items, p, s, total));
    }

    private static async Task<IResult> AddComment(Guid taskId, SaveCommentRequest req, ProjectGuard guard, AppDbContext db,
        ICurrentUser user, AppClock clock, CancellationToken ct)
    {
        var ctx = await guard.EnsureForTaskAsync(taskId, ProjectPermission.EditTasks, ct);
        var mentions = await ValidMentionsAsync(db, ctx.ProjectId, req.MentionIds, ct);
        var comment = new Comment
        {
            TaskId = taskId,
            AuthorId = user.Id,
            Body = req.Body.Trim(),
            Mentions = mentions,
            CreatedAt = clock.Now
        };
        comment.Raise(new CommentAdded(comment.Id, taskId, ctx.ProjectId, mentions, Excerpt(comment.Body)));
        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/comments/{comment.Id}",
            await CommentQuery(db.Comments.Where(c => c.Id == comment.Id), user.Id, true).FirstAsync(ct));
    }

    private static async Task<IResult> EditComment(Guid id, SaveCommentRequest req, ProjectGuard guard, AppDbContext db,
        ICurrentUser user, AppClock clock, CancellationToken ct)
    {
        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw AppException.NotFound("El comentario");
        var ctx = await guard.EnsureForTaskAsync(comment.TaskId, ProjectPermission.EditTasks, ct);
        if (comment.AuthorId != user.Id) throw AppException.Forbidden("Solo el autor puede editar el comentario.");
        comment.Body = req.Body.Trim();
        comment.Mentions = await ValidMentionsAsync(db, ctx.ProjectId, req.MentionIds, ct);
        comment.EditedAt = clock.Now;
        await db.SaveChangesAsync(ct);
        return Results.Ok(await CommentQuery(db.Comments.Where(c => c.Id == id), user.Id, true).FirstAsync(ct));
    }

    private static async Task<IResult> DeleteComment(Guid id, ProjectGuard guard, AppDbContext db, ICurrentUser user, CancellationToken ct)
    {
        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw AppException.NotFound("El comentario");
        var ctx = await guard.EnsureForTaskAsync(comment.TaskId, ProjectPermission.EditTasks, ct);
        if (comment.AuthorId != user.Id && !guard.PermissionsFor(ctx).CanManage)
            throw AppException.Forbidden("Solo el autor o el gestor pueden eliminar el comentario.");
        comment.IsDeleted = true;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<List<Guid>> ValidMentionsAsync(AppDbContext db, Guid projectId, List<Guid>? ids, CancellationToken ct)
    {
        if (ids is null || ids.Count == 0) return [];
        var distinct = ids.Distinct().ToList();
        return await db.ProjectMembers.Where(m => m.ProjectId == projectId && distinct.Contains(m.UserId))
            .Select(m => m.UserId).ToListAsync(ct);
    }

    private static string Excerpt(string body) => body.Length <= 140 ? body : body[..137] + "…";

    // ---------------- Actividad ----------------

    private static IQueryable<ActivityDto> ActivityQuery(IQueryable<ActivityLog> q) =>
        q.AsNoTracking().OrderByDescending(a => a.CreatedAt)
            .Select(a => new ActivityDto(a.Id, a.ProjectId, a.TaskId, a.ActorId, a.Actor != null ? a.Actor.FullName : "Sistema",
                a.Type, a.Message, a.Data, a.CreatedAt));

    private static async Task<IResult> ProjectActivity(Guid projectId, ProjectGuard guard, AppDbContext db, int? page, int? pageSize, CancellationToken ct)
    {
        await guard.EnsureAsync(projectId, ProjectPermission.View, ct);
        return Results.Ok(await ActivityQuery(db.ActivityLogs.Where(a => a.ProjectId == projectId)).ToPagedAsync(page, pageSize, ct));
    }

    private static async Task<IResult> TaskActivity(Guid taskId, ProjectGuard guard, AppDbContext db, CancellationToken ct)
    {
        await guard.EnsureForTaskAsync(taskId, ProjectPermission.View, ct);
        return Results.Ok(await ActivityQuery(db.ActivityLogs.Where(a => a.TaskId == taskId)).Take(100).ToListAsync(ct));
    }

    // ---------------- Notificaciones (solo las propias) ----------------

    private static async Task<IResult> ListNotifications(AppDbContext db, ICurrentUser user, bool? unreadOnly, int? page, int? pageSize, CancellationToken ct)
    {
        var q = db.Notifications.AsNoTracking().Where(n => n.UserId == user.Id);
        if (unreadOnly == true) q = q.Where(n => n.ReadAt == null);
        return Results.Ok(await q.OrderByDescending(n => n.CreatedAt)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.Link, n.ProjectId, n.TaskId, n.CreatedAt, n.ReadAt))
            .ToPagedAsync(page, pageSize ?? 20, ct));
    }

    private static async Task<IResult> UnreadCount(AppDbContext db, ICurrentUser user, CancellationToken ct) =>
        Results.Ok(new { count = await db.Notifications.CountAsync(n => n.UserId == user.Id && n.ReadAt == null, ct) });

    private static async Task<IResult> MarkRead(Guid id, AppDbContext db, ICurrentUser user, AppClock clock, CancellationToken ct)
    {
        var now = clock.Now;
        var updated = await db.Notifications.Where(n => n.Id == id && n.UserId == user.Id && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
        if (updated == 0 && !await db.Notifications.AnyAsync(n => n.Id == id && n.UserId == user.Id, ct))
            throw AppException.NotFound("La notificación");
        return Results.NoContent();
    }

    private static async Task<IResult> MarkAllRead(AppDbContext db, ICurrentUser user, AppClock clock, CancellationToken ct)
    {
        var now = clock.Now;
        await db.Notifications.Where(n => n.UserId == user.Id && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
        return Results.NoContent();
    }
}
