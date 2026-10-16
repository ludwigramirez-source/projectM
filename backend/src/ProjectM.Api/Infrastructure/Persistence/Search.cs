namespace ProjectM.Api.Infrastructure.Persistence;

/// <summary>Búsqueda de texto insensible a mayúsculas (ILIKE en PostgreSQL).</summary>
public static class Search
{
    /// <summary>Patrón "%texto%" con los comodines del usuario escapados.</summary>
    public static string Contains(string term) =>
        "%" + term.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
}
