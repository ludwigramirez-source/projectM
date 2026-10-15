using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ProjectM.Api.Infrastructure.Auth;
using ProjectM.Api.Infrastructure.Errors;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Domain.Projects;
using ProjectM.Domain.Users;

namespace ProjectM.Api.Infrastructure.Authorization;

public enum ProjectPermission
{
    /// <summary>Ver el proyecto y su contenido.</summary>
    View,
    /// <summary>Crear/editar/mover tareas, comentar y adjuntar.</summary>
    EditTasks,
    /// <summary>Editar el proyecto, equipo, listas, campos, hitos y riesgos.</summary>
    Manage,
    /// <summary>Iniciar y ejecutar el cierre (incluido el forzado).</summary>
    Close,
    /// <summary>Reabrir un proyecto cerrado.</summary>
    Reopen
}

/// <summary>Datos mínimos del proyecto necesarios para decidir permisos.</summary>
public record ProjectAuthContext(Guid ProjectId, ProjectStatus Status, Guid ManagerId, bool IsMember);

public record ProjectPermissionsDto(bool CanEditTasks, bool CanManage, bool CanClose, bool CanReopen);

public class ProjectRequirement(ProjectPermission permission) : IAuthorizationRequirement
{
    public ProjectPermission Permission { get; } = permission;
}

/// <summary>
/// Reglas de la matriz de permisos (PLAN.md §2). Funciones puras para poder probarlas sin infraestructura.
/// </summary>
public static class ProjectRules
{
    public static bool Allows(ProjectAuthContext p, Guid userId, string role, ProjectPermission permission)
    {
        var isAdmin = role == Roles.Admin;
        var isManager = p.ManagerId == userId;
        var participates = p.IsMember || isManager;

        return permission switch
        {
            ProjectPermission.View => isAdmin || participates,
            ProjectPermission.EditTasks => isAdmin || isManager ||
                                           (p.IsMember && role is Roles.Gestor or Roles.Miembro),
            ProjectPermission.Manage or ProjectPermission.Close => isAdmin || (isManager && role is Roles.Gestor),
            ProjectPermission.Reopen => isAdmin,
            _ => false
        };
    }

    public static bool IsWrite(ProjectPermission p) => p is not (ProjectPermission.View or ProjectPermission.Reopen);
}

public class ProjectAuthorizationHandler : AuthorizationHandler<ProjectRequirement, ProjectAuthContext>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ProjectRequirement requirement,
        ProjectAuthContext resource)
    {
        var sub = context.User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId)) return Task.CompletedTask;
        var role = Roles.All.FirstOrDefault(context.User.IsInRole) ?? Roles.Observador;
        if (ProjectRules.Allows(resource, userId, role, requirement.Permission)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Punto único de autorización por proyecto. Todo endpoint que toca un proyecto o sus hijos
/// debe llamar a <see cref="EnsureAsync"/> (o a sus variantes por tarea/lista).
/// </summary>
public class ProjectGuard(AppDbContext db, ICurrentUser user, IAuthorizationService authorization, IHttpContextAccessor http)
{
    /// <summary>Proyectos visibles para el usuario actual.</summary>
    public IQueryable<Project> VisibleProjects()
    {
        var q = db.Projects.AsQueryable();
        if (user.IsAdmin) return q;
        var id = user.Id;
        return q.Where(p => p.ManagerId == id || p.Members.Any(m => m.UserId == id));
    }

    public async Task<ProjectAuthContext> EnsureAsync(Guid projectId, ProjectPermission permission, CancellationToken ct)
    {
        var uid = user.Id;
        var ctx = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new ProjectAuthContext(p.Id, p.Status, p.ManagerId, p.Members.Any(m => m.UserId == uid)))
            .FirstOrDefaultAsync(ct);
        return await CheckAsync(ctx, permission);
    }

    /// <summary>Autoriza a partir de una tarea (resuelve su proyecto).</summary>
    public async Task<ProjectAuthContext> EnsureForTaskAsync(Guid taskId, ProjectPermission permission, CancellationToken ct)
    {
        var projectId = await db.Tasks.Where(t => t.Id == taskId).Select(t => (Guid?)t.ProjectId).FirstOrDefaultAsync(ct)
                        ?? throw AppException.NotFound("La tarea");
        return await EnsureAsync(projectId, permission, ct);
    }

    public ProjectPermissionsDto PermissionsFor(ProjectAuthContext ctx)
    {
        bool Can(ProjectPermission p) =>
            ProjectRules.Allows(ctx, user.Id, user.Role, p) &&
            !(ctx.Status == ProjectStatus.Cerrado && ProjectRules.IsWrite(p));
        return new ProjectPermissionsDto(
            Can(ProjectPermission.EditTasks),
            Can(ProjectPermission.Manage),
            Can(ProjectPermission.Close),
            ctx.Status == ProjectStatus.Cerrado && Can(ProjectPermission.Reopen));
    }

    private async Task<ProjectAuthContext> CheckAsync(ProjectAuthContext? ctx, ProjectPermission permission)
    {
        var principal = http.HttpContext!.User;
        // No revelar la existencia de proyectos ajenos: 404 si no puede verlo.
        if (ctx is null || !(await authorization.AuthorizeAsync(principal, ctx, new ProjectRequirement(ProjectPermission.View))).Succeeded)
            throw AppException.NotFound("El proyecto");

        if (ctx.Status == ProjectStatus.Cerrado && ProjectRules.IsWrite(permission))
            throw AppException.Conflict(ErrorCodes.ProjectClosed,
                "El proyecto está cerrado y es de solo lectura. Un Admin puede reabrirlo.");

        if (!(await authorization.AuthorizeAsync(principal, ctx, new ProjectRequirement(permission))).Succeeded)
            throw AppException.Forbidden();
        return ctx;
    }
}
