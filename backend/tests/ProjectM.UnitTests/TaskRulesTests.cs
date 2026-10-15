using FluentAssertions;
using ProjectM.Domain.Common;
using ProjectM.Domain.Tasks;

namespace ProjectM.UnitTests;

public class TaskRulesTests
{
    private static readonly DateTimeOffset T1 = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = T1.AddHours(2);

    [Fact]
    public void Completar_fija_CompletedAt_y_reabrir_lo_limpia()
    {
        var t = new TaskItem();
        t.ChangeStatus(TaskItemStatus.Completada, null, T1);
        t.CompletedAt.Should().Be(T1);
        t.IsOpen.Should().BeFalse();

        t.ChangeStatus(TaskItemStatus.Completada, null, T2);
        t.CompletedAt.Should().Be(T1, "volver a guardar como completada no cambia la fecha");

        t.ChangeStatus(TaskItemStatus.EnProgreso, null, T2);
        t.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void Bloquear_exige_motivo_y_desbloquear_lo_limpia()
    {
        var t = new TaskItem();
        t.Invoking(x => x.ChangeStatus(TaskItemStatus.Bloqueada, " ", T1))
            .Should().Throw<DomainException>().Which.Code.Should().Be("MOTIVO_BLOQUEO_REQUERIDO");

        t.ChangeStatus(TaskItemStatus.Bloqueada, "Esperando accesos del cliente", T1);
        t.BlockedReason.Should().Be("Esperando accesos del cliente");
        t.ChangeStatus(TaskItemStatus.EnProgreso, null, T2);
        t.BlockedReason.Should().BeNull();
    }

    [Fact]
    public void Cambio_de_estado_emite_evento_solo_si_cambia()
    {
        var t = new TaskItem();
        t.ChangeStatus(TaskItemStatus.Pendiente, null, T1);
        t.DomainEvents.Should().BeEmpty();
        t.ChangeStatus(TaskItemStatus.EnRevision, null, T1);
        t.DomainEvents.OfType<TaskStatusChanged>().Single().To.Should().Be(TaskItemStatus.EnRevision);
    }

    [Fact]
    public void Vencida_se_deriva_de_la_fecha_y_el_estado()
    {
        var today = new DateOnly(2026, 10, 15);
        var t = new TaskItem();
        t.SetDates(null, today.AddDays(-1));
        t.IsOverdue(today).Should().BeTrue();
        t.ChangeStatus(TaskItemStatus.Completada, null, T1);
        t.IsOverdue(today).Should().BeFalse();
    }

    [Fact]
    public void Vencimiento_no_puede_ser_anterior_al_inicio()
    {
        var t = new TaskItem();
        t.Invoking(x => x.SetDates(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 1)))
            .Should().Throw<DomainException>().Which.Code.Should().Be("RANGO_FECHAS");
    }

    [Theory]
    [InlineData(CustomFieldType.Numero, "12.5", true)]
    [InlineData(CustomFieldType.Numero, "doce", false)]
    [InlineData(CustomFieldType.Fecha, "2026-10-15", true)]
    [InlineData(CustomFieldType.Fecha, "15/10/2026", false)]
    [InlineData(CustomFieldType.Seleccion, "Alta", true)]
    [InlineData(CustomFieldType.Seleccion, "Otra", false)]
    [InlineData(CustomFieldType.Texto, "libre", true)]
    public void Campos_personalizados_validan_segun_su_tipo(CustomFieldType type, string raw, bool valid)
    {
        var field = new CustomFieldDefinition { Name = "Campo", Type = type, Options = ["Alta", "Baja"] };
        var act = () => field.Parse(Guid.NewGuid(), raw);
        if (valid) act.Should().NotThrow();
        else act.Should().Throw<DomainException>().Which.Code.Should().Be("CAMPO_INVALIDO");
    }

    [Fact]
    public void Campo_requerido_vacio_falla()
    {
        var field = new CustomFieldDefinition { Name = "Cliente", Type = CustomFieldType.Texto, IsRequired = true };
        field.Invoking(f => f.Parse(Guid.NewGuid(), "  ")).Should().Throw<DomainException>()
            .Which.Code.Should().Be("CAMPO_REQUERIDO");
    }
}
