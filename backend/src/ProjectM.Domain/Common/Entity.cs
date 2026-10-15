namespace ProjectM.Domain.Common;

/// <summary>Evento de dominio: se despacha después de confirmar la transacción (actividad, notificaciones).</summary>
public interface IDomainEvent;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    private readonly List<IDomainEvent> _events = [];
    public IReadOnlyList<IDomainEvent> DomainEvents => _events;
    public void Raise(IDomainEvent e) => _events.Add(e);
    public void ClearDomainEvents() => _events.Clear();
}

public static class Guard
{
    public static string Required(string? value, string field, int max)
    {
        var v = value?.Trim() ?? "";
        if (v.Length == 0) throw new DomainException("VALIDACION", $"{field} es obligatorio.", DomainErrorKind.Validation);
        if (v.Length > max) throw new DomainException("VALIDACION", $"{field} admite máximo {max} caracteres.", DomainErrorKind.Validation);
        return v;
    }

    public static string? Optional(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length > max) throw new DomainException("VALIDACION", $"{field} admite máximo {max} caracteres.", DomainErrorKind.Validation);
        return v;
    }

    public static void DateRange(DateOnly? start, DateOnly? end, string what = "La fecha de fin")
    {
        if (start is not null && end is not null && end < start)
            throw new DomainException("RANGO_FECHAS", $"{what} no puede ser anterior a la de inicio.", DomainErrorKind.Validation);
    }
}
