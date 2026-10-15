using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectM.Api.Infrastructure;
using ProjectM.Api.Infrastructure.Auth;
using ProjectM.Api.Infrastructure.Authorization;
using ProjectM.Api.Infrastructure.Errors;
using ProjectM.Api.Infrastructure.Pagination;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Api.Infrastructure.Validation;
using ProjectM.Domain.Collaboration;
using ProjectM.Domain.Projects;
using ProjectM.Domain.Tasks;
using ProjectM.Domain.Users;

namespace ProjectM.Api.Features.Projects;

public static class ProjectEndpoints
{
    public static RouteGroupBuilder MapProjectEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/projects").WithTags("Proyectos");
        g.MapGet("/", List);
        g.MapPost("/", Create).RequireAuthorization(Policies.AdminOrGestor).Validate<CreateProjectRequest>();
        g.MapGet("/{id:guid}", Get);
        g.MapPut("/{id:guid}", Update).Validate<UpdateProjectRequest>();
        g.MapPost("/{id:guid}/activate", (Guid id, ProjectLifecycle l, CancellationToken ct) =>
            l.ApplyAsync(id, (p, today) => p.Activate(today), ct));
        g.MapPost("/{id:guid}/pause", (Guid id, ProjectLifecycle l, CancellationToken ct) =>
            l.ApplyAsync(id, (p, _) => p.Pause(), ct));
        g.MapPost("/{id:guid}/resume", (Guid id, ProjectLifecycle l, CancellationToken ct) =>
            l.ApplyAsync(id, (p, _) => p.Resume(), ct));
        g.MapGet("/{id:guid}/summary", Summary);

        g.MapGet("/{id:guid}/members", Members);
        g.MapPost("/{id:guid}/members", AddMember);
        g.MapDelete("/{id:guid}/members/{userId:guid}", RemoveMember);
        return api;
    }

    // ---------------- Consultas ----------------

    private static async Task<IResult> List(ProjectGuard guard, AppClock clock, Guid? spaceId, ProjectStatus? status,
        string? q, int? page, int? pageSize, CancellationToken ct)
    {
        var today = clock.Today;
        var query = guard.VisibleProjects().AsNoTracking();
        if (spaceId is not null) query = query.Where(p => p.SpaceId == spaceId);
        if (status is not null) query = query.Where(p => p.Status == status);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p => p.Name.Contains(term) || p.Code.Contains(term) ||
                                     (p.Client != null && p.Client.Name.Contains(term)));
        }

        var paged = await query
            .OrderBy(p => p.Status == ProjectStatus.Cerrado).ThenBy(p => p.PlannedEnd)
            .Select(p => new
            {
                p.Id, p.Code, p.Name, p.SpaceId, SpaceName = p.Space.Name,
                ClientName = p.Client != null ? p.Client.Name : null,
                p.Status, p.Health, p.ManagerId, ManagerName = p.Manager.FullName, p.PlannedStart, p.PlannedEnd,
                Total = p.Lists.SelectMany(l => l.Tasks).Count(),
                Completed = p.Lists.SelectMany(l => l.Tasks).Count(t => t.Status == TaskItemStatus.Completada),
                Overdue = p.Lists.SelectMany(l => l.Tasks)
                    .Count(t => t.Status != TaskItemStatus.Completada && t.DueDate < today)
            })
            .ToPagedAsync(page, pageSize, ct);

        var items = paged.Items.Select(p => new ProjectListItemDto(p.Id, p.Code, p.Name, p.SpaceId, p.SpaceName,
            p.ClientName, p.Status, p.Health, p.ManagerId, p.ManagerName, p.PlannedStart, p.PlannedEnd, p.Total,
            p.Completed, p.Overdue, new ProjectMetrics(p.Total, p.Completed).ProgressPercent)).ToList();
        return Results.Ok(new PagedResult<ProjectListItemDto>(items, paged.Page, paged.PageSize, paged.TotalCount));
    }

    private static async Task<IResult> Get(Guid id, ProjectGuard guard, ProjectQueries queries, CancellationToken ct)
    {
        var ctx = await guard.EnsureAsync(id, ProjectPermission.View, ct);
        return Results.Ok(await queries.DetailAsync(id, guard.PermissionsFor(ctx), ct));
    }

    private static async Task<IResult> Summary(Guid id, ProjectGuard guard, AppDbContext db, AppClock clock, CancellationToken ct)
    {
        await guard.EnsureAsync(id, ProjectPermission.View, ct);
        var today = clock.Today;
        var tasks = db.Tasks.AsNoTracking().Where(t => t.ProjectId == id);

        var byStatus = await tasks.GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var dict = Enum.GetValues<TaskItemStatus>().ToDictionary(s => s, s => byStatus.FirstOrDefault(x => x.Key == s)?.Count ?? 0);
        var total = dict.Values.Sum();
        var completed = dict[TaskItemStatus.Completada];
        var overdue = await tasks.CountAsync(t => t.Status != TaskItemStatus.Completada && t.DueDate < today, ct);

        var risks = await db.Risks.AsNoTracking().Where(r => r.ProjectId == id && r.Status == RiskStatus.Abierto)
            .Select(r => r.Probability * r.Impact).ToListAsync(ct);

        var milestones = await db.Milestones.AsNoTracking().Where(m => m.ProjectId == id).OrderBy(m => m.DueDate)
            .Select(m => new
            {
                m.Id, m.Name, m.DueDate, m.CompletedAt,
                Total = m.Tasks.Count(t => !t.IsDeleted),
                Completed = m.Tasks.Count(t => !t.IsDeleted && t.Status == TaskItemStatus.Completada)
            }).ToListAsync(ct);

        var plannedEnd = await db.Projects.Where(p => p.Id == id)
            .Select(p => new { p.PlannedEnd, p.Status }).FirstAsync(ct);

        return Results.Ok(new ProjectSummaryDto(
            new ProjectMetrics(total, completed).ProgressPercent, total, completed, overdue,
            dict[TaskItemStatus.Bloqueada], risks.Count, risks.Count(s => s >= 6), dict,
            plannedEnd.Status == ProjectStatus.Cerrado ? null : plannedEnd.PlannedEnd.DayNumber - today.DayNumber,
            milestones.Select(m => new MilestoneSummaryDto(m.Id, m.Name, m.DueDate, m.CompletedAt, m.Total, m.Completed,
                m.CompletedAt is null && m.DueDate < today)).ToList()));
    }

    // ---------------- Apertura ----------------

    private static async Task<IResult> Create(CreateProjectRequest req, AppDbContext db, ICurrentUser user,
        UserManager<AppUser> users, AppClock clock, ProjectGuard guard, ProjectQueries queries, CancellationToken ct)
    {
        if (!await db.Spaces.AnyAsync(s => s.Id == req.SpaceId, ct))
            throw AppException.Validation("spaceId", "El espacio no existe.");

        // Un Gestor es responsable de sus propios proyectos; el Admin puede asignar a cualquier Gestor/Admin.
        var managerId = req.ManagerId ?? user.Id;
        if (!user.IsAdmin && managerId != user.Id)
            throw AppException.Validation("managerId", "Solo un Admin puede asignar otro gestor responsable.");
        await EnsureCanManageAsync(users, managerId);

        var now = clock.Now;
        var project = new Project
        {
            SpaceId = req.SpaceId,
            Name = req.Name.Trim(),
            ClientId = await ResolveClientAsync(db, req.ClientId, req.ClientName, now, ct),
            Objective = req.Objective?.Trim(),
            Scope = req.Scope?.Trim(),
            PlannedStart = req.PlannedStart,
            PlannedEnd = req.PlannedEnd,
            Budget = req.Budget,
            Currency = string.IsNullOrWhiteSpace(req.Currency) ? "USD" : req.Currency.ToUpperInvariant(),
            ManagerId = managerId,
            TemplateId = req.TemplateId,
            CreatedAt = now,
            CreatedById = user.Id
        };

        var memberIds = (req.MemberIds ?? []).Append(managerId).Distinct().ToList();
        var validMembers = await db.Users.Where(u => memberIds.Contains(u.Id) && u.IsActive).Select(u => u.Id).ToListAsync(ct);
        if (validMembers.Count != memberIds.Count)
            throw AppException.Validation("memberIds", "Algún miembro no existe o está inactivo.");
        project.Members.AddRange(validMembers.Select(uid => new ProjectMember { ProjectId = project.Id, UserId = uid, AddedAt = now }));

        await ApplyTemplateAsync(db, project, req.TemplateId, user.Id, now, ct);
        project.Raise(new ProjectCreated(project.Id, project.Name));
        foreach (var uid in validMembers.Where(uid => uid != user.Id)) db.AddEvent(new MemberAdded(project.Id, uid));
        if (req.ActivateNow) project.Activate(clock.Today);

        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        var ctx = await guard.EnsureAsync(project.Id, ProjectPermission.View, ct);
        return Results.Created($"/api/v1/projects/{project.Id}", await queries.DetailAsync(project.Id, guard.PermissionsFor(ctx), ct));
    }

    /// <summary>Crea listas y tareas iniciales desde la plantilla (o una lista "General" si no hay plantilla).</summary>
    private static async Task ApplyTemplateAsync(AppDbContext db, Project project, Guid? templateId, Guid userId,
        DateTimeOffset now, CancellationToken ct)
    {
        var lists = new Dictionary<string, TaskList>(StringComparer.OrdinalIgnoreCase);
        TaskList ListFor(string name)
        {
            if (lists.TryGetValue(name, out var l)) return l;
            l = new TaskList { ProjectId = project.Id, Name = name, Position = lists.Count, CreatedAt = now };
            lists[name] = l;
            project.Lists.Add(l);
            return l;
        }

        if (templateId is not null)
        {
            var template = await db.ProjectTemplates.AsNoTracking().Include(t => t.Tasks)
                               .FirstOrDefaultAsync(t => t.Id == templateId, ct)
                           ?? throw AppException.Validation("templateId", "La plantilla no existe.");
            var position = 0;
            foreach (var tt in template.Tasks.OrderBy(t => t.Position))
            {
                var list = ListFor(tt.ListName);
                var start = project.PlannedStart.AddDays(tt.StartOffsetDays);
                var task = new TaskItem
                {
                    ProjectId = project.Id,
                    ListId = list.Id,
                    Title = tt.Title,
                    Description = tt.Description,
                    Priority = tt.Priority,
                    Position = ++position * 1000,
                    CreatedById = userId,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                task.SetDates(start, start.AddDays(Math.Max(tt.DurationDays, 1) - 1));
                list.Tasks.Add(task);
            }
        }
        if (lists.Count == 0) ListFor("General");
    }

    // ---------------- Edición ----------------

    private static async Task<IResult> Update(Guid id, UpdateProjectRequest req, AppDbContext db, ProjectGuard guard,
        ICurrentUser user, UserManager<AppUser> users, AppClock clock, ProjectQueries queries, CancellationToken ct)
    {
        var ctx = await guard.EnsureAsync(id, ProjectPermission.Manage, ct);
        var project = await db.Projects.Include(p => p.Members).FirstAsync(p => p.Id == id, ct);
        ConcurrencyHelper.SetOriginalRowVersion(db, project, req.RowVersion);

        if (req.ManagerId != project.ManagerId)
        {
            if (!user.IsAdmin) throw AppException.Forbidden("Solo un Admin puede cambiar el gestor responsable.");
            await EnsureCanManageAsync(users, req.ManagerId);
            if (project.Members.All(m => m.UserId != req.ManagerId))
                project.Members.Add(new ProjectMember { ProjectId = id, UserId = req.ManagerId, AddedAt = clock.Now });
            project.ManagerId = req.ManagerId;
        }
        if (req.SpaceId != project.SpaceId && !await db.Spaces.AnyAsync(s => s.Id == req.SpaceId, ct))
            throw AppException.Validation("spaceId", "El espacio no existe.");

        project.SpaceId = req.SpaceId;
        project.Name = req.Name.Trim();
        project.ClientId = await ResolveClientAsync(db, req.ClientId, req.ClientName, clock.Now, ct);
        project.Objective = req.Objective?.Trim();
        project.Scope = req.Scope?.Trim();
        project.PlannedStart = req.PlannedStart;
        project.PlannedEnd = req.PlannedEnd;
        project.Budget = req.Budget;
        project.Currency = string.IsNullOrWhiteSpace(req.Currency) ? project.Currency : req.Currency.ToUpperInvariant();
        project.Health = req.Health;
        await db.SaveChangesAsync(ct);

        ctx = await guard.EnsureAsync(id, ProjectPermission.View, ct);
        return Results.Ok(await queries.DetailAsync(id, guard.PermissionsFor(ctx), ct));
    }

    // ---------------- Equipo ----------------

    private static async Task<IResult> Members(Guid id, ProjectGuard guard, ProjectQueries queries, CancellationToken ct)
    {
        await guard.EnsureAsync(id, ProjectPermission.View, ct);
        return Results.Ok(await queries.MembersAsync(id, ct));
    }

    private static async Task<IResult> AddMember(Guid id, AddMemberRequest req, AppDbContext db, ProjectGuard guard,
        AppClock clock, ProjectQueries queries, CancellationToken ct)
    {
        await guard.EnsureAsync(id, ProjectPermission.Manage, ct);
        if (!await db.Users.AnyAsync(u => u.Id == req.UserId && u.IsActive, ct))
            throw AppException.Validation("userId", "El usuario no existe o está inactivo.");
        if (!await db.ProjectMembers.AnyAsync(m => m.ProjectId == id && m.UserId == req.UserId, ct))
        {
            db.ProjectMembers.Add(new ProjectMember { ProjectId = id, UserId = req.UserId, AddedAt = clock.Now });
            db.AddEvent(new MemberAdded(id, req.UserId));
            await db.SaveChangesAsync(ct);
        }
        return Results.Ok(await queries.MembersAsync(id, ct));
    }

    private static async Task<IResult> RemoveMember(Guid id, Guid userId, AppDbContext db, ProjectGuard guard,
        ProjectQueries queries, CancellationToken ct)
    {
        var ctx = await guard.EnsureAsync(id, ProjectPermission.Manage, ct);
        if (ctx.ManagerId == userId)
            throw AppException.Conflict("GESTOR_REQUERIDO", "No se puede quitar al gestor responsable del equipo.");
        await db.ProjectMembers.Where(m => m.ProjectId == id && m.UserId == userId).ExecuteDeleteAsync(ct);
        return Results.Ok(await queries.MembersAsync(id, ct));
    }

    // ---------------- Auxiliares ----------------

    private static async Task EnsureCanManageAsync(UserManager<AppUser> users, Guid managerId)
    {
        var manager = await users.FindByIdAsync(managerId.ToString());
        if (manager is null || !manager.IsActive)
            throw AppException.Validation("managerId", "El gestor responsable no existe o está inactivo.");
        var roles = await users.GetRolesAsync(manager);
        if (!roles.Contains(Roles.Gestor) && !roles.Contains(Roles.Admin))
            throw AppException.Validation("managerId", "El responsable debe tener rol Gestor o Admin.");
    }

    private static async Task<Guid?> ResolveClientAsync(AppDbContext db, Guid? clientId, string? clientName,
        DateTimeOffset now, CancellationToken ct)
    {
        if (clientId is not null)
        {
            if (!await db.Clients.AnyAsync(c => c.Id == clientId, ct))
                throw AppException.Validation("clientId", "El cliente no existe.");
            return clientId;
        }
        if (string.IsNullOrWhiteSpace(clientName)) return null;
        var name = clientName.Trim();
        var existing = await db.Clients.FirstOrDefaultAsync(c => c.Name == name, ct);
        if (existing is not null) return existing.Id;
        var client = new Client { Name = name, CreatedAt = now };
        db.Clients.Add(client);
        return client.Id;
    }
}

/// <summary>Aplica transiciones de estado del proyecto con autorización.</summary>
public class ProjectLifecycle(AppDbContext db, ProjectGuard guard, AppClock clock, ProjectQueries queries)
{
    public async Task<IResult> ApplyAsync(Guid id, Action<Project, DateOnly> transition, CancellationToken ct)
    {
        await guard.EnsureAsync(id, ProjectPermission.Manage, ct);
        var project = await db.Projects.FirstAsync(p => p.Id == id, ct);
        transition(project, clock.Today);
        await db.SaveChangesAsync(ct);
        var ctx = await guard.EnsureAsync(id, ProjectPermission.View, ct);
        return Results.Ok(await queries.DetailAsync(id, guard.PermissionsFor(ctx), ct));
    }
}

public class ProjectQueries(AppDbContext db)
{
    public async Task<ProjectDetailDto> DetailAsync(Guid id, ProjectPermissionsDto permissions, CancellationToken ct)
    {
        var p = await db.Projects.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id, x.Code, x.Name, x.SpaceId, SpaceName = x.Space.Name, x.ClientId,
                ClientName = x.Client != null ? x.Client.Name : null, x.Objective, x.Scope, x.PlannedStart, x.PlannedEnd,
                x.ActualStart, x.ActualEnd, x.Budget, x.Currency, x.ManagerId, ManagerName = x.Manager.FullName,
                x.Status, x.Health, x.CreatedAt, x.ClosedAt, x.RowVersion,
                Lists = x.Lists.OrderBy(l => l.Position)
                    .Select(l => new ListDto(l.Id, l.Name, l.Position, l.Tasks.Count(t => !t.IsDeleted))).ToList()
            }).FirstOrDefaultAsync(ct) ?? throw AppException.NotFound("El proyecto");

        return new ProjectDetailDto(p.Id, p.Code, p.Name, p.SpaceId, p.SpaceName, p.ClientId, p.ClientName, p.Objective,
            p.Scope, p.PlannedStart, p.PlannedEnd, p.ActualStart, p.ActualEnd, p.Budget, p.Currency, p.ManagerId,
            p.ManagerName, p.Status, p.Health, p.CreatedAt, p.ClosedAt, Convert.ToBase64String(p.RowVersion),
            await MembersAsync(id, ct), p.Lists, permissions);
    }

    public async Task<List<MemberDto>> MembersAsync(Guid projectId, CancellationToken ct)
    {
        var managerId = await db.Projects.Where(p => p.Id == projectId).Select(p => p.ManagerId).FirstAsync(ct);
        var members = await db.ProjectMembers.AsNoTracking().Where(m => m.ProjectId == projectId)
            .Select(m => new
            {
                m.UserId, m.User.FullName, m.User.Email,
                Roles = db.UserRoles.Where(ur => ur.UserId == m.UserId)
                    .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!).ToList()
            })
            .OrderBy(m => m.FullName).ToListAsync(ct);
        return members.Select(m => new MemberDto(m.UserId, m.FullName, m.Email ?? "",
            Roles.All.FirstOrDefault(m.Roles.Contains) ?? Roles.Observador, m.UserId == managerId)).ToList();
    }
}

public static class ConcurrencyHelper
{
    /// <summary>Usa la RowVersion enviada por el cliente para detectar ediciones concurrentes (409).</summary>
    public static void SetOriginalRowVersion<T>(AppDbContext db, T entity, string? rowVersion) where T : class
    {
        if (string.IsNullOrEmpty(rowVersion)) return;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(rowVersion); }
        catch (FormatException) { throw AppException.Validation("rowVersion", "Versión no válida."); }
        db.Entry(entity).Property("RowVersion").OriginalValue = bytes;
    }
}
