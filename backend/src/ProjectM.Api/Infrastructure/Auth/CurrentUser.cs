using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ProjectM.Domain.Users;

namespace ProjectM.Api.Infrastructure.Auth;

public interface ICurrentUser
{
    Guid Id { get; }
    string Name { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    bool IsAdmin { get; }
    /// <summary>Rol global principal (el de mayor privilegio).</summary>
    string Role { get; }
}

public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid Id => Guid.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                                     ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id
        : Guid.Empty;

    public string Name => Principal?.FindFirstValue(JwtRegisteredClaimNames.Name) ?? "";
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;
    public bool IsAdmin => IsInRole(Roles.Admin);
    public string Role => Roles.All.FirstOrDefault(IsInRole) ?? Roles.Observador;
}
