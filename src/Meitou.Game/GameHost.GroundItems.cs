using System.Numerics;
using Meitou.Data.World;
using Meitou.Simulation;

namespace Meitou.Game;

/// <summary>
/// Loose items on the ground (docs/simulation.md "Ground items"): the snapshot's <see cref="WorldSnapshot.GroundItems"/> are diffed by id whenever
/// <see cref="WorldSnapshot.GroundItemsRevision"/> changes, each one a runtime placement of the world objects (its record's mesh, <c>WorldObjectLayout.Item</c>).
/// </summary>
sealed partial class GameHost
{
    long groundRevision = -1;
    readonly HashSet<int> shownGround = [];

    static string GroundId(int id) => "ground:" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Once a frame with <see cref="UpdateBuildings"/>, after a new world's runtime placements were cleared.</summary>
    void UpdateGroundItems(WorldSnapshot snapshot)
    {
        session.World.GroundItems.Limit = config.GroundItemLimit;   // the setting, applied at the next drop
        if (snapshot.GroundItemsRevision == groundRevision) return;
        groundRevision = snapshot.GroundItemsRevision;
        var current = snapshot.GroundItems.Select(g => g.Id).ToHashSet();
        foreach (int id in shownGround.Where(id => !current.Contains(id)).ToArray())
        {
            gpu.Objects?.RemoveRuntime(GroundId(id));
            shownGround.Remove(id);
        }
        foreach (var g in snapshot.GroundItems)
        {
            if (!shownGround.Add(g.Id)) continue;
            // Laid on the ground: the mesh's long axis is horizontal, its middle about half a unit above its origin's floor.
            gpu.Objects?.AddRuntime(new BuildingPlacement(WorldLayout.ZoneOf(g.Position.X, g.Position.Z), GroundId(g.Id), g.Record, g.Position,
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, g.Yaw), null, g.Position.Y + 0.5f));
        }
    }

    void ForgetGroundItems()
    {
        shownGround.Clear();
        groundRevision = -1;
    }
}
