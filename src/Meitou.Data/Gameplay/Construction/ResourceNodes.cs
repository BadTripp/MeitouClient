using System.Numerics;
using Meitou.Data.World;

namespace Meitou.Data.Gameplay.Construction;

/// <summary>A mineable rock (docs/game/buildings-production.md "Resources"): where it stands, its <c>world resource mining</c>, its radius and its BUILDING record.</summary>
public readonly record struct ResourceNode(Vector3 Position, int Resource, float Radius, string Record = "", Vector3? Centre = null);

/// <summary>Where the world's mineable rocks are.</summary>
public interface IResourceNodes
{
    /// <summary>The rocks of <paramref name="resource"/> (0: of any) within <paramref name="range"/> (horizontally) of a point.</summary>
    IReadOnlyList<ResourceNode> Near(int resource, float x, float z, float range);
}

/// <summary>
/// The rocks the foliage system places (docs/formats/foliage.md, "building type"): instances whose FOLIAGE_MESH names a BUILDING, the rock's resource being that building's
/// functionality <c>world resource mining</c>. Zones are placed once and kept (through the foliage world's layout cache when it has one). Thread-safe.
/// </summary>
public sealed class FoliageResourceNodes(FoliageWorld world, GameDatabase db, Func<string, string?>? findMesh = null) : IResourceNodes, IDisposable
{
    /// <summary>The smallest radius of a rock: the original takes the rock mesh's bounds and at least 25 (<c>uses_resources</c>); without the mesh file a rock is this.</summary>
    public const float NodeRadius = 25;

    readonly Dictionary<string, (Vector3 Centre, float Radius)?> meshBounds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The bounds sphere of a rock mesh (its centre and its longest half-extent as the radius, before scaling), read from the file's header; null without <c>findMesh</c> or the file.</summary>
    (Vector3 Centre, float Radius)? Bounds(string meshPath)
    {
        if (findMesh is null) return null;
        if (meshBounds.TryGetValue(meshPath, out var known)) return known;
        (Vector3, float)? bounds = null;
        if ((findMesh(meshPath) ?? findMesh(Path.GetFileName(meshPath.Replace('\\', '/')))) is { } path && Meitou.Data.Ogre.OgreMeshReader.TryReadBounds(path, out var b) && b.Max.X >= b.Min.X)
        {
            // The longest half-extent of the bounds box, not its half-diagonal (a rock of 71 x 72 x 66 is 36 across, not 60: the diagonal made the pick sphere nearly twice the rock).
            var size = b.Max - b.Min;
            bounds = ((b.Min + b.Max) / 2, Math.Max(size.X, Math.Max(size.Y, size.Z)) / 2);
        }
        return meshBounds[meshPath] = bounds;
    }

    readonly Dictionary<ZoneCoordinate, List<ResourceNode>> zones = [];
    readonly Dictionary<string, int> resourceOf = new(StringComparer.Ordinal);
    readonly Lock gate = new();

    public IReadOnlyList<ResourceNode> Near(int resource, float x, float z, float range)
    {
        var found = new List<ResourceNode>();
        lock (gate)
        {
            var first = WorldLayout.ZoneOf(x - range, z - range);
            var last = WorldLayout.ZoneOf(x + range, z + range);
            for (int zx = first.X; zx <= last.X; zx++)
                for (int zy = first.Y; zy <= last.Y; zy++)
                    foreach (var node in Zone(new ZoneCoordinate(zx, zy)))
                        if ((resource == 0 || node.Resource == resource) && Vector2.DistanceSquared(new(node.Position.X, node.Position.Z), new(x, z)) <= range * range) found.Add(node);
        }
        return found;
    }

    List<ResourceNode> Zone(ZoneCoordinate zone)
    {
        if (zones.TryGetValue(zone, out var list)) return list;
        list = [];
        if (zone.IsInsideGrid)
            foreach (var instance in world.Zone(zone).Instances)
                if (instance.Mesh.BuildingType is { } building && ResourceOf(building) is > 0 and var resource)
                {
                    // The rock's size is its mesh's bounds scaled by the instance (at least NodeRadius); its sphere is centred on the bounds' middle, not on the origin the mesh sits on.
                    var bounds = Bounds(instance.Mesh.MeshPath);
                    float radius = bounds is { } b ? Math.Max(NodeRadius, b.Radius * instance.Scale) : NodeRadius;
                    list.Add(new ResourceNode(instance.Position, resource, radius, building.StringId, bounds is { } c ? Vector3.Transform(c.Centre, instance.Transform) : null));
                }
        zones[zone] = list;
        return list;
    }

    int ResourceOf(GameRecord building)
    {
        if (resourceOf.TryGetValue(building.StringId, out int r)) return r;
        r = building.GetReferences("functionality") is [var link, ..] && db.Find(link.TargetStringId) is { } function ? function.GetInt("world resource mining") : 0;
        return resourceOf[building.StringId] = r;
    }

    public void Dispose() => world.Dispose();
}
