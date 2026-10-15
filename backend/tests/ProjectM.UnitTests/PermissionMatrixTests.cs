using FluentAssertions;
using ProjectM.Api.Infrastructure.Authorization;
using ProjectM.Domain.Projects;
using ProjectM.Domain.Users;

namespace ProjectM.UnitTests;

/// <summary>Matriz de permisos por proyecto (PLAN.md §2).</summary>
public class PermissionMatrixTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static ProjectAuthContext Ctx(bool member, bool manager = false) =>
        new(Guid.NewGuid(), ProjectStatus.Activo, manager ? Me : Other, member);

    public static TheoryData<string, bool, bool, ProjectPermission, bool> Cases => new()
    {
        // rol, miembro, gestor del proyecto, permiso, esperado
        { Roles.Admin, false, false, ProjectPermission.View, true },
        { Roles.Admin, false, false, ProjectPermission.Manage, true },
        { Roles.Admin, false, false, ProjectPermission.Reopen, true },
        { Roles.Gestor, true, true, ProjectPermission.Manage, true },
        { Roles.Gestor, true, true, ProjectPermission.Close, true },
        { Roles.Gestor, true, true, ProjectPermission.Reopen, false },
        { Roles.Gestor, true, false, ProjectPermission.View, true },
        { Roles.Gestor, true, false, ProjectPermission.EditTasks, true },
        { Roles.Gestor, true, false, ProjectPermission.Manage, false },
        { Roles.Gestor, false, false, ProjectPermission.View, false },
        { Roles.Miembro, true, false, ProjectPermission.EditTasks, true },
        { Roles.Miembro, true, false, ProjectPermission.Manage, false },
        { Roles.Miembro, true, false, ProjectPermission.Close, false },
        { Roles.Miembro, false, false, ProjectPermission.View, false },
        { Roles.Observador, true, false, ProjectPermission.View, true },
        { Roles.Observador, true, false, ProjectPermission.EditTasks, false },
        { Roles.Observador, false, false, ProjectPermission.View, false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Matriz(string role, bool member, bool manager, ProjectPermission permission, bool expected) =>
        ProjectRules.Allows(Ctx(member, manager), Me, role, permission).Should().Be(expected);

    [Fact]
    public void Solo_lectura_y_reapertura_no_son_escrituras()
    {
        ProjectRules.IsWrite(ProjectPermission.View).Should().BeFalse();
        ProjectRules.IsWrite(ProjectPermission.Reopen).Should().BeFalse();
        ProjectRules.IsWrite(ProjectPermission.EditTasks).Should().BeTrue();
        ProjectRules.IsWrite(ProjectPermission.Close).Should().BeTrue();
    }
}
