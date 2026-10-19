using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Meitou.Content;

namespace Meitou.Launcher;

public sealed partial class MainWindow : Window
{
    readonly LauncherConfig config;
    static readonly HttpClient Http = Releases.CreateClient();
    readonly Releases releases = new(Http);
    readonly NewsFeed news = new(Http);
    readonly LauncherUpdater updater;
    readonly ObservableCollection<ModRow> mods = [];
    List<Release> allReleases = [];
    GameInstall? install;
    CancellationTokenSource? download;
    ModRow? dragged;

    static string LauncherVersion => typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public MainWindow() : this(LauncherConfig.Load(), GameInstall.Locate()) { }

    public MainWindow(LauncherConfig config, GameInstall? found)
    {
        this.config = config;
        updater = new LauncherUpdater(releases, config.IncludePrereleases);
        InitializeComponent();
        ModListBox.ItemsSource = mods;
        ModListBox.AddHandler(PointerMovedEvent, ModList_PointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        ModListBox.AddHandler(PointerReleasedEvent, ModList_PointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        ModListBox.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);
        ArgumentsBox.Text = config.LaunchArguments;
        ShowPrereleaseToggle();
        DataFolderText.Text = $"Installed versions and the game's settings: {LauncherConfig.DataDirectory}";
        LauncherVersionText.Text = $"v{LauncherVersion} · {Releases.Rid}";
        LauncherAbout.Text = $"Version {LauncherVersion} for {Releases.Rid}. "
            + (updater.IsInstalled ? "Updates itself from GitHub." : "Not installed by its Setup, so updates open the release page.");
        RefreshPresets();
        RefreshInstalled();
        UseKenshi(found);
        Opened += async (_, _) => await Task.WhenAll(LoadNewsAsync(), RefreshReleasesAsync());
    }

    void Status(string text) => StatusText.Text = text;

    // ---- Pages ----

    void Nav_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var (nav, page) in new (ToggleButton, Control)[] { (NavHome, HomePage), (NavMods, ModsPage), (NavVersions, VersionsPage), (NavSettings, SettingsPage) })
        {
            nav.IsChecked = nav == sender;
            page.IsVisible = nav == sender;
        }
    }

    // ---- Home: news ----

    async Task LoadNewsAsync()
    {
        NewsCount.Text = "Loading";
        var (posts, problem) = await news.FetchAsync();
        NewsPanel.Children.Clear();
        foreach (var post in posts) NewsPanel.Children.Add(PostCard(post));
        if (posts.Count == 0) NewsPanel.Children.Add(new TextBlock { Classes = { "desc" }, Text = problem ?? "No news yet." });
        NewsCount.Text = posts.Count == 0 ? "" : problem is not null ? "Offline: saved posts" : posts.Count == 1 ? "1 post" : $"{posts.Count} posts";
    }

    Control PostCard(NewsPost post)
    {
        var card = new StackPanel { Spacing = 8 };
        if (post.Image is { } cover) card.Children.Add(MarkdownView.Image(NewsFeed.Resolve(post, cover), news, 300));
        card.Children.Add(new TextBlock { Text = post.Title, FontSize = 22, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        if (post.Date is { } date) card.Children.Add(new TextBlock { Classes = { "desc" }, Text = date.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture) });
        card.Children.Add(MarkdownView.Render(post, news, OpenLink));
        return new Border { Classes = { "post" }, Child = card };
    }

    // Links in posts open in the browser; only web addresses.
    async void OpenLink(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            await this.Launcher.LaunchUriAsync(uri);
    }

    // ---- Kenshi folder ----

    void UseKenshi(GameInstall? found)
    {
        install = found;
        KenshiPathBox.Text = found?.Root ?? config.KenshiPath ?? "";
        KenshiStatus.Text = found is null ? "Not found: browse to the folder with data/gamedata.base."
            : found.WorkshopDirectory is null ? "The folder with data/gamedata.base." : "The folder with data/gamedata.base; Steam workshop mods found.";
        KenshiBadge.Text = found is null ? "Kenshi not found: set its folder in Settings" : found.Root;
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

    void ShowPrereleaseToggle()
    {
        PrereleaseToggle.IsChecked = config.IncludePrereleases;
        PrereleaseToggle.Content = config.IncludePrereleases ? "On" : "Off";
    }

    void Prerelease_Click(object? sender, RoutedEventArgs e)
    {
        config.IncludePrereleases = PrereleaseToggle.IsChecked == true;
        config.Save();
        ShowPrereleaseToggle();
        ShowReleases();
    }

    async void OpenDataFolder_Click(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(LauncherConfig.DataDirectory);
        await this.Launcher.LaunchUriAsync(new Uri(LauncherConfig.DataDirectory + Path.DirectorySeparatorChar));
    }

    // ---- Mods ----

    void LoadMods()
    {
        mods.Clear();
        if (install is not null)
            foreach (var entry in ModList.LauncherOrder(install, ModList.Discover(install)))
                mods.Add(new ModRow(entry.Mod, entry.Enabled));
        foreach (var row in mods) row.PropertyChanged += (_, _) => ShowModCount();
        ShowModCount();
    }

    void ShowModCount() => ModCount.Text = mods.Count == 0 ? "No mods found" : $"{mods.Count(m => m.Enabled)} of {mods.Count} enabled";

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

    // Alt+Up / Alt+Down move the selected mod, as dragging does.
    void ModListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Alt || e.Key is not (Key.Up or Key.Down)) return;
        int i = ModListBox.SelectedIndex, j = i + (e.Key == Key.Up ? -1 : 1);
        if (i >= 0 && j >= 0 && j < mods.Count)
        {
            mods.Move(i, j);
            ModListBox.SelectedIndex = j;
        }
        e.Handled = true;
    }

    // Dragging a row's handle moves the row live under the pointer; the list holds the pointer capture, so rows can be
    // re-created underneath without ending the drag.
    void DragHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ModRow row } || !e.GetCurrentPoint(ModListBox).Properties.IsLeftButtonPressed) return;
        dragged = row;
        ModListBox.SelectedItem = row;
        e.Pointer.Capture(ModListBox);
        MarkDragged(true);
        e.Handled = true;
    }

    void ModList_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (dragged is null) return;
        double y = e.GetPosition(ModListBox).Y;
        int from = mods.IndexOf(dragged), to = from;
        for (int i = 0; i < mods.Count; i++)
        {
            if (ModListBox.ContainerFromIndex(i) is not Control item || item.TranslatePoint(new Point(0, 0), ModListBox) is not { } top) continue;
            if (y >= top.Y && y < top.Y + item.Bounds.Height) { to = i; break; }
        }
        if (to == from) return;
        MarkDragged(false);
        mods.Move(from, to);
        ModListBox.SelectedItem = dragged;
        ModListBox.UpdateLayout(); // the next hit test needs the rows where they are now
        MarkDragged(true);
    }

    void ModList_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (dragged is null) return;
        e.Pointer.Capture(null);
        EndDrag();
    }

    void EndDrag()
    {
        if (dragged is null) return;
        MarkDragged(false);
        dragged = null;
    }

    void MarkDragged(bool on)
    {
        if (dragged is not null && ModListBox.ContainerFromItem(dragged) is { } item)
            item.Classes.Set("dragging", on);
    }

    void ModListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ModListBox.SelectedItem is not ModRow row) { ModDetails.Text = ""; return; }
        var header = row.Mod.Header;
        var parts = new List<string>();
        if (header is { Description.Length: > 0 }) parts.Add(header.Description.ReplaceLineEndings(" "));
        if (header is { Dependencies.Count: > 0 })
        {
            var missing = header.Dependencies.Where(d => !IsAvailableBefore(d, row)).ToList();
            parts.Add($"Needs {string.Join(", ", header.Dependencies)}" + (missing.Count > 0 ? $" (not enabled above it: {string.Join(", ", missing)})" : ""));
        }
        parts.Add(row.Mod.Folder);
        ModDetails.Text = string.Join(" · ", parts);
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
        ShowModCount();
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
        await ShowLauncherUpdateAsync();
        Status(problem ?? "");
    }

    void ShowReleases()
    {
        var installed = Releases.Installed();
        var rows = allReleases
            .Where(r => r.Kind == ReleaseKind.Game && (config.IncludePrereleases || !r.Prerelease))
            .Select(r => new ReleaseRow(r, installed.Contains(r.Version)))
            .ToList();
        ReleaseListBox.ItemsSource = rows;
        VersionCount.Text = $"{rows.Count} releases · {installed.Count} installed";
    }

    async void RefreshReleases_Click(object? sender, RoutedEventArgs e) => await RefreshReleasesAsync();

    void ReleaseListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var release = (ReleaseListBox.SelectedItem as ReleaseRow)?.Release;
        ReleaseNotes.Text = PlainNotes(release?.Notes ?? "");
        if (download is null) InstallButton.IsEnabled = release?.Asset is not null;
    }

    // Release notes are Markdown; shown as text without heading marks and code ticks.
    static string PlainNotes(string markdown) =>
        string.Join("\n", markdown.ReplaceLineEndings("\n").Split('\n').Select(l => l.TrimStart('#').TrimStart().Replace("`", "")));

    // Install, or Cancel while a download runs.
    async void Install_Click(object? sender, RoutedEventArgs e)
    {
        if (download is not null) { download.Cancel(); return; }
        if (ReleaseListBox.SelectedItem is not ReleaseRow { Release: var release }) return;
        using var cts = download = new CancellationTokenSource();
        InstallButton.Content = "Cancel";
        DownloadProgress.Value = 0;
        DownloadProgress.IsVisible = true;
        Status($"Downloading {release.Title}...");
        try
        {
            await releases.InstallAsync(release, new Progress<double>(p => DownloadProgress.Value = p), cts.Token);
            config.SelectedVersion = release.Version;
            config.Save();
            RefreshInstalled();
            ShowReleases();
            Status($"{release.Title} installed.");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Status($"Download of {release.Title} cancelled.");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status($"Install failed: {ex.Message}");
        }
        finally
        {
            download = null;
            InstallButton.Content = "Install";
            DownloadProgress.IsVisible = false;
        }
    }

    void Uninstall_Click(object? sender, RoutedEventArgs e)
    {
        if (download is not null || ReleaseListBox.SelectedItem is not ReleaseRow { Release: var release }) return;
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

    async void OpenVersionFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (ReleaseListBox.SelectedItem is not ReleaseRow { Release: var release }) return;
        var dir = Releases.VersionDirectory(release.Version);
        if (!Directory.Exists(dir)) { Status($"{release.Title} is not installed."); return; }
        await this.Launcher.LaunchUriAsync(new Uri(dir + Path.DirectorySeparatorChar));
    }

    async void OpenReleasePage_Click(object? sender, RoutedEventArgs e)
    {
        if (ReleaseListBox.SelectedItem is ReleaseRow row)
            await this.Launcher.LaunchUriAsync(new Uri(row.Release.PageUrl));
    }

    // ---- Launcher updates ----

    // Installed by its Setup or AppImage: Velopack finds, downloads and applies the update. Run from anywhere else (a build, a copied
    // folder): the newest launcher-v* release's page.
    async Task ShowLauncherUpdateAsync()
    {
        string? version;
        if (updater.IsInstalled)
        {
            try { version = await updater.CheckAsync(); }
            catch (Exception ex) { LauncherAbout.Text = $"Version {LauncherVersion}. The update check failed: {ex.Message}"; return; }
            LauncherUpdateButton.Tag = null;
        }
        else
        {
            var page = allReleases.Where(r => r.Kind == ReleaseKind.Launcher && (config.IncludePrereleases || !r.Prerelease))
                .FirstOrDefault(r => System.Version.TryParse(r.Version.Split('-')[0], out var v) && v > System.Version.Parse(LauncherVersion));
            version = page?.Version;
            LauncherUpdateButton.Tag = page?.PageUrl;
        }
        LauncherUpdateButton.IsVisible = version is not null;
        LauncherUpdateButton.Content = updater.IsInstalled ? $"Update to {version}" : $"Get launcher {version}";
        if (version is not null) Status($"Launcher {version} is available (Settings).");
    }

    async void LauncherUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (LauncherUpdateButton.Tag is string url) { await this.Launcher.LaunchUriAsync(new Uri(url)); return; }
        LauncherUpdateButton.IsEnabled = false;
        try
        {
            await updater.UpdateAndRestartAsync(p => Dispatcher.UIThread.Post(() => Status($"Updating the launcher: {p}%")), CancellationToken.None);
        }
        catch (Exception ex)
        {
            Status($"Launcher update failed: {ex.Message}");
            LauncherUpdateButton.IsEnabled = true;
        }
    }

    // ---- Window: own title bar and resize grips ----

    void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) ToggleMaximize();
        else BeginMoveDrag(e);
    }

    void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Maximize_Click(object? sender, RoutedEventArgs e) => ToggleMaximize();

    void Close_Click(object? sender, RoutedEventArgs e) => Close();

    void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void Grip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: string edge } && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginResizeDrag(Enum.Parse<WindowEdge>(edge), e);
    }

    // Maximised, the plate fills the screen: no shadow margin, square corners, no grips.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != WindowStateProperty || Plate is null) return;
        bool max = WindowState == WindowState.Maximized;
        Plate.Margin = new Thickness(max ? 0 : 8);
        Plate.CornerRadius = new CornerRadius(max ? 0 : 10);
        Grips.IsVisible = !max;
        MaximizeIcon.Data = Avalonia.Media.Geometry.Parse(max ? "M2,0 H10 V8 M0,2 H8 V10 H0 Z" : "M0,0 H10 V10 H0 Z");
    }

    // ---- Play ----

    void Play_Click(object? sender, RoutedEventArgs e)
    {
        if (install is null) { Status("Set the Kenshi folder first (Settings)."); return; }
        if (VersionBox.SelectedItem is not string version) { Status("Install a version first (Versions)."); return; }
        if (Releases.FindGame(Releases.VersionDirectory(version)) is not { } exe) { Status($"Meitou {version} is not installed any more."); return; }
        if (!SaveMods()) return;
        config.SelectedVersion = version;
        config.Save();
        var start = new ProcessStartInfo(exe)
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
