using Microsoft.EntityFrameworkCore;
using ProjectM.Api.Infrastructure.Persistence;
using ProjectM.Domain.Projects;
using ProjectM.Domain.Tasks;

namespace ProjectM.Api.Infrastructure.Seed;

/// <summary>Datos de referencia mínimos (checklist de cierre y una plantilla base). Idempotente.</summary>
public class ReferenceDataSeeder(AppDbContext db, AppClock clock)
{
    public async Task SeedAsync()
    {
        if (!await db.ClosureChecklistTemplate.AnyAsync())
        {
            string[] required =
            [
                "Entregables aceptados formalmente por el cliente",
                "Documentación final entregada y archivada",
                "Cierre administrativo y financiero completado",
                "Lecciones aprendidas registradas en el acta"
            ];
            db.ClosureChecklistTemplate.AddRange(required.Select((t, i) =>
                new ClosureChecklistTemplateItem { Text = t, IsRequired = true, Position = i }));
            db.ClosureChecklistTemplate.Add(new ClosureChecklistTemplateItem
            {
                Text = "Accesos y recursos liberados",
                IsRequired = false,
                Position = required.Length
            });
        }

        if (!await db.ProjectTemplates.AnyAsync())
        {
            var t = new ProjectTemplate
            {
                Name = "Proyecto estándar",
                Description = "Fases de inicio, planificación, ejecución y cierre.",
                CreatedAt = clock.Now
            };
            (string List, string Title, TaskPriority P, int Offset, int Duration)[] tasks =
            [
                ("Inicio", "Reunión de arranque con el cliente", TaskPriority.Alta, 0, 1),
                ("Inicio", "Definir alcance y criterios de aceptación", TaskPriority.Alta, 1, 3),
                ("Planificación", "Elaborar cronograma", TaskPriority.Normal, 3, 3),
                ("Planificación", "Identificar riesgos iniciales", TaskPriority.Normal, 3, 2),
                ("Ejecución", "Desarrollo de entregables", TaskPriority.Normal, 7, 20),
                ("Ejecución", "Revisión de calidad", TaskPriority.Normal, 25, 3),
                ("Cierre", "Validación final con el cliente", TaskPriority.Alta, 28, 2),
                ("Cierre", "Documentar lecciones aprendidas", TaskPriority.Normal, 30, 1)
            ];
            t.Tasks = tasks.Select((x, i) => new ProjectTemplateTask
            {
                TemplateId = t.Id, ListName = x.List, Title = x.Title, Priority = x.P,
                StartOffsetDays = x.Offset, DurationDays = x.Duration, Position = i
            }).ToList();
            db.ProjectTemplates.Add(t);
        }

        await db.SaveChangesAsync();
    }
}
