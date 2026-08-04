namespace Lucky5.Realtime.Services;

using System.Collections.Concurrent;

public sealed class ConnectionRegistry
{
    private readonly ConcurrentDictionary<string, ConnectionEntry> _connections = new();

    public void Add(string connectionId, Guid userId)
    {
        _connections[connectionId] = new ConnectionEntry(userId, DateTime.UtcNow);
    }

    public void Remove(string connectionId)
    {
        _connections.TryRemove(connectionId, out _);
    }

    public void Touch(string connectionId)
    {
        if (_connections.TryGetValue(connectionId, out var value))
        {
            _connections[connectionId] = value with { LastHeartbeatUtc = DateTime.UtcNow };
        }
    }

    public bool TryGetUserId(string connectionId, out Guid userId)
    {
        if (_connections.TryGetValue(connectionId, out var entry))
        {
            userId = entry.UserId;
            return true;
        }
        userId = Guid.Empty;
        return false;
    }

    public IReadOnlyList<string> GetStaleConnections(TimeSpan maxAge)
    {
        var threshold = DateTime.UtcNow - maxAge;
        return _connections
            .Where(x => x.Value.LastHeartbeatUtc < threshold)
            .Select(x => x.Key)
            .ToArray();
    }

    private sealed record ConnectionEntry(Guid UserId, DateTime LastHeartbeatUtc);
}
