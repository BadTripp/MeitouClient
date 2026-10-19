using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation.Bodies;

namespace Meitou.Simulation;

/// <summary>
/// The bodies of the characters (stage 7, docs/game/character-stats.md): every tick it runs each living character's <see cref="MedicalState.Tick"/> (blood,
/// bleeding, part healing, hunger, the knock-out timer), sets the top speed from the speed chain (<see cref="Speed.Run"/>: athletics, legs, hunger,
/// encumbrance) and stops a character that has just been knocked out or has died. It runs in the Act phase, before the animation system, and writes only its own
/// slots. A corpse is removed <see cref="CorpseHours"/> game hours after it died (an engine choice; the original's rule is <b>Unknown</b>).
/// </summary>
/// <remarks>
/// One tick is 1/30 s of game time at speed 1, which is <see cref="HoursPerTick"/> game hours; the medical rates are per game hour
/// (character-stats.md). <see cref="BodyTimeScale"/> multiplies the time of the part and blood rates (not hunger or the knock-out timer), see
/// <see cref="MedicalContext.BodyTimeScale"/>.
/// </remarks>
public sealed class BodySystem(GameConstants constants, BodyOptions? options = null, float bodyTimeScale = 1) : ITickSystem
{
    /// <summary>Game hours in one tick: 11/36000 h is 1/30 game s at the Verified 1200/11 s per game hour (<c>GameClock</c>). Whether the original's medical dt is in game hours is Unknown (that is what <see cref="BodyTimeScale"/> covers).</summary>
    public const float HoursPerTick = 11f / 36000;
    /// <summary>How long a corpse lies before it is removed, in game hours.</summary>
    public const float CorpseHours = 12;
    public const long CorpseTicks = (long)(CorpseHours / HoursPerTick);

    public GameConstants Constants { get; } = constants;
    public BodyOptions Options { get; } = options ?? BodyOptions.Default;
    /// <summary>The <see cref="MedicalContext.BodyTimeScale"/> every character gets (default 1: the documented rates).</summary>
    public float BodyTimeScale { get; } = bodyTimeScale;
    readonly XpService xp = new(constants, options ?? BodyOptions.Default);

    public void Act(World world, Partition part, EffectBuffer effects)
    {
        var table = world.Characters;
        var prev = table.Previous;
        var next = table.Next;
        List<MedicalEvent>? events = null;
        for (int i = part.Start; i < part.End; i++)
        {
            if (!prev[i].Alive) continue;
            var cold = table.Cold(i)!;
            if (cold.Medical is not { } medical || cold.Race is not { } race || cold.Stats is not { } stats) continue;
            ref var n = ref next[i];
            if (medical.Dead && cold.DiedTick < 0) cold.DiedTick = world.Tick;   // also when something outside the tick killed it (a hit)
            if (medical.Incapacitated && (n.Task != (byte)CharacterTask.Idle || (n.Flags & (ushort)MoveFlags.AnyPath) != 0))
            {
                MovementSystem.Stop(table, i);
                n.Velocity = Vector3.Zero;
            }
            if (medical.Dead) continue;
            // The load: the inventory weight, plus carry person weight while carrying someone, against the strength the injuries leave (character-stats.md "Encumbrance and carrying").
            float load = cold.Inventory?.ContentWeight() ?? 0;
            bool carrying = !cold.Carrying.IsNone;
            float encumbrance = load <= 0 && !carrying ? 1 : Encumbrance.Factor(Constants, load, carrying, stats.Strength, medical.StatMultiplier(Meitou.Data.Gameplay.Bodies.StatsEnumerated.Strength));
            var ctx = new MedicalContext(Constants, Options, race)
            {
                EncumbranceFactor = encumbrance,
                Toughness = stats.Toughness,
                Strength = stats.Strength,
                Seed = world.Seed,
                CharacterKey = Rng.Key(table.IdOf(i)),
                BodyTimeScale = BodyTimeScale,
                // Lying in a bed of the "Sleep when injured" option rests too.
                Resting = n.Task == (byte)CharacterTask.Rest || n.Task == (byte)CharacterTask.Operate && cold.Implicit == Meitou.Data.Gameplay.Ai.TaskType.GO_HOME_AND_GO_TO_BED && cold.Operating,
            };
            // The combat system's own context (hits) reads the encumbrance from the fighter, so it matches the one used here.
            if (cold.Fighter is { } fighter) fighter.EncumbranceFactor = encumbrance;
            events ??= [];
            events.Clear();
            medical.Tick(HoursPerTick, ctx, events);
            n.MaxSpeed = Speed.Run(race, stats, medical, encumbrance);
            cold.SwimSpeed = Speed.Swim(race, stats, medical, encumbrance);
            TrainOnTheMove(stats, race, medical, in n, cold.WaterState == 3, carrying, encumbrance, world.TickSeconds);
            if ((cold.Orders & CharacterOrders.Sneak) != 0 && !medical.Incapacitated)
            {
                // An exposure older than a second (a looker's scan interval with room to spare) has lapsed.
                var exposure = cold.StealthExposureTick >= world.Tick - (long)MathF.Ceiling(1 / world.TickSeconds) ? cold.StealthExposure : StealthExposure.Unobserved;
                xp.Sneaking(stats, race, exposure, world.TickSeconds);
            }
            if (cold.IsPlayer || table.IdOf(i) == world.Player.Inspected) cold.Extended = ExtendedOf(stats, race, medical, n.MaxSpeed, encumbrance, ctx.Resting);
            n.Health = Math.Clamp(medical.Blood / MathF.Max(MedicalState.BloodCapacity(race, stats.Strength), 1) * 100, 0, 100);
            foreach (var e in events)
            {
                if (e.Kind is MedicalEventKind.Died or MedicalEventKind.KnockedOut)
                {
                    MovementSystem.Stop(table, i);
                    n.Velocity = Vector3.Zero;
                }
            }
        }
    }

    /// <summary>
    /// The numbers behind the information panel's extended lines (<c>FUN_140895350</c>, docs/game/ui-screens.md section 4). Toughness XP: 1, or 2 with the left arm crippled, plus 2
    /// for the right arm and 2 for blood-loss KO (the original adds a fourth term for a counter at <c>+0xa4</c> under 15, <b>Unknown</b>, left out). Strength XP is the carrying rate;
    /// what the original shows instead with a weapon in hand ("Weapon weight") is not modelled.
    /// </summary>
    ExtendedStats ExtendedOf(CharacterStats stats, RaceData race, MedicalState medical, float speed, float encumbrance, bool resting)
    {
        float runValue = Speed.RunValue(race, stats.Athletics);
        var arms = medical.Aggregates();
        float toughness = (arms.LeftArmDisabled ? 2 : 1) + (arms.RightArmDisabled ? 2 : 0) + (medical.BloodKo ? 2 : 0);
        float hunger = race.HungerRate * MedicalState.HungerSituationFactor(Constants, resting || medical.Unconscious || medical.Dead, false, encumbrance);
        return new ExtendedStats(XpService.AthleticsRate(encumbrance, speed, runValue), xp.StrengthCarryRate(encumbrance), toughness,
            MedicalState.KoPoint(Constants, stats.Toughness), speed, runValue, hunger);
    }

    /// <summary>
    /// XP outside combat (character-stats.md "Levelling"; the movement tick <c>FUN_1408c6660</c>): athletics while running, strength while carrying a load. A character counts as
    /// running when it moves faster than its race walk speed (+1); whether walking trains too is <b>Unknown</b> (the doc names running). In deep water (<paramref name="deepWater"/>)
    /// swimming trains instead of athletics (<b>Verified</b>, <c>FUN_1408c6660</c>). Strength rises only when the load
    /// encumbers it, and only while it moves. The speed the stat sees is the velocity the movement system left this tick.
    /// </summary>
    void TrainOnTheMove(CharacterStats stats, RaceData race, MedicalState medical, in CharacterHot n, bool deepWater, bool carrying, float encumbrance, float dt)
    {
        if (medical.Incapacitated) return;
        float speed = MathF.Sqrt(n.Velocity.X * n.Velocity.X + n.Velocity.Z * n.Velocity.Z);
        if (speed < 1) return;
        if (deepWater) xp.SwimmingInWater(stats, race, carrying, dt);
        else if (speed > n.WalkSpeed + 1) xp.AthleticsFromRunning(stats, race, encumbrance, speed / MathF.Max(n.MaxSpeed, 1), dt);
        if (encumbrance < 1) xp.StrengthFromCarrying(stats, race, encumbrance, dt);
    }

    public void SlowWorld(World world)
    {
        var table = world.Characters;
        for (int i = 0; i < table.HighWater; i++)
        {
            if (table.Next[i].Alive && table.Cold(i) is { DiedTick: >= 0 } cold && world.Tick - cold.DiedTick >= CorpseTicks)
            {
                var corpse = new CharacterId(i, table.Next[i].Generation);
                world.Uniques.Left(corpse);
                table.Remove(corpse);
            }
        }
    }
}
