using System.Collections.Concurrent;
using System.Collections.Generic;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;

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

    public IReadOnlyList<LobbyMachineInfo> GetLobbySnapshot()
    {
        var result = new List<LobbyMachineInfo>();
        foreach (var kvp in _machineSpectators)
        {
            var machineId = kvp.Key;
            var set = kvp.Value;
            lock (set)
            {
                result.Add(new LobbyMachineInfo(machineId, isOccupied: false, occupantUserId: null, set.Count));
            }
        }
        return result;
    }
}
