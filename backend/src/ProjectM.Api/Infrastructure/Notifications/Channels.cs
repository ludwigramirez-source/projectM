using Microsoft.AspNetCore.SignalR;
using ProjectM.Api.Infrastructure.Realtime;

namespace ProjectM.Api.Infrastructure.Notifications;

/// <summary>Notificación ya persistida, lista para entregarse por un canal.</summary>
public record NotificationMessage(
    Guid Id,
    Guid UserId,
    string Type,
    string Title,
    string? Body,
    string? Link,
    Guid? ProjectId,
    Guid? TaskId,
    DateTimeOffset CreatedAt);

/// <summary>
/// Canal de entrega de notificaciones. v1: tiempo real dentro de la app (SignalR).
/// Para agregar correo o WhatsApp basta con registrar otra implementación en DI
/// (y, si aplica, consultar las preferencias del usuario dentro del canal).
/// </summary>
public interface INotificationChannel
{
    string Name { get; }
    Task SendAsync(NotificationMessage message, CancellationToken ct);
}

public class SignalRNotificationChannel(IHubContext<NotificationsHub> hub) : INotificationChannel
{
    public string Name => "in-app";

    public Task SendAsync(NotificationMessage m, CancellationToken ct) =>
        hub.Clients.Group(NotificationsHub.UserGroup(m.UserId)).SendAsync("notification", m, ct);
}

/// <summary>Entrega a todos los canales registrados; un canal que falla no afecta a los demás.</summary>
public class NotificationSender(IEnumerable<INotificationChannel> channels, ILogger<NotificationSender> logger)
{
    public async Task SendAsync(IEnumerable<NotificationMessage> messages, CancellationToken ct)
    {
        foreach (var m in messages)
        foreach (var channel in channels)
        {
            try { await channel.SendAsync(m, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Fallo al entregar notificación {Id} por {Channel}", m.Id, channel.Name); }
        }
    }
}
