namespace Lucky5.Infrastructure.Services;

using Lucky5.Application.Contracts;
using Lucky5.Domain.Entities;
using System.Collections.Concurrent;

public sealed class StubNotificationService : INotificationService
{
    private readonly ConcurrentDictionary<Guid, List<DeviceToken>> _tokens = new();

    public Task RegisterDeviceAsync(Guid userId, string token, string platform, CancellationToken cancellationToken)
    {
        var list = _tokens.GetOrAdd(userId, _ => []);
        if (!list.Any(t => t.Token == token))
        {
            list.Add(new DeviceToken { UserId = userId, Token = token, Platform = platform });
        }
        return Task.CompletedTask;
    }

    public Task SendToUserAsync(Guid userId, string title, string body, CancellationToken cancellationToken)
    {
        // Stub: log to console. Replace with Firebase Admin SDK when configured.
        Console.WriteLine($"[PUSH→User:{userId}] {title}: {body}");
        return Task.CompletedTask;
    }

    public Task SendToTopicAsync(string topic, string title, string body, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[PUSH→Topic:{topic}] {title}: {body}");
        return Task.CompletedTask;
    }
}
