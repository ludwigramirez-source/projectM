using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProjectM.Api.Infrastructure.Authorization;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Domain.Users;

namespace ProjectM.Api.Infrastructure.Realtime;

/// <summary>
/// Hub de tiempo real. Grupos: <c>user:{id}</c> (notificaciones personales) y
/// <c>project:{id}</c> (cambios del proyecto abierto). Eventos: notification, projectChanged.
/// </summary>
[Authorize]
public class NotificationsHub(AppDbContext db) : Hub
{
    public static string UserGroup(Guid userId) => $"user:{userId}";
    public static string ProjectGroup(Guid projectId) => $"project:{projectId}";

    private Guid UserId => Guid.TryParse(Context.User?.FindFirstValue("sub"), out var id) ? id : Guid.Empty;

    public override async Task OnConnectedAsync()
    {
        if (UserId != Guid.Empty) await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(UserId));
        await base.OnConnectedAsync();
    }

    /// <summary>Suscribe la conexión a los cambios de un proyecto que el usuario puede ver.</summary>
    public async Task JoinProject(Guid projectId)
    {
        var uid = UserId;
        var ctx = await db.Projects.AsNoTracking().Where(p => p.Id == projectId)
            .Select(p => new ProjectAuthContext(p.Id, p.Status, p.ManagerId, p.Members.Any(m => m.UserId == uid)))
            .FirstOrDefaultAsync(Context.ConnectionAborted);
        var role = Roles.All.FirstOrDefault(r => Context.User?.IsInRole(r) == true) ?? Roles.Observador;
        if (ctx is null || !ProjectRules.Allows(ctx, uid, role, ProjectPermission.View))
            throw new HubException("No tienes acceso a este proyecto.");
        await Groups.AddToGroupAsync(Context.ConnectionId, ProjectGroup(projectId));
    }

    public Task LeaveProject(Guid projectId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, ProjectGroup(projectId));
}
