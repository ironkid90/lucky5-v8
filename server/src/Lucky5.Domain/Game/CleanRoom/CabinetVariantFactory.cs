namespace Lucky5.Domain.Game.CleanRoom;

using System;

public static class CabinetVariantFactory
{
    public static ICabinetVariantEngine GetEngine(int gameId)
    {
        return gameId switch
        {
            1 => new Lucky5CabinetVariant(),
            2 => new WildWitchCabinetVariant(), // Wild Witch (Video Klein) variant
            // Future variants can be added here, e.g. 3 => new JacksOrBetterCabinetVariant()
            _ => new Lucky5CabinetVariant() // Defaulting to Lucky 5 for now
        };
    }
}
