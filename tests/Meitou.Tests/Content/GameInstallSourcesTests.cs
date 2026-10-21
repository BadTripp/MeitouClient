using Meitou.Content;

namespace Meitou.Tests.Content;

public sealed class GameInstallSourcesTests : IDisposable
{
    readonly string home = Path.Combine(Path.GetTempPath(), "meitou-installs-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(home)) Directory.Delete(home, recursive: true); }

    string MakeInstall(params string[] parts)
    {
        var root = Path.Combine([home, .. parts]);
        Directory.CreateDirectory(Path.Combine(root, "data"));
        File.WriteAllText(Path.Combine(root, "data", "gamedata.base"), "");
        return root;
    }

    static string FirstValid(IEnumerable<string> candidates) => candidates.First(GameInstall.IsValid);

    [Fact]
    public void Heroics_list_gives_the_install_paths_and_a_broken_list_gives_none()
    {
        var kenshi = MakeInstall("Games", "Heroic", "Kenshi");
        var list = Path.Combine(home, ".config", "heroic", "gog_store", "installed.json");
        Directory.CreateDirectory(Path.GetDirectoryName(list)!);
        File.WriteAllText(list, $$"""{"installed":[{"appName":"1","install_path":"{{Path.Combine(home, "Games", "Heroic", "Other").Replace("\\", "\\\\")}}"},{"appName":"2","install_path":"{{kenshi.Replace("\\", "\\\\")}}"}]}""");
        Assert.Equal(kenshi, FirstValid(GameInstallSources.Launchers(home)));
        File.WriteAllText(list, "{ not json");
        Assert.Empty(GameInstallSources.HeroicInstalls(list));
    }

    [Fact]
    public void Lutris_game_files_that_mention_kenshi_give_the_folder_of_their_exe()
    {
        var kenshi = MakeInstall("lutris-games", "Kenshi");
        var games = Path.Combine(home, ".config", "lutris", "games");
        Directory.CreateDirectory(games);
        File.WriteAllText(Path.Combine(games, "kenshi-1.yml"), $"game_slug: kenshi\ngame:\n  exe: {Path.Combine(kenshi, "kenshi_x64.exe")}\n");
        File.WriteAllText(Path.Combine(games, "other-2.yml"), "game_slug: other\ngame:\n  exe: /nowhere/other.exe\n");
        Assert.Equal([kenshi], GameInstallSources.LutrisInstalls(games));
    }

    [Fact]
    public void A_wine_prefix_or_bottle_with_the_gog_folder_is_found()
    {
        var wine = MakeInstall(".wine", "drive_c", "GOG Games", "Kenshi");
        Assert.Equal(wine, FirstValid(GameInstallSources.Launchers(home)));
        Directory.Delete(Path.Combine(home, ".wine"), recursive: true);
        var bottle = MakeInstall(".local", "share", "bottles", "bottles", "Games", "drive_c", "Program Files (x86)", "GOG Galaxy", "Games", "Kenshi");
        Assert.Equal(bottle, FirstValid(GameInstallSources.Launchers(home)));
    }

    [Fact]
    public void A_folder_without_the_game_data_is_not_accepted()
    {
        Directory.CreateDirectory(Path.Combine(home, ".wine", "drive_c", "GOG Games", "Kenshi"));
        Assert.DoesNotContain(GameInstallSources.Launchers(home), GameInstall.IsValid);
    }

    [Fact]
    public void A_desktop_launcher_named_kenshi_gives_its_path_or_the_folder_of_its_program()
    {
        var withPath = MakeInstall("launcher", "A");
        var withExec = MakeInstall("launcher", "B");
        var apps = Path.Combine(home, ".local", "share", "applications");
        Directory.CreateDirectory(Path.Combine(apps, "wine", "Programs"));
        File.WriteAllText(Path.Combine(apps, "kenshi-a.desktop"), $"[Desktop Entry]\nName=Kenshi\nPath={withPath}\nExec=env WINEPREFIX=/x wine start /unix kenshi_x64.exe\n");
        File.WriteAllText(Path.Combine(apps, "wine", "Programs", "kenshi-b.desktop"), $"[Desktop Entry]\nName=Kenshi\nExec=env WINEPREFIX=/x wine \"{Path.Combine(withExec, "kenshi_x64.exe")}\"\n");
        File.WriteAllText(Path.Combine(apps, "other.desktop"), "[Desktop Entry]\nName=Other\nPath=/nowhere\n");
        var found = GameInstallSources.DesktopEntries(apps).Where(GameInstall.IsValid).ToList();
        Assert.Contains(withPath, found);
        Assert.Contains(withExec, found);
        Assert.DoesNotContain("/nowhere", GameInstallSources.DesktopEntries(apps));
    }

    [Fact]
    public void Steams_record_names_an_install_under_another_folder_name()
    {
        var steamapps = Path.Combine(home, "lib", "steamapps");
        Directory.CreateDirectory(steamapps);
        Assert.Null(GameInstallSources.SteamInstallDirectory(steamapps));
        File.WriteAllText(Path.Combine(steamapps, "appmanifest_233860.acf"), "\"AppState\"\n{\n\t\"appid\"\t\t\"233860\"\n\t\"installdir\"\t\t\"Kenshi Copy\"\n}\n");
        Assert.Equal(Path.Combine(steamapps, "common", "Kenshi Copy"), GameInstallSources.SteamInstallDirectory(steamapps));
    }

    [Fact]
    public void The_xdg_variables_move_the_folders_that_are_searched_and_a_relative_one_is_ignored()
    {
        var kenshi = MakeInstall("elsewhere", "Kenshi");
        var dataHome = Path.Combine(home, "data-home");
        Directory.CreateDirectory(Path.Combine(dataHome, "applications"));
        File.WriteAllText(Path.Combine(dataHome, "applications", "kenshi.desktop"), $"[Desktop Entry]\nName=Kenshi\nPath={kenshi}\n");
        Func<string, string?> env = name => name == "XDG_DATA_HOME" ? dataHome : null;
        Assert.Contains(kenshi, GameInstallSources.Launchers(home, null, env));
        Assert.DoesNotContain(kenshi, GameInstallSources.Launchers(home));

        var relative = GameInstallSources.Xdg.From(home, name => name is "XDG_DATA_HOME" or "XDG_CONFIG_HOME" ? "relative/path" : null);
        Assert.Equal(Path.Combine(home, ".local/share"), relative.DataHome);
        Assert.Equal(Path.Combine(home, ".config"), relative.ConfigHome);
        Assert.Contains("/usr/share", relative.DataDirs);
        var extra = Path.Combine(home, "extra-share");
        Assert.Contains(extra, GameInstallSources.Xdg.From(home, name => name == "XDG_DATA_DIRS" ? extra + ":/opt/share" : null).DataDirs);
    }

    [Fact]
    public void The_user_config_file_lives_under_the_xdg_config_folder()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows keeps meitou.local.json next to the executable");
        var config = Path.Combine(home, "cfg");
        Assert.Equal(Path.Combine(config, "meitou", "config.json"), GameInstallSources.UserConfigFile(name => name == "XDG_CONFIG_HOME" ? config : null));
    }

    [Fact]
    public void A_wine_prefixs_registry_names_where_gog_installed_kenshi()
    {
        var prefix = Path.Combine(home, "prefix");
        var kenshi = MakeInstall("games", "Kenshi");
        Directory.CreateDirectory(Path.Combine(prefix, "dosdevices"));
        File.CreateSymbolicLink(Path.Combine(prefix, "dosdevices", "z:"), "/");
        // The layout GOG's installer writes into system.reg (checked on a real Heroic prefix): doubled backslashes, lower-case value names, a Z: path.
        var windows = kenshi.Replace("/", "\\\\");
        File.WriteAllText(Path.Combine(prefix, "system.reg"), "WINE REGISTRY Version 2\n\n"
            + "[Software\\\\Wow6432Node\\\\GOG.com\\\\Games\\\\1193046833] 1778854162\n#time=1dce4746dbb17ce\n\"gameID\"=\"1193046833\"\n\"gameName\"=\"Kenshi\"\n"
            + $"\"path\"=\"Z:{windows}\"\n\"workingDir\"=\"Z:{windows}\"\n\n"
            + "[Software\\\\Wow6432Node\\\\GOG.com\\\\Games\\\\1351891846] 1778854162\n\"gameName\"=\"Jazz Jackrabbit 2\"\n\"path\"=\"Z:\\\\nowhere\\\\Jazz\"\n");
        Assert.Equal([kenshi], GameInstallSources.WinePrefixInstalls(prefix).Distinct());
        Assert.Equal(Path.Combine("/", "mnt", "Games", "K"), GameInstallSources.ToUnixPath(prefix, @"Z:\mnt\Games\K\"));
        var bare = Path.Combine(home, "bare");
        Assert.Equal(Path.Combine(bare, "drive_c", "GOG Games", "Kenshi"), GameInstallSources.ToUnixPath(bare, @"C:\GOG Games\Kenshi"));
        Assert.Null(GameInstallSources.ToUnixPath(prefix, "not a windows path"));
    }

    [Fact]
    public void A_proton_prefix_and_an_uninstall_entry_are_read_too_and_heroics_game_config_names_the_prefix()
    {
        var kenshi = MakeInstall("games", "Kenshi");
        var prefix = Path.Combine(home, "proton");
        Directory.CreateDirectory(Path.Combine(prefix, "pfx"));
        File.WriteAllText(Path.Combine(prefix, "pfx", "system.reg"), "[Software\\\\Wow6432Node\\\\Microsoft\\\\Windows\\\\CurrentVersion\\\\Uninstall\\\\1193046833_is1] 1\n\"DisplayName\"=\"Kenshi\"\n"
            + $"\"InstallLocation\"=\"Z:{kenshi.Replace("/", "\\\\")}\\\\\"\n");
        Assert.Contains(kenshi, GameInstallSources.WinePrefixInstalls(prefix));
        var config = Path.Combine(home, "heroic", "GamesConfig");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "1.json"), "{\"1\":{\"winePrefix\":\"" + prefix + "\"},\"version\":\"v0\"}");
        Assert.Equal([prefix], GameInstallSources.HeroicPrefixes(config));
    }
}
