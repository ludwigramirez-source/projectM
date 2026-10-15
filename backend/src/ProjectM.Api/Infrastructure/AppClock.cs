namespace ProjectM.Api.Infrastructure;

/// <summary>
/// Reloj de la aplicación. "Hoy" se calcula en la zona horaria del equipo (App:TimeZone, IANA)
/// para que vencimientos y fechas reales coincidan con el calendario local.
/// </summary>
public class AppClock(TimeProvider time, IConfiguration config)
{
    private readonly TimeZoneInfo _zone = ResolveZone(config["App:TimeZone"]);

    public DateTimeOffset Now => time.GetUtcNow();
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), _zone).DateTime);

    private static TimeZoneInfo ResolveZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
    }
}
