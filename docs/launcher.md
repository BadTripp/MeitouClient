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
