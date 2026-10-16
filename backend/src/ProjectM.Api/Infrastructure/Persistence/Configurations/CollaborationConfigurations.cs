using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectM.Domain.Collaboration;
using ProjectM.Domain.Projects;
using ProjectM.Domain.Tasks;
using ProjectM.Domain.Users;

namespace ProjectM.Api.Infrastructure.Persistence.Configurations;

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> e)
    {
        e.ToTable("Comments");
        e.Property(x => x.Body).HasMaxLength(5000).IsRequired();
        e.PrimitiveCollection(x => x.Mentions).HasColumnType("uuid[]");
        e.HasQueryFilter(x => !x.IsDeleted);
        e.HasOne(x => x.Task).WithMany(t => t.Comments).HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.TaskId, x.CreatedAt });
    }
}

public class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> e)
    {
        e.ToTable("ActivityLogs");
        e.Property(x => x.Type).HasMaxLength(50).IsRequired();
        e.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        e.Property(x => x.Data).HasColumnType("jsonb");
        e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        // Sin FK a la tarea: el historial sobrevive a la eliminación de la tarea.
        e.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.ProjectId, x.CreatedAt }).IsDescending(false, true);
        e.HasIndex(x => new { x.TaskId, x.CreatedAt });
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> e)
    {
        e.ToTable("Notifications");
        e.Property(x => x.Type).HasMaxLength(50).IsRequired();
        e.Property(x => x.Title).HasMaxLength(300).IsRequired();
        e.Property(x => x.Body).HasMaxLength(1000);
        e.Property(x => x.Link).HasMaxLength(300);
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.UserId, x.ReadAt, x.CreatedAt }).IsDescending(false, false, true);
        e.HasIndex(x => new { x.TaskId, x.Type });
    }
}
