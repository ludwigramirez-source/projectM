using ProjectM.Domain.Common;

namespace ProjectM.Domain.Projects;

/// <summary>Agrupador de proyectos (área, departamento, línea de negocio).</summary>
public class Space : Entity
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Nombre de tono de la paleta (primary, secondary, accent, success, danger).</summary>
    public string Color { get; set; } = "primary";
    public int Position { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<Project> Projects { get; set; } = [];
}

public class Client : Entity
{
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
