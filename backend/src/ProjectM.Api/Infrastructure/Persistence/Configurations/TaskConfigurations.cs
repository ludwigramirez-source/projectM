using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectM.Domain.Projects;
using ProjectM.Domain.Tasks;

namespace ProjectM.Api.Infrastructure.Persistence.Configurations;

public class TaskListConfiguration : IEntityTypeConfiguration<TaskList>
{
    public void Configure(EntityTypeBuilder<TaskList> e)
    {
        e.ToTable("TaskLists");
        e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        e.HasOne(x => x.Project).WithMany(p => p.Lists).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.ProjectId, x.Position });
    }
}

public class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> e)
    {
        e.ToTable("Tasks");
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.Description).HasColumnType("nvarchar(max)");
        e.Property(x => x.BlockedReason).HasMaxLength(500);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.RowVersion).IsRowVersion();
        e.HasQueryFilter(x => !x.IsDeleted);

        // Project → Lists → Tasks hace cascada; el resto NoAction para evitar rutas múltiples en SQL Server.
        e.HasOne(x => x.List).WithMany(l => l.Tasks).HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.ParentTask).WithMany(x => x.Subtasks).HasForeignKey(x => x.ParentTaskId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.Milestone).WithMany(m => m.Tasks).HasForeignKey(x => x.MilestoneId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.Assignee).WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);

        e.HasIndex(x => new { x.ProjectId, x.Status });
        e.HasIndex(x => new { x.ListId, x.Position });
        e.HasIndex(x => new { x.AssigneeId, x.Status, x.DueDate });
        e.HasIndex(x => x.ParentTaskId);
        e.HasIndex(x => x.MilestoneId);
        e.HasIndex(x => x.DueDate).HasFilter("[CompletedAt] IS NULL AND [IsDeleted] = 0");
    }
}

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> e)
    {
        e.ToTable("Tags");
        e.Property(x => x.Name).HasMaxLength(50).IsRequired();
        e.Property(x => x.Color).HasMaxLength(20);
        e.HasIndex(x => x.Name).IsUnique();
    }
}

public class TaskTagConfiguration : IEntityTypeConfiguration<TaskTag>
{
    public void Configure(EntityTypeBuilder<TaskTag> e)
    {
        e.ToTable("TaskTags");
        e.HasKey(x => new { x.TaskId, x.TagId });
        e.HasOne<TaskItem>().WithMany(t => t.Tags).HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Tag).WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.TagId);
    }
}

public class CustomFieldDefinitionConfiguration : IEntityTypeConfiguration<CustomFieldDefinition>
{
    public void Configure(EntityTypeBuilder<CustomFieldDefinition> e)
    {
        e.ToTable("CustomFieldDefinitions");
        e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        e.PrimitiveCollection(x => x.Options).HasColumnType("nvarchar(max)");
        e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.ProjectId, x.Position });
    }
}

public class CustomFieldValueConfiguration : IEntityTypeConfiguration<CustomFieldValue>
{
    public void Configure(EntityTypeBuilder<CustomFieldValue> e)
    {
        e.ToTable("CustomFieldValues");
        e.HasKey(x => new { x.TaskId, x.FieldId });
        e.Property(x => x.TextValue).HasMaxLength(1000);
        e.Property(x => x.NumberValue).HasPrecision(18, 4);
        e.HasOne<TaskItem>().WithMany(t => t.CustomFieldValues).HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Field).WithMany().HasForeignKey(x => x.FieldId).OnDelete(DeleteBehavior.NoAction);
        e.HasIndex(x => x.FieldId);
    }
}

public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> e)
    {
        e.ToTable("Attachments");
        e.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        e.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
        e.Property(x => x.StorageKey).HasMaxLength(100).IsRequired();
        e.HasQueryFilter(x => !x.IsDeleted);
        e.HasOne<TaskItem>().WithMany(t => t.Attachments).HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.UploadedBy).WithMany().HasForeignKey(x => x.UploadedById).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.TaskId);
    }
}
