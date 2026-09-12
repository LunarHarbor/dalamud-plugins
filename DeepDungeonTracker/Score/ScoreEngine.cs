using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepDungeonTracker;

public sealed record ScoreFloor(int Number, bool Cleared, bool BossDefeated, int Kills,
    int Mimics, int Mandragoras, int NPCs, int DreadBeasts, int Coffers,
    int Enchantments, int Traps, int Deaths, bool Map, int LocalDeaths = 0, int Candles = 0, int IncenseUses = 0);

public sealed record ScoreSet(int PartySize, bool Completed, bool TimeBonus, ScoreFloor[] Floors);

public sealed record ScoreRun(DeepDungeon Dungeon, int Level, int Arm, int Armor, int KOs,
    ScoreSet[] Sets, int SchemaVersion = 2);

public sealed record ScoreBreakdown(int Character, int Floors, int Kills, int Maps, int Coffers,
    int Mimics, int NPCs, int DreadBeasts, int Enchantments, int Traps, int Deaths,
    int Time, int Bosses, int Party, int Completion, int BonusUnits, int Multiplier, int Bonus)
{
    public int Total => checked(Character + Floors + Kills + Bonus);
}

public static class ScoreEngine
{
    public const string RulesVersion = "community-7.5-v1";
    public const string PilgrimRulesVersion = "pilgrim-eo-candidate-v1";

    public static int MaximumFloor(DeepDungeon dungeon) => dungeon == DeepDungeon.PalaceOfTheDead ? 200 : 100;
    public static int ChallengeStarts(DeepDungeon dungeon) => dungeon == DeepDungeon.PalaceOfTheDead ? 101 : 31;
    public static int MaximumLevel(DeepDungeon dungeon) => dungeon switch
    {
        DeepDungeon.PalaceOfTheDead => 60, DeepDungeon.HeavenOnHigh => 70,
        DeepDungeon.EurekaOrthos => 90, DeepDungeon.PilgrimsTraverse => 100, _ => 0,
    };
    public static int PartyBonus(int size) => size switch
    {
        1 => 200, 2 => 100, 3 => 50, 4 => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(size)),
    };

    public static ScoreBreakdown Calculate(ScoreRun run, int? projectedFloor = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Dungeon == DeepDungeon.None || !Enum.IsDefined(run.Dungeon))
            throw new ArgumentException("A supported dungeon is required.", nameof(run));
        if (run.Sets == null || run.Sets.Any(s => s == null || s.Floors == null || s.Floors.Any(f => f == null)))
            throw new ArgumentException("Missing set or floor data.", nameof(run));
        var floors = run.Sets.SelectMany(s => s.Floors).ToArray();
        if (floors.Length == 0)
            throw new ArgumentException("No floors have been captured.", nameof(run));
        if (run.Level < 1 || run.Level > MaximumLevel(run.Dungeon) || run.Arm is < 0 or > 99 ||
            run.Armor is < 0 or > 99 || run.KOs is < 0 or > 99)
            throw new ArgumentException("Invalid character or failure totals.", nameof(run));
        var start = floors[0].Number;
        var last = floors[^1].Number;
        if (start < 1 || last > MaximumFloor(run.Dungeon) ||
            floors.Where((f, i) => f.Number != start + i).Any())
            throw new ArgumentException("Captured floors must be consecutive.", nameof(run));
        foreach (var set in run.Sets)
        {
            _ = PartyBonus(set.PartySize);
            if (set.Floors.Length == 0 || (set.Completed && set.Floors.Any(f => !f.Cleared)))
                throw new ArgumentException("A completed set must contain cleared floors.", nameof(run));
            if (set.Floors.Any(f => (f.Number - 1) / 10 != (set.Floors[0].Number - 1) / 10) ||
                (set.Completed && set.Floors[^1].Number % 10 != 0))
                throw new ArgumentException("Floor sets must remain within their ten-floor boundary and complete on its last floor.", nameof(run));
            if (set.Floors.Any(f => f.BossDefeated && !IsBossFloor(run.Dungeon, f.Number)))
                throw new ArgumentException("A boss defeat must belong to a boss floor.", nameof(run));
            foreach (var f in set.Floors)
                if (new[] { f.Kills, f.Mimics, f.Mandragoras, f.NPCs, f.DreadBeasts,
                    f.Coffers, f.Enchantments, f.Traps, f.Deaths, f.LocalDeaths, f.Candles, f.IncenseUses }.Any(n => n < 0) ||
                    (long)f.Mimics + f.Mandragoras + f.NPCs + f.DreadBeasts > f.Kills || f.LocalDeaths > f.Deaths)
                    throw new ArgumentException("Invalid event counts.", nameof(run));
        }
        checked
        {
            var reached = projectedFloor ?? last;
            if (reached < last || reached > MaximumFloor(run.Dungeon))
                throw new ArgumentOutOfRangeException(nameof(projectedFloor));
            var distance = reached - start;
            var r = distance + 1;
            var x = 101 - 10 * run.KOs;
            // HoH/EO/PT odd-floor weighting is provisional.
            var killRate = 100 + r / 2 * (run.Dungeon == DeepDungeon.PalaceOfTheDead ? 1 : 2);
            var character = (run.Arm + run.Armor) * 10 + run.Level * 500;
            var floorScore = distance * (3000 + (run.Arm + run.Armor) * 10);
            var kills = floors.Sum(f => f.Kills) * killRate;
            var deepKills = floors.Where(f => f.Number >= ChallengeStarts(run.Dungeon))
                .Sum(f => IsBossFloor(run.Dungeon, f.Number) ? 0 : f.Kills - f.Mimics - f.Mandragoras - f.NPCs - f.DreadBeasts);
            var maps = floors.Count(f => f.Cleared && f.Map && !IsBossFloor(run.Dungeon, f.Number)) * 25;
            var coffers = floors.Sum(f => f.Coffers);
            var mimics = floors.Sum(f => f.Mimics + f.Mandragoras) * 5;
            var npcs = floors.Sum(f => f.NPCs) * 20;
            var dread = floors.Sum(f => f.DreadBeasts) * 5;
            var enchantments = floors.Where(f => f.Cleared).Sum(f => f.Enchantments) * 5;
            var traps = -floors.Sum(f => f.Traps) * 2;
            var deaths = -floors.Sum(f => f.Deaths) * 50;
            var time = run.Sets.Count(s => s.Completed && s.TimeBonus) * 150;
            var party = run.Sets.Where(s => s.Completed).Sum(s => PartyBonus(s.PartySize));
            var bosses = floors.Where(f => f.BossDefeated).Sum(f => BossBonus(run.Dungeon, f.Number));
            var completion = last == MaximumFloor(run.Dungeon) && run.Sets[^1].Completed ? 3000 : 0;
            var units = deepKills + maps + coffers + mimics + npcs + dread + enchantments + traps + deaths + time + party + bosses + completion;
            // Clamp after summing penalties.
            var bonus = units > 0 ? units * x : -10 * run.KOs;
            return new(character, floorScore, kills, maps * x, coffers * x, mimics * x, npcs * x,
                dread * x, enchantments * x, traps * x, deaths * x, time * x, bosses * x,
                party * x, completion * x, units, x, bonus);
        }
    }

    public static bool IsBossFloor(DeepDungeon dungeon, int floor) => floor % 10 == 0 ||
        (floor == 99 && dungeon is DeepDungeon.EurekaOrthos or DeepDungeon.PilgrimsTraverse);

    public static int BossBonus(DeepDungeon dungeon, int floor)
    {
        if (dungeon == DeepDungeon.PalaceOfTheDead)
            return floor == 200 ? 0 : floor is 50 or 100 ? 500 : 50;
        // PT floor 99 uses the provisional EO weight.
        return floor == 100 ? 0 : floor == 30 || (floor == 99 && dungeon is DeepDungeon.EurekaOrthos or DeepDungeon.PilgrimsTraverse) ? 500 : 50;
    }
}
