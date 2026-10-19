using System.ComponentModel;
using System.Runtime.CompilerServices;
using Meitou.Content;

namespace Meitou.Launcher;

/// <summary>A line of the Mods tab.</summary>
public sealed class ModRow(ModInfo mod, bool enabled) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public ModInfo Mod { get; } = mod;
    public string Name => Mod.Name;
    public string Source => Mod.Source == ModSource.Workshop ? "workshop" : "local";
    public string Notes => Mod.HasNativePlugin ? "RE_Kenshi plugin: its DLLs are ignored in Meitou" : "";

    public bool Enabled
    {
        get;
        set { if (field != value) { field = value; Changed(); } }
    } = enabled;

    void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A line of the Versions tab.</summary>
public sealed class ReleaseRow(Release release, bool installed)
{
    public Release Release { get; } = release;
    public string Text => $"{Release.Title}  ({Release.Published:yyyy-MM-dd})"
        + (Release.Prerelease ? "  pre-release" : "")
        + (installed ? "  [installed]" : "")
        + (Release.Asset is null ? $"  no {Releases.Rid} build" : "");
}
