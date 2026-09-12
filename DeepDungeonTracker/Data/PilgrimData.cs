namespace DeepDungeonTracker;

public static class PilgrimData
{
    public static bool IsMimic(uint nameId) => nameId is 14264 or 14265 or 14266;

    public static bool IsBoss(uint nameId, int floor) => floor switch
    {
        10 => nameId == 13979,
        20 => nameId == 13973,
        30 => nameId == 13863,
        40 => nameId == 13977,
        50 => nameId == 14263,
        60 => nameId == 14097,
        70 => nameId == 13971,
        80 => nameId == 13968,
        90 => nameId == 14090,
        99 => nameId is 14037 or 14038,
        _ => false
    };

    private static readonly Pomander[] Shared =
    [
        Pomander.Safety, Pomander.Sight, Pomander.Strength, Pomander.Steel,
        Pomander.Affluence, Pomander.Flight, Pomander.Alteration, Pomander.Purity,
        Pomander.Fortune, Pomander.Witching, Pomander.Serenity, Pomander.Intuition, Pomander.Raising
    ];

    public static Pomander? MapPomander(int itemId) => itemId switch
    {
        >= 1 and <= 22 => (Pomander)(itemId - 1),
        >= 23 and <= 35 => Shared[itemId - 23],
        36 => Pomander.Haste,
        37 => Pomander.Purification,
        38 => Pomander.Devotion,
        _ => null
    };
}
