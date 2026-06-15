namespace Lucky5.Application.Contracts;

public interface INotificationService
{
    Task RegisterDeviceAsync(Guid userId, string token, string platform, CancellationToken cancellationToken);
    Task SendToUserAsync(Guid userId, string title, string body, CancellationToken cancellationToken);
    Task SendToTopicAsync(string topic, string title, string body, CancellationToken cancellationToken);
}
