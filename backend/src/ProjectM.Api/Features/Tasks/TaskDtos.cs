using FluentValidation;
using ProjectM.Domain.Tasks;

namespace ProjectM.Api.Features.Tasks;

public record TagDto(Guid Id, string Name, string Color);

/// <summary>Fila de tarea para Lista/Kanban/Calendario.</summary>
public record TaskListItemDto(
    Guid Id,
    Guid ProjectId,
    string ProjectName,
    Guid ListId,
    string ListName,
    Guid? ParentTaskId,
    string Title,
    TaskItemStatus Status,
    TaskPriority Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    Guid? AssigneeId,
    string? AssigneeName,
    Guid? MilestoneId,
    double Position,
    int SubtaskCount,
    int CompletedSubtasks,
    int AttachmentCount,
    int CommentCount,
    bool IsOverdue,
    List<TagDto> Tags);

public record SubtaskDto(Guid Id, string Title, TaskItemStatus Status, Guid? AssigneeId, string? AssigneeName, DateOnly? DueDate);
public record AttachmentDto(Guid Id, string FileName, string ContentType, long SizeBytes, string UploadedByName, DateTimeOffset CreatedAt);
public record CustomFieldValueDto(Guid FieldId, string Name, CustomFieldType Type, List<string> Options, bool IsRequired, string? Value);

public record TaskDetailDto(
    Guid Id,
    Guid ProjectId,
    string ProjectName,
    Guid ListId,
    string ListName,
    Guid? ParentTaskId,
    string? ParentTitle,
    string Title,
    string? Description,
    TaskItemStatus Status,
    string? BlockedReason,
    TaskPriority Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    DateTimeOffset? CompletedAt,
    Guid? AssigneeId,
    string? AssigneeName,
    Guid? MilestoneId,
    string? MilestoneName,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsOverdue,
    string RowVersion,
    List<TagDto> Tags,
    List<SubtaskDto> Subtasks,
    List<CustomFieldValueDto> CustomFields,
    List<AttachmentDto> Attachments,
    bool CanEdit);

public record CreateTaskRequest(
    Guid ListId,
    Guid? ParentTaskId,
    string Title,
    string? Description,
    Guid? AssigneeId,
    TaskItemStatus? Status,
    string? BlockedReason,
    TaskPriority? Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    Guid? MilestoneId,
    List<Guid>? TagIds,
    Dictionary<Guid, string?>? CustomFields);

public record UpdateTaskRequest(
    string Title,
    string? Description,
    Guid? AssigneeId,
    TaskPriority Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    Guid? MilestoneId,
    string? RowVersion);

public record ChangeStatusRequest(TaskItemStatus Status, string? BlockedReason);

/// <summary>Mueve una tarea: cambia lista y/o estado y la ubica entre dos tareas vecinas.</summary>
public record MoveTaskRequest(Guid? ListId, TaskItemStatus? Status, string? BlockedReason, Guid? BeforeId, Guid? AfterId);

public record SetTagsRequest(List<Guid> TagIds);
public record SetCustomFieldRequest(string? Value);

public class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.ListId).NotEmpty().WithMessage("Selecciona una lista.");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200).WithMessage("El título es obligatorio (máx. 200).");
        RuleFor(x => x.Description).MaximumLength(20000);
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.StartDate)
            .When(x => x.StartDate is not null && x.DueDate is not null)
            .WithMessage("La fecha de vencimiento no puede ser anterior a la de inicio.");
        RuleFor(x => x.TagIds).Must(t => t is null || t.Count <= 20).WithMessage("Máximo 20 etiquetas.");
    }
}

public class UpdateTaskRequestValidator : AbstractValidator<UpdateTaskRequest>
{
    public UpdateTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200).WithMessage("El título es obligatorio (máx. 200).");
        RuleFor(x => x.Description).MaximumLength(20000);
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.StartDate)
            .When(x => x.StartDate is not null && x.DueDate is not null)
            .WithMessage("La fecha de vencimiento no puede ser anterior a la de inicio.");
    }
}

public class ChangeStatusRequestValidator : AbstractValidator<ChangeStatusRequest>
{
    public ChangeStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.BlockedReason).MaximumLength(500);
    }
}

public class MoveTaskRequestValidator : AbstractValidator<MoveTaskRequest>
{
    public MoveTaskRequestValidator() => RuleFor(x => x.BlockedReason).MaximumLength(500);
}

public class SetTagsRequestValidator : AbstractValidator<SetTagsRequest>
{
    public SetTagsRequestValidator() =>
        RuleFor(x => x.TagIds).NotNull().Must(t => t.Count <= 20).WithMessage("Máximo 20 etiquetas.");
}
