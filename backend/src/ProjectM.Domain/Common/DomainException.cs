namespace ProjectM.Domain.Common;

public enum DomainErrorKind
{
    /// <summary>Datos inválidos para la regla de negocio (HTTP 400).</summary>
    Validation,
    /// <summary>La operación no es posible en el estado actual (HTTP 409).</summary>
    Conflict
}

/// <summary>Violación de una regla de negocio. <see cref="Code"/> es estable y lo consume el frontend.</summary>
public class DomainException(string code, string message, DomainErrorKind kind = DomainErrorKind.Conflict)
    : Exception(message)
{
    public string Code { get; } = code;
    public DomainErrorKind Kind { get; } = kind;
}
