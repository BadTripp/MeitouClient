using Meitou.Content;

namespace Meitou.Data;

/// <summary>
/// The game-data files to load, in order, as the game picks them (docs/formats/overview.md, "Load order"):
/// the core files, then the mods listed in <c>data/mods.cfg</c>.
/// </summary>
public sealed class LoadOrder
{
    /// <summary>Core files, always first and in this order (a fixed list in the game; missing ones are skipped).</summary>
    public static readonly IReadOnlyList<string> BaseFiles = ["gamedata.base", "Newwworld.mod", "Dialogue.mod", "rebirth.mod"];

    public List<LoadOrderEntry> Entries { get; } = [];

    public List<GameDataIssue> Issues { get; } = [];

    /// <summary>Only the core files, without the mods in <c>mods.cfg</c>: the unmodded base game.</summary>
    public static LoadOrder BaseGame(GameInstall install)
    {
        var order = FromInstall(install);
        order.Entries.RemoveAll(e => !BaseFiles.Contains(e.Name));
        order.Issues.Clear();
        return order;
    }

    public static LoadOrder FromInstall(GameInstall install)
    {
        var order = new LoadOrder();
        foreach (var name in BaseFiles)
        {
            var path = Path.Combine(install.DataDirectory, name);
            if (File.Exists(path))
                order.Entries.Add(new LoadOrderEntry(name, path));
        }

        var modsCfg = Path.Combine(install.DataDirectory, "mods.cfg");
        if (!File.Exists(modsCfg))
            return order;
        var available = FindMods(install);
        foreach (var name in ParseModsCfg(File.ReadAllLines(modsCfg)))
        {
            if (order.Entries.Any(e => e.Name == name + ".mod"))
                continue;
            if (available.TryGetValue(name, out var path))
                order.Entries.Add(new LoadOrderEntry(name + ".mod", path));
            else
                order.Issues.Add(new GameDataIssue(GameDataIssueKind.MissingFile, "mods.cfg", null, $"mod '{name}' not found"));
        }
        return order;
    }

    /// <summary>Mod names from <c>mods.cfg</c> with the game's rules: <see cref="ModList.ParseModsCfg"/>.</summary>
    public static IEnumerable<string> ParseModsCfg(IEnumerable<string> lines) => ModList.ParseModsCfg(lines);

    /// <summary>Mods on disk by name: <see cref="ModList.FindMods"/>.</summary>
    public static Dictionary<string, string> FindMods(GameInstall install) => ModList.FindMods(install);
}

public sealed record LoadOrderEntry(string Name, string Path);
