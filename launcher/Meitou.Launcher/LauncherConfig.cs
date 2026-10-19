using System.Text.Json;

namespace Meitou.Launcher;

/// <summary>The launcher's settings and mod presets, in <c>launcher.json</c> in <see cref="DataDirectory"/>.</summary>
public sealed class LauncherConfig
{
    /// <summary>
    /// The launcher's own folder: <c>%LocalAppData%/Meitou</c> on Windows, <c>~/.local/share/Meitou</c> on Linux,
    /// <c>~/Library/Application Support/Meitou</c> on macOS. Holds the installed versions and is the game's working directory, so its
    /// <c>meitou.user.json</c> is shared by every version.
    /// </summary>
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "Meitou");

    static string FilePath => Path.Combine(DataDirectory, "launcher.json");

    public string? KenshiPath { get; set; }
    public bool IncludePrereleases { get; set; }
    public string? SelectedVersion { get; set; }
    public string LaunchArguments { get; set; } = "";
    public List<ModPreset> Presets { get; set; } = [];

    public static LauncherConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), LauncherJson.Default.LauncherConfig) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, LauncherJson.Default.LauncherConfig));
    }
}

/// <summary>A named mod list: mod names in load order, each enabled or not.</summary>
public sealed class ModPreset
{
    public string Name { get; set; } = "";
    public List<PresetMod> Mods { get; set; } = [];
    public override string ToString() => Name;
}

public sealed class PresetMod
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
}

/// <summary>Source-generated JSON for the launcher's files, so it works trimmed.</summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(LauncherConfig))]
[System.Text.Json.Serialization.JsonSerializable(typeof(Releases.Cache))]
sealed partial class LauncherJson : System.Text.Json.Serialization.JsonSerializerContext;
