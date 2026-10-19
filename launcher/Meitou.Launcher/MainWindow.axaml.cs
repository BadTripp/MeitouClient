using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Meitou.Content;

namespace Meitou.Launcher;

public sealed partial class MainWindow : Window
{
    readonly LauncherConfig config = LauncherConfig.Load();
    readonly Releases releases = new(Releases.CreateClient());
    readonly ObservableCollection<ModRow> mods = [];
    List<Release> allReleases = [];
    GameInstall? install;
    bool busy;

    static string LauncherVersion => typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public MainWindow()
    {
        InitializeComponent();
        ModListBox.ItemsSource = mods;
        PrereleaseBox.IsChecked = config.IncludePrereleases;
        ArgumentsBox.Text = config.LaunchArguments;
        DataFolderText.Text = $"Versions and game settings are kept in {LauncherConfig.DataDirectory}";
        LauncherVersionText.Text = $"Launcher {LauncherVersion} ({Releases.Rid})";
        RefreshPresets();
        RefreshInstalled();
        UseKenshi(config.KenshiPath is { } saved && GameInstall.IsValid(saved) ? GameInstall.Open(saved) : GameInstall.Locate());
        Opened += async (_, _) => await RefreshReleasesAsync();
    }

    void Status(string text) => StatusText.Text = text;

    // ---- Kenshi folder ----

    void UseKenshi(GameInstall? found)
    {
        install = found;
        KenshiPathBox.Text = found?.Root ?? config.KenshiPath ?? "";
        KenshiStatus.Text = found is null ? "Kenshi not found: browse to its folder." : $"Kenshi found{(found.WorkshopDirectory is null ? "" : ", with Steam workshop mods")}.";
        if (found is not null && config.KenshiPath != found.Root)
        {
            config.KenshiPath = found.Root;
            config.Save();
        }
        LoadMods();
    }

    void DetectKenshi_Click(object? sender, RoutedEventArgs e) => UseKenshi(GameInstall.Locate());

    async void BrowseKenshi_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Kenshi folder" });
        if (folders is [var folder] && folder.TryGetLocalPath() is { } path)
            SetKenshiPath(path);
    }

    void KenshiPath_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (KenshiPathBox.Text is { } path && path != install?.Root)
            SetKenshiPath(path.Trim().Trim('"'));
    }

    void SetKenshiPath(string path)
    {
        if (GameInstall.IsValid(path)) UseKenshi(GameInstall.Open(path));
        else KenshiStatus.Text = $"'{path}' is not a Kenshi folder (data/gamedata.base not found).";
    }

    void Arguments_LostFocus(object? sender, RoutedEventArgs e)
    {
        config.LaunchArguments = ArgumentsBox.Text ?? "";
        config.Save();
    }

    // ---- Mods ----

    void LoadMods()
    {
        mods.Clear();
        if (install is null) return;
        foreach (var entry in ModList.LauncherOrder(install, ModList.Discover(install)))
            mods.Add(new ModRow(entry.Mod, entry.Enabled));
        Status($"{mods.Count} mods found, {mods.Count(m => m.Enabled)} enabled.");
    }

    bool SaveMods()
    {
        if (install is null) { Status("Set the Kenshi folder first (Settings)."); return false; }
        try
        {
            ModList.Save(install, mods.Select(m => new ModEntry(m.Mod, m.Enabled)));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status($"Could not save the mod list: {e.Message}");
            return false;
        }
    }

    void SaveMods_Click(object? sender, RoutedEventArgs e)
    {
        if (SaveMods()) Status($"Mod list saved: {mods.Count(m => m.Enabled)} enabled.");
    }

    void RefreshMods_Click(object? sender, RoutedEventArgs e) => LoadMods();

    void SelectAll_Click(object? sender, RoutedEventArgs e) { foreach (var m in mods) m.Enabled = true; }

    void SelectNone_Click(object? sender, RoutedEventArgs e) { foreach (var m in mods) m.Enabled = false; }

    void Up_Click(object? sender, RoutedEventArgs e) => MoveSelected(-1);

    void Down_Click(object? sender, RoutedEventArgs e) => MoveSelected(1);

    void MoveSelected(int by)
    {
        int i = ModListBox.SelectedIndex, j = i + by;
        if (i < 0 || j < 0 || j >= mods.Count) return;
        mods.Move(i, j);
        ModListBox.SelectedIndex = j;
    }

    void ModListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ModListBox.SelectedItem is not ModRow row) { ModDetails.Text = ""; return; }
        var header = row.Mod.Header;
        var lines = new List<string> { $"{row.Name}  ({row.Source}: {row.Mod.Folder})" };
        if (header is not null)
        {
            if (header.Author.Length > 0) lines.Add($"Author: {header.Author}");
            if (header.Description.Length > 0) lines.Add(header.Description);
            if (header.Dependencies.Count > 0)
            {
                var missing = header.Dependencies.Where(d => !IsAvailableBefore(d, row)).ToList();
                lines.Add($"Needs: {string.Join(", ", header.Dependencies)}" + (missing.Count > 0 ? $"  (not enabled above it: {string.Join(", ", missing)})" : ""));
            }
        }
        if (row.Mod.HasNativePlugin) lines.Add("RE_Kenshi plugin mod: its data loads in Meitou, its DLLs do not (docs/formats/rekenshi.md).");
        ModDetails.Text = string.Join("\n", lines);
    }

    // A dependency is met by a core file or by an enabled mod earlier in the list.
    bool IsAvailableBefore(string dependency, ModRow row)
    {
        if (dependency is "gamedata.base" or "Newwworld.mod" or "Dialogue.mod" or "rebirth.mod") return true;
        foreach (var m in mods)
        {
            if (m == row) return false;
            if (m.Enabled && m.Name + ".mod" == dependency) return true;
        }
        return false;
    }

    // ---- Presets ----

    void RefreshPresets(string? select = null)
    {
        PresetBox.ItemsSource = config.Presets.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        PresetBox.SelectedItem = config.Presets.FirstOrDefault(p => p.Name == select);
    }

    void SavePreset_Click(object? sender, RoutedEventArgs e)
    {
        var name = PresetName.Text?.Trim();
        if (string.IsNullOrEmpty(name)) { Status("Type a name for the preset."); return; }
        config.Presets.RemoveAll(p => p.Name == name);
        config.Presets.Add(new ModPreset { Name = name, Mods = [.. mods.Select(m => new PresetMod { Name = m.Name, Enabled = m.Enabled })] });
        config.Save();
        PresetName.Text = "";
        RefreshPresets(name);
        Status($"Preset '{name}' saved.");
    }

    void ApplyPreset_Click(object? sender, RoutedEventArgs e)
    {
        if (PresetBox.SelectedItem is not ModPreset preset) return;
        var byName = mods.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var ordered = new List<ModRow>();
        var missing = new List<string>();
        foreach (var p in preset.Mods)
        {
            if (byName.Remove(p.Name, out var row)) { row.Enabled = p.Enabled; ordered.Add(row); }
            else if (p.Enabled) missing.Add(p.Name);
        }
        foreach (var row in mods.Where(m => byName.ContainsKey(m.Name))) { row.Enabled = false; ordered.Add(row); }
        mods.Clear();
        foreach (var row in ordered) mods.Add(row);
        Status($"Preset '{preset.Name}' applied (not saved yet)." + (missing.Count > 0 ? $" Not on disk: {string.Join(", ", missing)}." : ""));
    }

    void DeletePreset_Click(object? sender, RoutedEventArgs e)
    {
        if (PresetBox.SelectedItem is not ModPreset preset) return;
        config.Presets.Remove(preset);
        config.Save();
        RefreshPresets();
        Status($"Preset '{preset.Name}' deleted.");
    }

    // ---- Versions ----

    void RefreshInstalled()
    {
        var installed = Releases.Installed();
        VersionBox.ItemsSource = installed;
        VersionBox.SelectedItem = installed.Contains(config.SelectedVersion ?? "") ? config.SelectedVersion : installed.FirstOrDefault();
    }

    async Task RefreshReleasesAsync()
    {
        Status("Checking GitHub for releases...");
        var (list, problem) = await releases.FetchAsync();
        allReleases = list;
        ShowReleases();
        if (allReleases.Where(r => r.Kind == ReleaseKind.Launcher && !r.Prerelease)
                .FirstOrDefault(r => System.Version.TryParse(r.Version, out var v) && v > System.Version.Parse(LauncherVersion)) is { } update)
        {
            LauncherUpdateButton.Content = $"Launcher {update.Version} is available: open its page";
            LauncherUpdateButton.Tag = update.PageUrl;
            LauncherUpdateButton.IsVisible = true;
        }
        Status(problem ?? $"{allReleases.Count(r => r.Kind == ReleaseKind.Game)} game releases on GitHub.");
    }

    void ShowReleases()
    {
        var installed = Releases.Installed();
        ReleaseListBox.ItemsSource = allReleases
            .Where(r => r.Kind == ReleaseKind.Game && (config.IncludePrereleases || !r.Prerelease))
            .Select(r => new ReleaseRow(r, installed.Contains(r.Version)))
            .ToList();
    }

    async void RefreshReleases_Click(object? sender, RoutedEventArgs e) => await RefreshReleasesAsync();

    void Prerelease_Changed(object? sender, RoutedEventArgs e)
    {
        config.IncludePrereleases = PrereleaseBox.IsChecked == true;
        config.Save();
        ShowReleases();
    }

    void ReleaseListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var release = (ReleaseListBox.SelectedItem as ReleaseRow)?.Release;
        ReleaseNotes.Text = release?.Notes ?? "";
        InstallButton.IsEnabled = release?.Asset is not null;
    }

    async void Install_Click(object? sender, RoutedEventArgs e)
    {
        if (busy || ReleaseListBox.SelectedItem is not ReleaseRow { Release: var release }) return;
        busy = true;
        DownloadProgress.Value = 0;
        DownloadProgress.IsVisible = true;
        Status($"Downloading {release.Title}...");
        try
        {
            await releases.InstallAsync(release, new Progress<double>(p => DownloadProgress.Value = p));
            config.SelectedVersion = release.Version;
            config.Save();
            RefreshInstalled();
            ShowReleases();
            Status($"{release.Title} installed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status($"Install failed: {ex.Message}");
        }
        finally
        {
            DownloadProgress.IsVisible = false;
            busy = false;
        }
    }

    void Uninstall_Click(object? sender, RoutedEventArgs e)
    {
        if (busy || ReleaseListBox.SelectedItem is not ReleaseRow { Release: var release }) return;
        var dir = Releases.VersionDirectory(release.Version);
        if (!Directory.Exists(dir)) { Status($"{release.Title} is not installed."); return; }
        try
        {
            Directory.Delete(dir, true);
            RefreshInstalled();
            ShowReleases();
            Status($"{release.Title} uninstalled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status($"Could not remove {dir}: {ex.Message}");
        }
    }

    async void OpenReleasePage_Click(object? sender, RoutedEventArgs e)
    {
        if (ReleaseListBox.SelectedItem is ReleaseRow row)
            await this.Launcher.LaunchUriAsync(new Uri(row.Release.PageUrl));
    }

    async void LauncherUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (LauncherUpdateButton.Tag is string url)
            await this.Launcher.LaunchUriAsync(new Uri(url));
    }

    // ---- Play ----

    void Play_Click(object? sender, RoutedEventArgs e)
    {
        if (install is null) { Status("Set the Kenshi folder first (Settings)."); return; }
        if (VersionBox.SelectedItem is not string version) { Status("Install a version first (Versions)."); return; }
        if (!SaveMods()) return;
        config.SelectedVersion = version;
        config.Save();
        var start = new ProcessStartInfo(Path.Combine(Releases.VersionDirectory(version), Releases.GameExecutable))
        {
            WorkingDirectory = LauncherConfig.DataDirectory,
            UseShellExecute = false,
        };
        start.Environment[GameInstall.EnvironmentVariable] = install.Root;
        foreach (var arg in SplitArguments(config.LaunchArguments)) start.ArgumentList.Add(arg);
        try
        {
            Process.Start(start);
            Status($"Meitou {version} started.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            Status($"Could not start Meitou {version}: {ex.Message}");
        }
    }

    // Space-separated, with double quotes around arguments that contain spaces.
    static IEnumerable<string> SplitArguments(string text)
    {
        var current = new System.Text.StringBuilder();
        bool quoted = false, any = false;
        foreach (char c in text)
        {
            if (c == '"') { quoted = !quoted; any = true; }
            else if (char.IsWhiteSpace(c) && !quoted) { if (any) yield return current.ToString(); current.Clear(); any = false; }
            else { current.Append(c); any = true; }
        }
        if (any) yield return current.ToString();
    }
}
