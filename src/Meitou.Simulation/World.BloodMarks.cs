using System.Numerics;

namespace Meitou.Simulation;

/// <summary>What left blood on the ground (docs/game/blood-decals.md); the host turns it into terrain decals.</summary>
public enum BloodMarkKind : byte
{
    /// <summary>A bleeding character stands still: its pool grows. <see cref="BloodMark.Amount"/> is the bleeding.</summary>
    Pool,
    /// <summary>A bleeding character walks on: a smear along <see cref="BloodMark.Direction"/>, as opaque as <see cref="BloodMark.Amount"/>.</summary>
    Smear,
    /// <summary>A bleeding character is carried: a drop of <see cref="BloodMark.Amount"/> in size.</summary>
    Drip,
    /// <summary>A hit drew blood: a splash of <see cref="BloodMark.Amount"/> in size, flung along <see cref="BloodMark.Direction"/>.</summary>
    Splash,
}

/// <summary>One mark for the host, display only like <see cref="SoundEvent"/>: nothing in the simulation reads it.</summary>
/// <param name="Direction">X/Z: the way a smear runs or a splash flies.</param>
/// <param name="Colour">The race's <c>blood colour</c> (0xRRGGBB).</param>
public readonly record struct BloodMark(BloodMarkKind Kind, CharacterId Who, Vector3 Position, Vector2 Direction, float Amount, uint Colour);

public sealed partial class World
{
    volatile System.Collections.Concurrent.ConcurrentQueue<BloodMark>? bloodMarks;

    /// <summary>
    /// Where the bodies and combat queue the blood they leave on the ground; null (the default) records none, which is also the game's <c>Blood</c> setting off.
    /// The host sets it and empties it every frame. Nothing in the simulation reads it, so it cannot change a run.
    /// </summary>
    public System.Collections.Concurrent.ConcurrentQueue<BloodMark>? BloodMarks { get => bloodMarks; set => bloodMarks = value; }
}
