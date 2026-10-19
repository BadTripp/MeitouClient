# Launcher

`launcher/Meitou.Launcher` (Avalonia, `meitou-launcher`): installs game releases from GitHub, edits the mod list like Kenshi's own
launcher, keeps mod presets and starts the game. Prototype UI, unstyled. Mods come only from disk (`mods/` and downloaded workshop
items); the launcher never downloads or hosts mods.

## Releases

- One repository, two tag families: the game `v<version>` (`release.yml`), the launcher `launcher-v<version>`
  (`launcher-release.yml`, changelog `launcher/CHANGELOG.md`). Launcher releases are never marked Latest. Both workflows pass
  `--notes-start-tag` with the previous tag of their own family, so the generated notes never span the other family's tag.
- Assets: `Meitou-<version>-<rid>.zip` and `MeitouLauncher-<version>-<rid>.zip`, `<rid>` as .NET's
  `RuntimeInformation.RuntimeIdentifier` (`win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`). A release with no zip for the machine's
  RID is listed but not installable.
- The list comes from the REST API without a token (60 requests an hour per IP). The answer and its ETag are cached in
  `releases-cache.json`, so an unchanged list is a 304 that costs no quota. Downloads use the asset URLs, which don't count
  against the quota.
- A download is checked against the asset's `digest` (`sha256:<hex>`) when GitHub lists one. The zip's single top folder becomes
  `versions/<version>/`. On Unix the game executable (`meitou`) gets its execute bits.
- A newer non-pre-release `launcher-v*` shows a button to its page. The launcher does not update itself yet.

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

The launcher's folder is `%LocalAppData%/Meitou`, `~/.local/share/Meitou` or `~/Library/Application Support/Meitou`, the same
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
