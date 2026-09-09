namespace Lucky5.Domain.Game.CleanRoom;

public interface IDeckManager
{
    CleanRoomCard[] BuildDeck(EngineConfig config);
    CleanRoomCard[] ApplyPressureModifications(CleanRoomCard[] deck, PolicyDistributionMode mode, ulong entropySeed, int consecutiveLosses, EngineConfig config);
    CleanRoomCard[] AlterDeck(CleanRoomCard[] deck, PolicyDistributionMode mode, ulong entropySeed, int consecutiveLosses, EngineConfig config);
}

public class DeckManager : IDeckManager
{
    public CleanRoomCard[] BuildDeck(EngineConfig config)
    {
        return FiveCardDrawEngine.BuildStandardDeck();
    }

    public CleanRoomCard[] ApplyPressureModifications(CleanRoomCard[] deck, PolicyDistributionMode mode, ulong entropySeed, int consecutiveLosses, EngineConfig config)
    {
        return MachinePolicy.AlterDeck(deck, mode, entropySeed, consecutiveLosses, config);
    }

    public CleanRoomCard[] AlterDeck(CleanRoomCard[] deck, PolicyDistributionMode mode, ulong entropySeed, int consecutiveLosses, EngineConfig config)
    {
        return MachinePolicy.AlterDeck(deck, mode, entropySeed, consecutiveLosses, config);
    }
}