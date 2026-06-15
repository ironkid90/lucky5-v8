namespace Lucky5.Domain.Game;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using Lucky5.Domain.Entities;

public sealed class RoundNoiseRng
{
    private ulong _state;

    public RoundNoiseRng(ulong seed)
    {
        _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
    }

    public ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 1)
        {
            return 0;
        }

        return (int)(NextUInt64() % (ulong)maxExclusive);
    }

    public double NextUnit()
    {
        return (NextUInt64() >> 11) * (1.0 / (1UL << 53));
    }

    public void Shuffle<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = NextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public static ulong CreateEntropySeed(Guid userId, int machineId, decimal betAmount, MachineLedgerState ledger)
    {
        Span<byte> cryptoBytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(cryptoBytes);
        var cryptoSeed = BinaryPrimitives.ReadUInt64LittleEndian(cryptoBytes);

        var guidBytes = userId.ToByteArray();
        var guidHead = BinaryPrimitives.ReadUInt64LittleEndian(guidBytes.AsSpan(0, 8));
        var guidTail = BinaryPrimitives.ReadUInt64LittleEndian(guidBytes.AsSpan(8, 8));

        var betBits = DecimalToEntropyBits(betAmount);

        var timingNoise = (ulong)DateTime.UtcNow.Ticks ^ (ulong)Stopwatch.GetTimestamp();
        var ledgerNoise = Mix(
            ((ulong)(uint)ledger.RoundCount << 32) ^ (uint)machineId,
            DecimalToEntropyBits(ledger.CapitalIn),
            DecimalToEntropyBits(ledger.CapitalOut),
            DecimalToEntropyBits(ledger.BaseCapitalOut),
            DecimalToEntropyBits(ledger.JackpotCapitalOut),
            DecimalToEntropyBits(ledger.DoubleUpCapitalOut),
            DecimalToEntropyBits(ledger.NetSinceLastClose));

        return Mix(cryptoSeed, guidHead, guidTail, betBits, timingNoise, ledgerNoise);
    }

    private static ulong DecimalToEntropyBits(decimal value)
    {
        var bits = decimal.GetBits(value);
        return Mix(
            ((ulong)(uint)bits[0] << 32) ^ (uint)bits[1],
            ((ulong)(uint)bits[2] << 32) ^ (uint)bits[3]);
    }

    public static ulong Mix(params ulong[] values)
    {
        var seed = 0xD2B74407B1CE6E93UL;
        foreach (var value in values)
        {
            seed ^= value + 0x9E3779B97F4A7C15UL + (seed << 6) + (seed >> 2);
        }

        return seed;
    }
}
