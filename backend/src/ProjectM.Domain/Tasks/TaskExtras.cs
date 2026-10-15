using System.Globalization;
using ProjectM.Domain.Common;
using ProjectM.Domain.Users;

namespace ProjectM.Domain.Tasks;

/// <summary>Etiqueta global reutilizable entre proyectos.</summary>
public class Tag : Entity
{
    public string Name { get; set; } = "";
    /// <summary>Tono de la paleta (primary, secondary, accent, success, danger, neutral).</summary>
    public string Color { get; set; } = "secondary";
}

public class TaskTag
{
    public Guid TaskId { get; set; }
    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}

public enum CustomFieldType
{
    Texto,
    Numero,
    Fecha,
    Seleccion
}

/// <summary>Definición de un campo personalizado a nivel de proyecto.</summary>
public class CustomFieldDefinition : Entity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public CustomFieldType Type { get; set; }
    /// <summary>Opciones válidas para el tipo Selección.</summary>
    public List<string> Options { get; set; } = [];
    public bool IsRequired { get; set; }
    public int Position { get; set; }

    /// <summary>
    /// Valida y normaliza un valor recibido como texto. Devuelve null si el valor está vacío.
    /// Un campo obligatorio no puede vaciarse (<paramref name="enforceRequired"/>) y se exige al completar la tarea.
    /// </summary>
    public CustomFieldValue? Parse(Guid taskId, string? raw, bool enforceRequired = true)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            if (IsRequired && enforceRequired)
                throw new DomainException("CAMPO_REQUERIDO", $"El campo «{Name}» es obligatorio.", DomainErrorKind.Validation);
            return null;
        }

        var v = new CustomFieldValue { TaskId = taskId, FieldId = Id };
        switch (Type)
        {
            case CustomFieldType.Texto:
                if (value.Length > 1000) throw Invalid("admite máximo 1000 caracteres");
                v.TextValue = value;
                break;
            case CustomFieldType.Numero:
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n))
                    throw Invalid("debe ser un número");
                v.NumberValue = n;
                break;
            case CustomFieldType.Fecha:
                if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    throw Invalid("debe ser una fecha (aaaa-mm-dd)");
                v.DateValue = d;
                break;
            case CustomFieldType.Seleccion:
                if (!Options.Contains(value)) throw Invalid("debe ser una de las opciones definidas");
                v.TextValue = value;
                break;
        }
        return v;
    }

    private DomainException Invalid(string why) =>
        new("CAMPO_INVALIDO", $"El campo «{Name}» {why}.", DomainErrorKind.Validation);
}

public class CustomFieldValue
{
    public Guid TaskId { get; set; }
    public Guid FieldId { get; set; }
    public CustomFieldDefinition Field { get; set; } = null!;
    public string? TextValue { get; set; }
    public decimal? NumberValue { get; set; }
    public DateOnly? DateValue { get; set; }

    /// <summary>Representación canónica en texto (para la API).</summary>
    public string? AsString() =>
        TextValue
        ?? NumberValue?.ToString("0.############", CultureInfo.InvariantCulture)
        ?? DateValue?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

public class Attachment : Entity
{
    public Guid TaskId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string StorageKey { get; set; } = "";
    public Guid UploadedById { get; set; }
    public AppUser UploadedBy { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
