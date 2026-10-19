# Launcher

`launcher/Meitou.Launcher` (Avalonia, `meitou-launcher`): installs game releases from GitHub, edits the mod list like Kenshi's own
launcher, keeps mod presets and starts the game. Styled after the Meitou UI kit ([meitou-ui](meitou-ui.md)) with its own title bar; Exo 2
is read from the Kenshi install, nothing is shipped. Mods come only from disk (`mods/` and downloaded workshop
items); the launcher never downloads or hosts mods.

## Releases

- One repository, two tag families: the game `v<version>` (`release.yml`), the launcher `launcher-v<version>`
  (`launcher-release.yml`, changelog `launcher/CHANGELOG.md`). Launcher releases are never marked Latest. Both workflows pass
  `--notes-start-tag` with the previous tag of their own family, so the generated notes never span the other family's tag.
- Game assets: `Meitou-<version>-<rid>.zip`, `<rid>` as .NET's `RuntimeInformation.RuntimeIdentifier` (`win-x64`, `linux-x64`,
  `osx-x64`, `osx-arm64`). A release with no zip for the machine's RID is listed but not installable.
- The list comes from the REST API without a token (60 requests an hour per IP). The answer and its ETag are cached in
  `releases-cache.json`, so an unchanged list is a 304 that costs no quota. Downloads use the asset URLs, which don't count
  against the quota.
- A download is checked against the asset's `digest` (`sha256:<hex>`) when GitHub lists one. The zip's single top folder becomes
  `versions/<version>/`. The game is `MeitouClient[.exe]` since 0.15 (`meitou[.exe]` before; the launcher takes either); on Unix
  it gets its execute bits. Cancelling a download removes the partial zip and unpacked folder.

## Self-update (Velopack)

- `launcher-release.yml` packs the launcher with `vpk pack` (Velopack, MIT; `vpk` version = the `Velopack` package version) on each
  runtime's own OS, one channel per RID (`--channel <rid>`, packId `MeitouLauncher`): Windows `-Setup.exe` + `-Portable.zip`,
  Linux an AppImage, macOS a bundle and installer (Mac-only tooling, unsigned). Every file name carries its channel
  (**Verified** for win-x64: `MeitouLauncher-<v>-win-x64-full.nupkg`, `releases.win-x64.json`, `assets.win-x64.json`,
  `RELEASES-win-x64`), so all four go on one `launcher-v<version>` release; the job fails on a name clash.
- `Program.Main` calls `VelopackApp.Build().Run()` first. `LauncherUpdater` uses `ExplicitChannel = <rid>` and a `GithubSource`
  whose `GetReleases` returns only `launcher-v*` releases from the launcher's own cached list. Velopack's stock listing reads just
  the newest 10 releases (`per_page=10`, page 1; **Verified** in its source 2026-10-19), which game releases would crowd out;
  its `GitBase` merges the feed of every release given and skips releases without one.
- Installed by Setup or AppImage (`UpdateManager.IsInstalled`): Settings offers "Update to <v>", which downloads, applies and
  restarts. Run from anywhere else (a build, a copied folder): a button to the newest `launcher-v*` release page.
- **Unknown**: the update path end to end; no `launcher-v*` release exists yet.

## Game zips per runtime

`release.yml` runs `tools/scripts/package-release.ps1 -Rid <rid>` on one runner per RID (win-x64 on Windows, linux-x64 on Ubuntu,
both osx RIDs on macOS) and attaches all four zips to the one `v<version>` release. Each zip has a single top folder
`Meitou-<version>-<rid>/` with the game (named by `Meitou.Game`'s `AssemblyName`), `meitou-viewer` and the self-contained Satori runtime. Unix zips are made with
`zip(1)` (execute bits kept), macOS binaries and dylibs are ad-hoc signed, runtime debug symbols (`*.dbg`, ~250 MB) are dropped.

Native dependencies off Windows:
- Bundled from NuGet runtimes (**Verified** in the linux-x64 publish output): `libopenal.so`/`.dylib` (OpenAL Soft 1.23.1),
  `libglfw.so.3`/`.3.dylib` (GLFW 3.4), `libshaderc_shared`.
- Windows-only, not shipped: DLSS/Streamline (`sl.*.dll`, `nvngx_dlss.dll`, `NvLowLatencyVk.dll`) and FSR (`amd_fidelityfx_vk.dll`);
  both load via `NativeLibrary.TryLoad` and report a reason when missing. `FsrUpscaler` also loads `vulkan-1.dll` by name.
- Linux: the Vulkan loader (`libvulkan.so.1`) and driver come from the system, as do X11/Wayland and the OpenAL backend
  (PipeWire/PulseAudio/ALSA). **Unknown**: whether the game starts and renders on Linux; never run there.
- macOS: no Vulkan loader or MoltenVK is bundled (no `Silk.NET.MoltenVK.Native` reference); the user must install one (LunarG SDK).
  `VulkanDevice` requests and requires Vulkan 1.3 and does not enable `VK_KHR_portability_enumeration`, which the loader needs to list
  MoltenVK. **Unknown**: whether MoltenVK offers 1.3 and the features the renderer uses; never run on macOS. Browser downloads get
  Gatekeeper's quarantine flag (no Apple signing or notarisation).
- Kenshi's Steam folder is found only on Windows (`GameInstall`); elsewhere it comes from `KENSHI_PATH` or the console prompt.

## Files

The launcher's data folder (its program, once installed, is Velopack's, e.g. `%LocalAppData%/MeitouLauncher`) is `%LocalAppData%/Meitou`, `~/.local/share/Meitou` or `~/Library/Application Support/Meitou`, the same
folder as the game's caches (shader cache, LODs, navmesh). Beside them it holds
`launcher.json` (Kenshi folder, channel, chosen version, launch arguments, presets), `versions/` and the game's working directory.
The game runs from that folder, so its `meitou.user.json` (and anything else it writes to the working directory) is shared by all
versions. The game gets the Kenshi folder through `KENSHI_PATH`.

## Mod list

`ModList` (Meitou.Core) holds the discovery and `mods.cfg` rules shared with `LoadOrder`
([overview](formats/overview.md#load-order)):

- Shown as the original launcher shows it: enabled mods in `mods.cfg` order first, then the rest after their header dependencies.
  Mods in a dependency cycle are kept at the end, where the original drops them. Mods not in `__mods.list` are new and start enabled.
- Saving (Save, Play) writes `mods.cfg` (enabled `<name>.mod`, list order, CRLF) and `__mods.list` (every listed name, plus old
  names not on disk). These are the files the original game reads, so the mod list applies to it too.
- A preset is a named, ordered list of names with enabled flags. Applying one reorders the list. Mods the preset doesn't name go
  to the end, disabled, and mods it names that aren't on disk are reported. Applying doesn't save.
- A mod folder with `RE_Kenshi.json` is marked as a plugin mod whose DLLs Meitou ignores ([RE_Kenshi](formats/rekenshi.md)).

## News

The Home page shows the repository's `news/` folder (format in `news/README.md`): one folder per article,
`<yyyy-mm-dd>-<slug>/index.md` with an optional front matter block (`title`, `date`, `image`) and its pictures beside it, newest
20 first. `news/` is listed through the contents API on `master` (one call, cached with its ETag in `news-cache/`); each
`index.md` downloads from raw.githubusercontent.com and is kept by its folder's tree hash, images by URL, so the page works
offline. Relative image and link paths resolve against the article's folder; a folder without `index.md` is skipped.
Debug builds run from a checkout read its own `news/` from disk instead (found above the executable next to `MeitouClient.slnx`),
to preview posts before they are pushed; the header then says "Debug: local news/". Markdown is parsed by Markdig and drawn
by `MarkdownView` (headings, paragraphs, emphasis, code, links, lists, quotes, code blocks, rules, images); raw HTML is dropped
and links open only for http(s).
