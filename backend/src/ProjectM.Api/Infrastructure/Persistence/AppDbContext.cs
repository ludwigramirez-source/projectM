using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ProjectM.Api.Infrastructure.Events;
using ProjectM.Api.Infrastructure.Persistence.Configurations;
using ProjectM.Domain.Collaboration;
using ProjectM.Domain.Common;
using ProjectM.Domain.Projects;
using ProjectM.Domain.Tasks;
using ProjectM.Domain.Users;

namespace ProjectM.Api.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, DomainEventDispatcher? dispatcher = null)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    private readonly List<IDomainEvent> _pendingEvents = [];
    private bool _dispatching;

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Space> Spaces => Set<Space>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<Milestone> Milestones => Set<Milestone>();
    public DbSet<Risk> Risks => Set<Risk>();
    public DbSet<ProjectTemplate> ProjectTemplates => Set<ProjectTemplate>();
    public DbSet<ProjectTemplateTask> ProjectTemplateTasks => Set<ProjectTemplateTask>();
    public DbSet<ClosureChecklistTemplateItem> ClosureChecklistTemplate => Set<ClosureChecklistTemplateItem>();
    public DbSet<ProjectClosureChecklistItem> ProjectClosureChecklistItems => Set<ProjectClosureChecklistItem>();
    public DbSet<ProjectClosure> ProjectClosures => Set<ProjectClosure>();
    public DbSet<LessonLearned> LessonsLearned => Set<LessonLearned>();

    public DbSet<TaskList> TaskLists => Set<TaskList>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TaskTag> TaskTags => Set<TaskTag>();
    public DbSet<CustomFieldDefinition> CustomFields => Set<CustomFieldDefinition>();
    public DbSet<CustomFieldValue> CustomFieldValues => Set<CustomFieldValue>();
    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<Notification> Notifications => Set<Notification>();

    /// <summary>Registra un evento que no nace de una entidad rastreada (p. ej. tras un ExecuteUpdate).</summary>
    public void AddEvent(IDomainEvent e) => _pendingEvents.Add(e);

    /// <summary>
    /// Antes de guardar: detecta ediciones de tareas, recoge eventos de dominio y deja que el
    /// despachador agregue historial y notificaciones en la MISMA transacción. Después de
    /// confirmar: entrega notificaciones y avisos en tiempo real.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        if (dispatcher is null || _dispatching) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);

        ChangeTracker.DetectChanges();
        RaiseTaskUpdates();
        var events = ChangeTracker.Entries<Entity>().SelectMany(e => e.Entity.DomainEvents).Concat(_pendingEvents).ToList();
        foreach (var entry in ChangeTracker.Entries<Entity>()) entry.Entity.ClearDomainEvents();
        _pendingEvents.Clear();

        PostCommitWork? work = null;
        if (events.Count > 0)
        {
            _dispatching = true;
            try { work = await dispatcher.BeforeSaveAsync(this, events, ct); }
            finally { _dispatching = false; }
        }

        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
        if (work is not null) await dispatcher.AfterSaveAsync(work, ct);
        return result;
    }

    private static readonly Dictionary<string, string> TrackedTaskFields = new()
    {
        [nameof(TaskItem.Title)] = "el título",
        [nameof(TaskItem.Description)] = "la descripción",
        [nameof(TaskItem.Priority)] = "la prioridad",
        [nameof(TaskItem.StartDate)] = "la fecha de inicio",
        [nameof(TaskItem.DueDate)] = "el vencimiento",
        [nameof(TaskItem.MilestoneId)] = "el hito",
        [nameof(TaskItem.ListId)] = "la lista"
    };

    private void RaiseTaskUpdates()
    {
        foreach (var entry in ChangeTracker.Entries<TaskItem>().Where(e => e.State == EntityState.Modified))
        {
            var fields = entry.Properties
                .Where(p => p.IsModified && TrackedTaskFields.ContainsKey(p.Metadata.Name) && !Equals(p.OriginalValue, p.CurrentValue))
                .Select(p => TrackedTaskFields[p.Metadata.Name]).ToList();
            if (fields.Count > 0) entry.Entity.Raise(new TaskUpdated(entry.Entity.Id, entry.Entity.ProjectId, fields));
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(e =>
        {
            e.ToTable("Users");
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.HasIndex(x => x.IsActive);
        });
        builder.Entity<AppRole>().ToTable("Roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");

        builder.HasSequence<int>(ProjectConfiguration.NumberSequence).StartsAt(1).IncrementsBy(1);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var type in builder.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            builder.Entity(type.ClrType).Ignore(nameof(Entity.DomainEvents));
            builder.Entity(type.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Strings sin longitud explícita: varchar(500) (los textos largos declaran "text").
        builder.Properties<string>().HaveMaxLength(500);
        builder.Properties<decimal>().HavePrecision(18, 2);
    }
}
