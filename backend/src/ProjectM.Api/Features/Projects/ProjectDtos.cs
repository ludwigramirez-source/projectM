using FluentValidation;
using ProjectM.Api.Infrastructure.Authorization;
using ProjectM.Domain.Projects;
using ProjectM.Domain.Tasks;

namespace ProjectM.Api.Features.Projects;

public record ProjectListItemDto(
    Guid Id,
    string Code,
    string Name,
    Guid SpaceId,
    string SpaceName,
    string? ClientName,
    ProjectStatus Status,
    ProjectHealth Health,
    Guid ManagerId,
    string ManagerName,
    DateOnly PlannedStart,
    DateOnly PlannedEnd,
    int TotalTasks,
    int CompletedTasks,
    int OverdueTasks,
    int Progress);

public record MemberDto(Guid UserId, string FullName, string Email, string Role, bool IsManager);
public record ListDto(Guid Id, string Name, int Position, int TaskCount);

public record ProjectDetailDto(
    Guid Id,
    string Code,
    string Name,
    Guid SpaceId,
    string SpaceName,
    Guid? ClientId,
    string? ClientName,
    string? Objective,
    string? Scope,
    DateOnly PlannedStart,
    DateOnly PlannedEnd,
    DateOnly? ActualStart,
    DateOnly? ActualEnd,
    decimal? Budget,
    string Currency,
    Guid ManagerId,
    string ManagerName,
    ProjectStatus Status,
    ProjectHealth Health,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    string RowVersion,
    List<MemberDto> Members,
    List<ListDto> Lists,
    ProjectPermissionsDto Permissions);

public record CreateProjectRequest(
    Guid SpaceId,
    string Name,
    Guid? ClientId,
    string? ClientName,
    string? Objective,
    string? Scope,
    DateOnly PlannedStart,
    DateOnly PlannedEnd,
    decimal? Budget,
    string? Currency,
    Guid? ManagerId,
    List<Guid>? MemberIds,
    Guid? TemplateId,
    bool ActivateNow);

public record UpdateProjectRequest(
    Guid SpaceId,
    string Name,
    Guid? ClientId,
    string? ClientName,
    string? Objective,
    string? Scope,
    DateOnly PlannedStart,
    DateOnly PlannedEnd,
    decimal? Budget,
    string? Currency,
    Guid ManagerId,
    ProjectHealth Health,
    string? RowVersion);

public record AddMemberRequest(Guid UserId);

public record MilestoneSummaryDto(Guid Id, string Name, DateOnly DueDate, DateTimeOffset? CompletedAt, int TotalTasks, int CompletedTasks, bool IsOverdue);

public record ProjectSummaryDto(
    int Progress,
    int TotalTasks,
    int CompletedTasks,
    int OverdueTasks,
    int BlockedTasks,
    int OpenRisks,
    int HighRisks,
    Dictionary<TaskItemStatus, int> TasksByStatus,
    int? DaysRemaining,
    List<MilestoneSummaryDto> Milestones);

public class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.SpaceId).NotEmpty().WithMessage("Selecciona un espacio.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150).WithMessage("El nombre es obligatorio (máx. 150).");
        RuleFor(x => x.ClientName).MaximumLength(150);
        RuleFor(x => x.Objective).MaximumLength(2000);
        RuleFor(x => x.Scope).MaximumLength(4000);
        RuleFor(x => x.PlannedEnd).GreaterThanOrEqualTo(x => x.PlannedStart)
            .WithMessage("La fecha de fin no puede ser anterior a la de inicio.");
        RuleFor(x => x.Budget).GreaterThanOrEqualTo(0).When(x => x.Budget is not null)
            .WithMessage("El presupuesto no puede ser negativo.");
        RuleFor(x => x.Currency).Length(3).When(x => !string.IsNullOrEmpty(x.Currency))
            .WithMessage("La moneda debe ser un código ISO de 3 letras.");
        RuleFor(x => x.MemberIds).Must(m => m is null || m.Count <= 100).WithMessage("Máximo 100 miembros.");
    }
}

public class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(x => x.SpaceId).NotEmpty().WithMessage("Selecciona un espacio.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150).WithMessage("El nombre es obligatorio (máx. 150).");
        RuleFor(x => x.ClientName).MaximumLength(150);
        RuleFor(x => x.Objective).MaximumLength(2000);
        RuleFor(x => x.Scope).MaximumLength(4000);
        RuleFor(x => x.PlannedEnd).GreaterThanOrEqualTo(x => x.PlannedStart)
            .WithMessage("La fecha de fin no puede ser anterior a la de inicio.");
        RuleFor(x => x.Budget).GreaterThanOrEqualTo(0).When(x => x.Budget is not null)
            .WithMessage("El presupuesto no puede ser negativo.");
        RuleFor(x => x.Currency).Length(3).When(x => !string.IsNullOrEmpty(x.Currency))
            .WithMessage("La moneda debe ser un código ISO de 3 letras.");
        RuleFor(x => x.ManagerId).NotEmpty().WithMessage("Selecciona un gestor responsable.");
    }
}
