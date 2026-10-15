using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectM.Domain.Projects;

namespace ProjectM.Api.Infrastructure.Persistence.Configurations;

public class SpaceConfiguration : IEntityTypeConfiguration<Space>
{
    public void Configure(EntityTypeBuilder<Space> e)
    {
        e.ToTable("Spaces");
        e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        e.Property(x => x.Description).HasMaxLength(500);
        e.Property(x => x.Color).HasMaxLength(20);
        e.HasIndex(x => x.Name).IsUnique();
    }
}

public class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> e)
    {
        e.ToTable("Clients");
        e.Property(x => x.Name).HasMaxLength(150).IsRequired();
        e.HasIndex(x => x.Name).IsUnique();
    }
}

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public const string NumberSequence = "ProjectNumbers";

    public void Configure(EntityTypeBuilder<Project> e)
    {
        e.ToTable("Projects");
        e.Property(x => x.Number).HasDefaultValueSql($"NEXT VALUE FOR {NumberSequence}");
        e.HasIndex(x => x.Number).IsUnique();
        e.Property(x => x.Code).HasMaxLength(20)
            .HasComputedColumnSql("CONCAT('PRJ-', RIGHT(CONCAT('0000', [Number]), CASE WHEN [Number] > 9999 THEN LEN([Number]) ELSE 4 END))", stored: true);
        e.HasIndex(x => x.Code);
        e.Property(x => x.Name).HasMaxLength(150).IsRequired();
        e.Property(x => x.Objective).HasMaxLength(2000);
        e.Property(x => x.Scope).HasMaxLength(4000);
        e.Property(x => x.Currency).HasMaxLength(3);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.Health).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.RowVersion).IsRowVersion();

        e.HasOne(x => x.Space).WithMany(s => s.Projects).HasForeignKey(x => x.SpaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Manager).WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Restrict);

        e.HasIndex(x => x.Status);
        e.HasIndex(x => x.SpaceId);
        e.HasIndex(x => x.ManagerId);
    }
}

public class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> e)
    {
        e.ToTable("ProjectMembers");
        e.HasKey(x => new { x.ProjectId, x.UserId });
        e.HasOne(x => x.Project).WithMany(p => p.Members).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.UserId);
    }
}

public class MilestoneConfiguration : IEntityTypeConfiguration<Milestone>
{
    public void Configure(EntityTypeBuilder<Milestone> e)
    {
        e.ToTable("Milestones");
        e.Property(x => x.Name).HasMaxLength(150).IsRequired();
        e.Property(x => x.Description).HasMaxLength(1000);
        e.HasOne<Project>().WithMany(p => p.Milestones).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.ProjectId, x.DueDate });
    }
}

public class RiskConfiguration : IEntityTypeConfiguration<Risk>
{
    public void Configure(EntityTypeBuilder<Risk> e)
    {
        e.ToTable("Risks");
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.Description).HasMaxLength(2000);
        e.Property(x => x.Mitigation).HasMaxLength(2000);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        e.Ignore(x => x.Severity);
        e.HasOne<Project>().WithMany(p => p.Risks).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.ProjectId, x.Status });
    }
}

public class ClosureChecklistTemplateItemConfiguration : IEntityTypeConfiguration<ClosureChecklistTemplateItem>
{
    public void Configure(EntityTypeBuilder<ClosureChecklistTemplateItem> e)
    {
        e.ToTable("ClosureChecklistTemplateItems");
        e.Property(x => x.Text).HasMaxLength(300).IsRequired();
    }
}

public class ProjectClosureChecklistItemConfiguration : IEntityTypeConfiguration<ProjectClosureChecklistItem>
{
    public void Configure(EntityTypeBuilder<ProjectClosureChecklistItem> e)
    {
        e.ToTable("ProjectClosureChecklistItems");
        e.Property(x => x.Text).HasMaxLength(300).IsRequired();
        e.Property(x => x.Notes).HasMaxLength(1000);
        e.HasOne<Project>().WithMany(p => p.ClosureChecklist).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.DoneBy).WithMany().HasForeignKey(x => x.DoneById).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.ProjectId);
    }
}

public class ProjectClosureConfiguration : IEntityTypeConfiguration<ProjectClosure>
{
    public void Configure(EntityTypeBuilder<ProjectClosure> e)
    {
        e.ToTable("ProjectClosures");
        e.HasKey(x => x.ProjectId);
        e.Property(x => x.Summary).HasMaxLength(4000);
        e.Property(x => x.ForceJustification).HasMaxLength(2000);
        e.PrimitiveCollection(x => x.Deliverables).HasColumnType("nvarchar(max)");
        e.HasOne<Project>().WithOne(p => p.Closure).HasForeignKey<ProjectClosure>(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.ClosedBy).WithMany().HasForeignKey(x => x.ClosedById).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Lessons).WithOne().HasForeignKey(l => l.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class LessonLearnedConfiguration : IEntityTypeConfiguration<LessonLearned>
{
    public void Configure(EntityTypeBuilder<LessonLearned> e)
    {
        e.ToTable("LessonsLearned");
        e.Property(x => x.Category).HasMaxLength(100);
        e.Property(x => x.WhatWentWell).HasMaxLength(2000);
        e.Property(x => x.WhatToImprove).HasMaxLength(2000);
        e.Property(x => x.Recommendation).HasMaxLength(2000);
    }
}

public class ProjectTemplateConfiguration : IEntityTypeConfiguration<ProjectTemplate>
{
    public void Configure(EntityTypeBuilder<ProjectTemplate> e)
    {
        e.ToTable("ProjectTemplates");
        e.Property(x => x.Name).HasMaxLength(150).IsRequired();
        e.Property(x => x.Description).HasMaxLength(1000);
        e.HasIndex(x => x.Name).IsUnique();
        e.HasMany(x => x.Tasks).WithOne().HasForeignKey(t => t.TemplateId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProjectTemplateTaskConfiguration : IEntityTypeConfiguration<ProjectTemplateTask>
{
    public void Configure(EntityTypeBuilder<ProjectTemplateTask> e)
    {
        e.ToTable("ProjectTemplateTasks");
        e.Property(x => x.ListName).HasMaxLength(100).IsRequired();
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.Description).HasMaxLength(4000);
        e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
    }
}
