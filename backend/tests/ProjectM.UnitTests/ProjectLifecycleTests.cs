using FluentAssertions;
using ProjectM.Domain.Common;
using ProjectM.Domain.Projects;

namespace ProjectM.UnitTests;

public class ProjectLifecycleTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static Project NewProject() => new()
    {
        Name = "Demo",
        PlannedStart = new DateOnly(2026, 9, 1),
        PlannedEnd = new DateOnly(2026, 12, 31)
    };

    private static ClosureChecklistTemplateItem[] Checklist =>
    [
        new() { Text = "Entregables aceptados", IsRequired = true, Position = 0 },
        new() { Text = "Opcional", IsRequired = false, Position = 1 }
    ];

    private static Project InClosure()
    {
        var p = NewProject();
        p.Activate(Today);
        p.StartClosure(Checklist);
        return p;
    }

    private static void CompleteRequirements(Project p)
    {
        foreach (var i in p.ClosureChecklist.Where(i => i.IsRequired)) i.IsDone = true;
        p.Closure!.Summary = "Proyecto entregado a satisfacción.";
    }

    [Fact]
    public void Nuevo_proyecto_inicia_en_Planeacion_y_al_activar_fija_inicio_real()
    {
        var p = NewProject();
        p.Status.Should().Be(ProjectStatus.Planeacion);
        p.Activate(Today);
        p.Status.Should().Be(ProjectStatus.Activo);
        p.ActualStart.Should().Be(Today);
        p.DomainEvents.OfType<ProjectStatusChanged>().Should().ContainSingle();
    }

    [Fact]
    public void Pausa_y_reanudacion()
    {
        var p = NewProject();
        p.Activate(Today);
        p.Pause();
        p.Status.Should().Be(ProjectStatus.EnPausa);
        p.Resume();
        p.Status.Should().Be(ProjectStatus.Activo);
    }

    [Fact]
    public void No_se_puede_pausar_un_proyecto_en_planeacion()
    {
        var act = () => NewProject().Pause();
        act.Should().Throw<DomainException>().Which.Code.Should().Be("TRANSICION_INVALIDA");
    }

    [Fact]
    public void Iniciar_cierre_copia_el_checklist_y_crea_el_acta()
    {
        var p = InClosure();
        p.Status.Should().Be(ProjectStatus.EnCierre);
        p.ClosureChecklist.Should().HaveCount(2);
        p.Closure.Should().NotBeNull();
    }

    [Fact]
    public void No_se_puede_cerrar_sin_iniciar_el_cierre()
    {
        var p = NewProject();
        p.Activate(Today);
        var act = () => p.Close(new ProjectMetrics(0, 0), false, null, Guid.NewGuid(), Now, Today);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("TRANSICION_INVALIDA");
    }

    [Fact]
    public void Cierre_con_tareas_abiertas_sin_forzar_falla()
    {
        var p = InClosure();
        CompleteRequirements(p);
        var act = () => p.Close(new ProjectMetrics(10, 8), false, null, Guid.NewGuid(), Now, Today);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("TAREAS_ABIERTAS");
        p.Status.Should().Be(ProjectStatus.EnCierre);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("muy corta")]
    public void Cierre_forzado_exige_justificacion_suficiente(string? justification)
    {
        var p = InClosure();
        CompleteRequirements(p);
        var act = () => p.Close(new ProjectMetrics(10, 8), true, justification, Guid.NewGuid(), Now, Today);
        var ex = act.Should().Throw<DomainException>().Which;
        ex.Code.Should().Be("JUSTIFICACION_REQUERIDA");
        ex.Kind.Should().Be(DomainErrorKind.Validation);
    }

    [Fact]
    public void Cierre_forzado_con_justificacion_registra_el_acta()
    {
        var p = InClosure();
        CompleteRequirements(p);
        var by = Guid.NewGuid();
        p.Close(new ProjectMetrics(10, 8), true, "El cliente canceló las dos tareas restantes.", by, Now, Today);

        p.Status.Should().Be(ProjectStatus.Cerrado);
        p.ActualEnd.Should().Be(Today);
        p.Closure!.IsForced.Should().BeTrue();
        p.Closure.OpenTasksAtClose.Should().Be(2);
        p.Closure.ForceJustification.Should().NotBeNullOrEmpty();
        p.Closure.ClosedById.Should().Be(by);
        p.DomainEvents.OfType<ProjectClosed>().Single().Forced.Should().BeTrue();
    }

    [Fact]
    public void Cierre_normal_no_marca_forzado_aunque_se_pida_forzar()
    {
        var p = InClosure();
        CompleteRequirements(p);
        p.Close(new ProjectMetrics(5, 5), true, null, Guid.NewGuid(), Now, Today);
        p.Closure!.IsForced.Should().BeFalse();
        p.Status.Should().Be(ProjectStatus.Cerrado);
    }

    [Fact]
    public void Checklist_obligatorio_incompleto_impide_el_cierre_incluso_forzado()
    {
        var p = InClosure();
        p.Closure!.Summary = "Resumen";
        var act = () => p.Close(new ProjectMetrics(1, 1), true, "Justificación suficientemente larga", Guid.NewGuid(), Now, Today);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("CHECKLIST_INCOMPLETO");
    }

    [Fact]
    public void Item_opcional_del_checklist_no_bloquea()
    {
        var p = InClosure();
        CompleteRequirements(p);
        p.ClosureChecklist.Single(i => !i.IsRequired).IsDone.Should().BeFalse();
        ClosureRules.Validate(p, new ProjectMetrics(3, 3), false, null).Should().BeEmpty();
    }

    [Fact]
    public void Acta_sin_resumen_impide_el_cierre()
    {
        var p = InClosure();
        foreach (var i in p.ClosureChecklist) i.IsDone = true;
        var problems = ClosureRules.Validate(p, new ProjectMetrics(1, 1), false, null);
        problems.Select(x => x.Code).Should().Equal("ACTA_INCOMPLETA");
    }

    [Fact]
    public void Proyecto_cerrado_es_solo_lectura_y_no_admite_transiciones()
    {
        var p = InClosure();
        CompleteRequirements(p);
        p.Close(new ProjectMetrics(0, 0), false, null, Guid.NewGuid(), Now, Today);

        p.Invoking(x => x.EnsureEditable()).Should().Throw<DomainException>().Which.Code.Should().Be("PROYECTO_CERRADO");
        p.Invoking(x => x.Pause()).Should().Throw<DomainException>().Which.Code.Should().Be("PROYECTO_CERRADO");
    }

    [Fact]
    public void Reabrir_exige_motivo_y_vuelve_a_Activo()
    {
        var p = InClosure();
        CompleteRequirements(p);
        p.Close(new ProjectMetrics(0, 0), false, null, Guid.NewGuid(), Now, Today);

        p.Invoking(x => x.Reopen("corto")).Should().Throw<DomainException>().Which.Code.Should().Be("MOTIVO_REQUERIDO");
        p.Reopen("El cliente solicitó un entregable adicional.");
        p.Status.Should().Be(ProjectStatus.Activo);
        p.ActualEnd.Should().BeNull();
        p.ClosedAt.Should().BeNull();
    }

    [Fact]
    public void Cancelar_cierre_vuelve_a_Activo_y_conserva_el_checklist()
    {
        var p = InClosure();
        p.ClosureChecklist[0].IsDone = true;
        p.CancelClosure();
        p.Status.Should().Be(ProjectStatus.Activo);
        p.StartClosure(Checklist);
        p.ClosureChecklist.Should().HaveCount(2);
        p.ClosureChecklist[0].IsDone.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3, 1, 33)]
    [InlineData(3, 2, 67)]
    [InlineData(4, 4, 100)]
    public void Avance_se_calcula_por_tareas_completadas(int total, int completed, int expected) =>
        new ProjectMetrics(total, completed).ProgressPercent.Should().Be(expected);
}
