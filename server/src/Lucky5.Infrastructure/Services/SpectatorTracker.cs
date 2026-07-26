using System.Collections.Concurrent;
using System.Collections.Generic;
using Lucky5.Application.Contracts;

namespace Lucky5.Infrastructure.Services;

public class SpectatorTracker : ISpectatorTracker
{
    private readonly ConcurrentDictionary<int, HashSet<string>> _machineSpectators = new();

    public void AddSpectator(int machineId, string connectionId)
    {
        _machineSpectators.AddOrUpdate(machineId,
            _ => new HashSet<string> { connectionId },
            (_, set) => { lock (set) { set.Add(connectionId); } return set; });
    }

    public void RemoveSpectator(int machineId, string connectionId)
    {
        if (_machineSpectators.TryGetValue(machineId, out var set))
        {
            lock (set) { set.Remove(connectionId); }
        }
    }

    public int GetSpectatorCount(int machineId)
    {
        if (_machineSpectators.TryGetValue(machineId, out var set))
        {
            lock (set) { return set.Count; }
        }
        return 0;
    }
}
