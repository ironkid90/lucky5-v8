namespace Lucky5.Domain.Game.CleanRoom;

using System;

public static class CabinetVariantFactory
{
    public static ICabinetVariantEngine GetEngine(int gameId)
    {
        return gameId switch
        {
            1 => new Lucky5CabinetVariant(),
            // Future variants can be added here, e.g. 2 => new JacksOrBetterCabinetVariant()
            _ => new Lucky5CabinetVariant() // Defaulting to Lucky 5 for now
        };
    }
}
