using ProjectM.Domain.Users;

namespace ProjectM.Api.Infrastructure.Auth;

public static class Policies
{
    public const string Admin = "Admin";
    public const string AdminOrGestor = "AdminOrGestor";

    public static void AddPolicies(Microsoft.AspNetCore.Authorization.AuthorizationOptions o)
    {
        o.AddPolicy(Admin, p => p.RequireRole(Roles.Admin));
        o.AddPolicy(AdminOrGestor, p => p.RequireRole(Roles.Admin, Roles.Gestor));
    }
}
