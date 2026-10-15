using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Domain.Collaboration;
using ProjectM.Domain.Common;
using ProjectM.Domain.Projects;
using ProjectM.Domain.Tasks;
using ProjectM.Domain.Users;

namespace ProjectM.Api.Infrastructure.Seed;

/// <summary>
/// Datos de demostración (Seed:Demo=true). Fechas relativas a "hoy" para que el tablero siempre
/// luzca vivo. Idempotente: no hace nada si ya existen los usuarios demo.
/// </summary>
public class DemoSeeder(AppDbContext db, UserManager<AppUser> users, IConfiguration config, AppClock clock, ILogger<DemoSeeder> logger)
{
    public const string Domain = "projectm.demo";

    private DateOnly _today;
    private DateTimeOffset _now;
    private readonly Dictionary<string, AppUser> _u = [];
    private readonly Random _rnd = new(2026);

    public async Task SeedAsync()
    {
        if (!config.GetValue<bool>("Seed:Demo")) return;
        var password = config["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Seed:Demo=true pero falta Seed:DemoPassword; no se cargan datos demo.");
            return;
        }
        if (await db.Users.AnyAsync(u => u.Email!.EndsWith("@" + Domain))) return;

        _today = clock.Today;
        _now = clock.Now;

        await CreateUser("laura", "Laura Méndez", Roles.Gestor, password);
        await CreateUser("carlos", "Carlos Rivas", Roles.Gestor, password);
        await CreateUser("ana", "Ana Torres", Roles.Miembro, password);
        await CreateUser("diego", "Diego Paredes", Roles.Miembro, password);
        await CreateUser("sofia", "Sofía Herrera", Roles.Miembro, password);
        await CreateUser("mateo", "Mateo Castillo", Roles.Miembro, password);
        await CreateUser("valeria", "Valeria Ortiz", Roles.Observador, password);

        var tech = new Space { Name = "Tecnología", Description = "Proyectos de sistemas y producto digital", Color = "primary", Position = 0, CreatedAt = _now };
        var ops = new Space { Name = "Operaciones y Clientes", Description = "Implementaciones y mejoras para clientes", Color = "accent", Position = 1, CreatedAt = _now };
        db.Spaces.AddRange(tech, ops);

        var tags = new Dictionary<string, Tag>();
        foreach (var (name, color) in new[] { ("backend", "primary"), ("frontend", "accent"), ("urgente", "danger"), ("cliente", "secondary"), ("documentación", "success") })
        {
            var tag = await db.Tags.FirstOrDefaultAsync(x => x.Name == name);
            if (tag is null) db.Tags.Add(tag = new Tag { Name = name, Color = color });
            tags[name] = tag;
        }

        var crm = Crm(ops, tags);
        var app = MobileApp(tech, tags);
        Website(tech);
        await Cloud(tech);

        Notify("ana", "TaskAssigned", "Te asignaron «Configurar integración con ERP»", "Por Laura Méndez", crm);
        Notify("diego", "Mention", "Carlos Rivas te mencionó en «Pasarela de pagos»", "@Diego Paredes ¿cómo vamos con el sandbox?", app);
        Notify("sofia", "TaskOverdue", "«Pruebas en dispositivos Android» está vencida", "Vencimiento pasado", app);
        Notify("mateo", "AddedToProject", "Te agregaron al proyecto «App móvil de pedidos»", "Por Carlos Rivas", app);

        // Los eventos de dominio generados al construir los datos no deben producir historial duplicado.
        foreach (var e in db.ChangeTracker.Entries<Entity>()) e.Entity.ClearDomainEvents();
        await db.SaveChangesAsync();
        logger.LogInformation("Datos demo cargados (usuarios @{Domain}).", Domain);
    }

    // ---------------- Proyectos ----------------

    private Project Crm(Space space, Dictionary<string, Tag> tags)
    {
        var p = NewProject(space, "Implementación CRM", "Grupo Andino", "laura", -30, 45,
            "Centralizar la gestión comercial del cliente en un CRM integrado con su ERP.",
            "Módulos de ventas y servicio, integración con ERP, migración de 3 años de datos y capacitación.",
            85000m, ["ana", "diego", "sofia", "valeria"]);
        p.Activate(_today.AddDays(-28));

        var (analisis, construccion, despliegue) = (List(p, "Análisis", 0), List(p, "Construcción", 1), List(p, "Despliegue", 2));
        var m1 = Milestone(p, "Diseño aprobado", -10, done: true);
        var m2 = Milestone(p, "Integración ERP lista", 12);
        Milestone(p, "Salida a producción", 40);

        var effort = new CustomFieldDefinition { ProjectId = p.Id, Name = "Esfuerzo (h)", Type = CustomFieldType.Numero, Position = 0 };
        var env = new CustomFieldDefinition { ProjectId = p.Id, Name = "Ambiente", Type = CustomFieldType.Seleccion, Options = ["Desarrollo", "Pruebas", "Producción"], Position = 1 };
        db.CustomFields.AddRange(effort, env);

        AddTask(p, analisis, "Levantamiento de requerimientos", "laura", TaskItemStatus.Completada, -28, -20, m1, [tags["cliente"]]);
        AddTask(p, analisis, "Mapa de procesos comerciales", "ana", TaskItemStatus.Completada, -22, -15, m1);
        AddTask(p, analisis, "Modelo de datos del CRM", "diego", TaskItemStatus.Completada, -18, -11, m1, [tags["backend"]]);
        var erp = AddTask(p, construccion, "Configurar integración con ERP", "ana", TaskItemStatus.EnProgreso, -8, 8, m2, [tags["backend"]],
            "Sincronizar clientes, productos y pedidos cada 15 minutos.");
        Value(erp, effort, "40");
        Value(erp, env, "Pruebas");
        Subtask(erp, "Mapeo de campos de clientes", "ana", TaskItemStatus.Completada);
        Subtask(erp, "Webhook de pedidos", "diego", TaskItemStatus.EnProgreso);
        Subtask(erp, "Manejo de reintentos", "ana", TaskItemStatus.Pendiente);
        var pipeline = AddTask(p, construccion, "Pipeline de ventas y etapas", "sofia", TaskItemStatus.EnRevision, -6, 2, m2, [tags["frontend"]]);
        Value(pipeline, effort, "16");
        AddTask(p, construccion, "Migración de datos históricos", "diego", TaskItemStatus.EnProgreso, -3, 15, m2, [tags["backend"]]);
        AddTask(p, construccion, "Tableros de servicio al cliente", "sofia", TaskItemStatus.Pendiente, 3, 18, null, [tags["frontend"]]);
        AddTask(p, construccion, "Reglas de asignación de leads", "ana", TaskItemStatus.Pendiente, 5, 20);
        AddTask(p, construccion, "Permisos por rol comercial", "diego", TaskItemStatus.Pendiente, 8, 22, null, [tags["backend"]]);
        AddTask(p, despliegue, "Plan de capacitación", "laura", TaskItemStatus.Pendiente, 20, 30, null, [tags["documentación"]]);
        AddTask(p, despliegue, "Pruebas de aceptación con el cliente", "laura", TaskItemStatus.Pendiente, 28, 36, null, [tags["cliente"]]);
        AddTask(p, despliegue, "Salida a producción", "diego", TaskItemStatus.Pendiente, 38, 40);

        Risk(p, "Calidad de datos del ERP", "Duplicados y campos incompletos en el maestro de clientes", 2, 3, "diego", "Limpieza previa y reglas de deduplicación");
        Risk(p, "Disponibilidad del equipo del cliente", null, 2, 2, "laura", "Agenda fija de validaciones semanales");

        Comment(erp, "laura", "¿Ya tenemos credenciales del ambiente de pruebas del ERP?", []);
        Comment(erp, "ana", "Sí, llegaron ayer. Ya sincronicé clientes; sigo con pedidos.", []);
        Comment(pipeline, "sofia", "@Laura Méndez lo dejé listo para revisión, falta validar etapas con el cliente.", ["laura"]);

        Activity(p, null, "laura", "ProjectCreated", "abrió el proyecto «Implementación CRM»", -30);
        Activity(p, null, "laura", "ProjectStatusChanged", "cambió el estado del proyecto de Planeación a Activo", -28);
        Activity(p, pipeline.Id, "sofia", "TaskStatusChanged", "movió «Pipeline de ventas y etapas» de En progreso a En revisión", -1);
        Activity(p, erp.Id, "ana", "CommentAdded", "comentó en «Configurar integración con ERP»", 0);
        return p;
    }

    private Project MobileApp(Space space, Dictionary<string, Tag> tags)
    {
        var p = NewProject(space, "App móvil de pedidos", "Distribuidora Norte", "carlos", -45, 10,
            "Permitir a los clientes hacer pedidos desde el celular con seguimiento en tiempo real.",
            "App iOS/Android, catálogo, carrito, pagos en línea y notificaciones push.",
            120000m, ["diego", "sofia", "mateo", "valeria"]);
        p.Activate(_today.AddDays(-44));
        p.Health = ProjectHealth.EnRiesgo;

        var (diseno, desarrollo, qa) = (List(p, "Diseño", 0), List(p, "Desarrollo", 1), List(p, "QA", 2));
        Milestone(p, "MVP interno", -5);
        var beta = Milestone(p, "Beta con clientes", 8);

        AddTask(p, diseno, "Prototipo navegable", "sofia", TaskItemStatus.Completada, -44, -35, null, [tags["frontend"]]);
        AddTask(p, diseno, "Sistema de diseño móvil", "sofia", TaskItemStatus.Completada, -38, -28, null, [tags["frontend"]]);
        AddTask(p, desarrollo, "Catálogo y búsqueda", "mateo", TaskItemStatus.Completada, -30, -12, null, [tags["frontend"]]);
        AddTask(p, desarrollo, "Carrito de compras", "mateo", TaskItemStatus.EnRevision, -15, -2, beta, [tags["frontend"]]);
        var pagos = AddTask(p, desarrollo, "Pasarela de pagos", "diego", TaskItemStatus.Bloqueada, -12, -3, beta, [tags["backend"], tags["urgente"]],
            "Integración con el proveedor de pagos en modo sandbox.", blockedReason: "Esperando credenciales del proveedor de pagos");
        AddTask(p, desarrollo, "Notificaciones push", "diego", TaskItemStatus.EnProgreso, -6, 4, beta, [tags["backend"]]);
        AddTask(p, desarrollo, "Seguimiento de pedido en tiempo real", "mateo", TaskItemStatus.EnProgreso, -4, 6, beta);
        AddTask(p, desarrollo, "Inicio de sesión con teléfono", "diego", TaskItemStatus.Pendiente, -2, -1, beta, [tags["urgente"]]);
        AddTask(p, qa, "Pruebas en dispositivos Android", "sofia", TaskItemStatus.EnProgreso, -10, -4, beta);
        AddTask(p, qa, "Pruebas en dispositivos iOS", "sofia", TaskItemStatus.Pendiente, -3, 3, beta);
        AddTask(p, qa, "Pruebas de carga del API", "diego", TaskItemStatus.Pendiente, 2, 7, null, [tags["backend"]]);
        AddTask(p, qa, "Publicación en tiendas", "carlos", TaskItemStatus.Pendiente, 7, 10, null, [tags["cliente"]]);

        Risk(p, "Retraso del proveedor de pagos", "Sin credenciales productivas no hay beta", 3, 3, "carlos", "Escalar con el proveedor y evaluar alternativa");
        Risk(p, "Revisión de tiendas puede tardar", null, 2, 2, "carlos", "Enviar a revisión con una semana de holgura");

        Comment(pagos, "carlos", "@Diego Paredes ¿cómo vamos con el sandbox?", ["diego"]);
        Comment(pagos, "diego", "Sandbox funcionando. Bloqueado para productivo hasta que el proveedor envíe las llaves.", []);

        Activity(p, null, "carlos", "ProjectCreated", "abrió el proyecto «App móvil de pedidos»", -45);
        Activity(p, pagos.Id, "diego", "TaskStatusChanged", "movió «Pasarela de pagos» de En progreso a Bloqueada", -2);
        return p;
    }

    private void Website(Space space)
    {
        var p = NewProject(space, "Renovación sitio web", "Uso interno", "laura", 10, 70,
            "Renovar el sitio corporativo con nueva identidad y mejor posicionamiento.",
            "Arquitectura de información, diseño, CMS y migración de contenidos.", null, ["sofia", "mateo"]);
        var inicio = List(p, "Inicio", 0);
        var diseno = List(p, "Diseño", 1);
        AddTask(p, inicio, "Reunión de arranque", "laura", TaskItemStatus.Pendiente, 10, 10);
        AddTask(p, inicio, "Inventario de contenidos", "mateo", TaskItemStatus.Pendiente, 11, 18);
        AddTask(p, inicio, "Benchmark de sitios del sector", "sofia", TaskItemStatus.Pendiente, 11, 16);
        AddTask(p, diseno, "Arquitectura de información", "sofia", TaskItemStatus.Pendiente, 18, 25);
        AddTask(p, diseno, "Wireframes de páginas clave", "sofia", TaskItemStatus.Pendiente, 25, 35);
        AddTask(p, diseno, "Selección de CMS", "mateo", TaskItemStatus.Pendiente, 20, 28);
        Activity(p, null, "laura", "ProjectCreated", "abrió el proyecto «Renovación sitio web»", -2);
    }

    private async Task Cloud(Space space)
    {
        var p = NewProject(space, "Migración a la nube 2026", "Uso interno", "carlos", -120, -20,
            "Migrar los servidores locales a la nube reduciendo costos de operación.",
            "Inventario, migración de 12 servicios, monitoreo y apagado del centro de datos.", 60000m, ["diego", "mateo", "ana"]);
        p.Activate(_today.AddDays(-118));
        var plan = List(p, "Planificación", 0);
        var mig = List(p, "Migración", 1);
        string[] done = ["Inventario de servidores", "Diseño de red en la nube", "Estimación de costos", "Migrar base de datos", "Migrar servicios web",
            "Migrar almacenamiento de archivos", "Configurar monitoreo", "Pruebas de recuperación", "Apagar servidores locales", "Documentación operativa"];
        for (var i = 0; i < done.Length; i++)
            AddTask(p, i < 3 ? plan : mig, done[i], i % 2 == 0 ? "diego" : "mateo", TaskItemStatus.Completada, -118 + i * 9, -110 + i * 9);
        AddTask(p, mig, "Migrar servidor de reportes heredado", "ana", TaskItemStatus.Pendiente, -40, -25);

        // Cierre forzado: la tarea del servidor heredado quedó fuera de alcance.
        p.StartClosure(await db.ClosureChecklistTemplate.AsNoTracking().ToListAsync());
        foreach (var item in p.ClosureChecklist) { item.IsDone = item.IsRequired; item.DoneById = item.IsDone ? _u["carlos"].Id : null; item.DoneAt = item.IsDone ? _now.AddDays(-20) : null; }
        var c = p.Closure!;
        c.Summary = "Se migraron 11 de 12 servicios con un 18 % de ahorro mensual. El servidor de reportes heredado se retirará en un proyecto aparte.";
        c.Deliverables = ["Infraestructura en la nube operando", "Runbook de operación", "Informe de costos"];
        c.Lessons =
        [
            new LessonLearned { ProjectId = p.Id, Category = "Planificación", WhatWentWell = "El inventario temprano evitó sorpresas.", WhatToImprove = "Subestimamos los sistemas heredados.", Recommendation = "Evaluar sistemas heredados en la fase de inicio.", Position = 0 },
            new LessonLearned { ProjectId = p.Id, Category = "Equipo", WhatWentWell = "Buena coordinación con operaciones.", WhatToImprove = "Documentar decisiones en el momento.", Recommendation = "Bitácora de decisiones compartida.", Position = 1 }
        ];
        c.PlannedStart = p.PlannedStart; c.PlannedEnd = p.PlannedEnd; c.ActualStart = p.ActualStart; c.ActualEnd = _today.AddDays(-15);
        c.TotalTasks = 11; c.CompletedTasks = 10; c.OpenTasksAtClose = 1; c.IsForced = true;
        c.ForceJustification = "La migración del servidor de reportes se trasladó a un proyecto independiente acordado con dirección.";
        c.ClosedById = _u["carlos"].Id; c.ClosedAt = _now.AddDays(-15);
        p.ActualEnd = _today.AddDays(-15);
        p.ForceStatusForSeed(ProjectStatus.Cerrado, _now.AddDays(-15), _u["carlos"].Id);

        Activity(p, null, "carlos", "ProjectClosed", "cerró el proyecto de forma forzada (con tareas abiertas)", -15);
    }

    // ---------------- Auxiliares ----------------

    private async Task CreateUser(string key, string name, string role, string password)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(), Email = $"{key}@{Domain}", UserName = $"{key}@{Domain}", FullName = name,
            EmailConfirmed = true, CreatedAt = _now
        };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException("No se pudo crear el usuario demo: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, role);
        _u[key] = user;
    }

    private Project NewProject(Space space, string name, string client, string manager, int startOffset, int endOffset,
        string objective, string scope, decimal? budget, string[] members)
    {
        var c = db.Clients.Local.FirstOrDefault(x => x.Name == client) ?? new Client { Name = client, CreatedAt = _now };
        if (db.Entry(c).State == EntityState.Detached) db.Clients.Add(c);
        var p = new Project
        {
            SpaceId = space.Id, Name = name, ClientId = c.Id, Objective = objective, Scope = scope,
            PlannedStart = _today.AddDays(startOffset), PlannedEnd = _today.AddDays(endOffset), Budget = budget, Currency = "USD",
            ManagerId = _u[manager].Id, CreatedAt = _now.AddDays(startOffset - 2), CreatedById = _u[manager].Id
        };
        foreach (var m in members.Append(manager).Distinct())
            p.Members.Add(new ProjectMember { ProjectId = p.Id, UserId = _u[m].Id, AddedAt = p.CreatedAt });
        db.Projects.Add(p);
        return p;
    }

    private TaskList List(Project p, string name, int position)
    {
        var l = new TaskList { ProjectId = p.Id, Name = name, Position = position, CreatedAt = p.CreatedAt };
        p.Lists.Add(l);
        return l;
    }

    private Milestone Milestone(Project p, string name, int dueOffset, bool done = false)
    {
        var m = new Milestone { ProjectId = p.Id, Name = name, DueDate = _today.AddDays(dueOffset), CompletedAt = done ? _now.AddDays(dueOffset) : null };
        p.Milestones.Add(m);
        return m;
    }

    private double _position;

    private TaskItem AddTask(Project p, TaskList list, string title, string assignee, TaskItemStatus status, int startOffset, int dueOffset,
        Milestone? milestone = null, Tag[]? tags = null, string? description = null, string? blockedReason = null)
    {
        var t = new TaskItem
        {
            ProjectId = p.Id, ListId = list.Id, Title = title, Description = description, MilestoneId = milestone?.Id,
            Priority = blockedReason is not null || tags?.Any(x => x.Name == "urgente") == true ? TaskPriority.Urgente : _rnd.Next(4) == 0 ? TaskPriority.Alta : TaskPriority.Normal,
            Position = _position += 1000, CreatedById = p.ManagerId, CreatedAt = _now.AddDays(Math.Min(startOffset, 0) - 1), UpdatedAt = _now
        };
        t.Assign(_u[assignee].Id);
        t.SetDates(_today.AddDays(startOffset), _today.AddDays(dueOffset));
        if (status != TaskItemStatus.Pendiente)
            t.ChangeStatus(status, blockedReason, status == TaskItemStatus.Completada ? _now.AddDays(Math.Min(dueOffset, 0)) : _now);
        foreach (var tag in tags ?? []) t.Tags.Add(new TaskTag { TaskId = t.Id, TagId = tag.Id });
        list.Tasks.Add(t);
        return t;
    }

    private void Subtask(TaskItem parent, string title, string assignee, TaskItemStatus status)
    {
        var s = new TaskItem
        {
            ProjectId = parent.ProjectId, ListId = parent.ListId, ParentTaskId = parent.Id, Title = title,
            Position = _position += 1000, CreatedById = parent.CreatedById, CreatedAt = parent.CreatedAt, UpdatedAt = _now
        };
        s.Assign(_u[assignee].Id);
        s.SetDates(parent.StartDate, parent.DueDate);
        if (status != TaskItemStatus.Pendiente) s.ChangeStatus(status, null, _now);
        db.Tasks.Add(s);
    }

    private static void Value(TaskItem t, CustomFieldDefinition field, string raw) => t.CustomFieldValues.Add(field.Parse(t.Id, raw)!);

    private void Risk(Project p, string title, string? description, int probability, int impact, string owner, string mitigation) =>
        p.Risks.Add(new Risk
        {
            ProjectId = p.Id, Title = title, Description = description, Probability = probability, Impact = impact,
            OwnerId = _u[owner].Id, Mitigation = mitigation, CreatedAt = _now.AddDays(-7)
        });

    private int _minutes;

    private void Comment(TaskItem t, string author, string body, string[] mentions) =>
        db.Comments.Add(new Comment
        {
            TaskId = t.Id, AuthorId = _u[author].Id, Body = body, Mentions = mentions.Select(m => _u[m].Id).ToList(),
            CreatedAt = _now.AddHours(-6).AddMinutes(_minutes += 35)
        });

    private void Activity(Project p, Guid? taskId, string actor, string type, string message, int dayOffset) =>
        db.ActivityLogs.Add(new ActivityLog
        {
            ProjectId = p.Id, TaskId = taskId, ActorId = _u[actor].Id, Type = type, Message = message,
            CreatedAt = _now.AddDays(dayOffset).AddMinutes(-(_minutes += 7))
        });

    private void Notify(string user, string type, string title, string body, Project p) =>
        db.Notifications.Add(new Notification
        {
            UserId = _u[user].Id, Type = type, Title = title, Body = body, Link = $"/projects/{p.Id}", ProjectId = p.Id,
            CreatedAt = _now.AddMinutes(-(_minutes += 13))
        });

}
