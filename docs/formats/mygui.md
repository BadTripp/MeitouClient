# MyGUI UI resources

Sources: the configured Kenshi install's `data/gui/`, the existing
[screen research](../game/ui-screens.md), and [MyGUI's upstream skin/template examples](https://github.com/MyGUI/mygui/tree/master/Docs/Papers).

## Layouts and resources

- **Verified** against every installed `data/gui/layout/**/*.layout` by
  `MyGuiTests.AllInstalledLayoutsAndHudResourcesCanBeRead`: layouts are XML with a
  `MyGUI type="Layout"` root and nested `Widget` elements. Attributes include `type`,
  `skin`, `name`, `layer`, `align`, `position` and `position_real`. Coordinates have
  four whitespace-separated numbers: left, top, width, height.
- **Observed** in `Kenshi_MainPanel.layout`: `position_real` values describe a widget
  relative to its parent, including children of fractional-size panels. Values may
  extend outside 0..1. Meitou evaluates these against the current viewport; exact
  MyGUI integer rounding and resize alignment are still **Unknown**.
- **Verified** by the installed HUD test: named controls include `TimeSpeedButton1`
  through `TimeSpeedButton4`, `MoneyAmountText`, `DayText`, `TimeText` and
  `LifeBar1` through `LifeBar9`; there is no `LifeBar10`.
- **Observed** in the installed HUD: anonymous `TabItem` children have editor squad
  captions, `LifeBar*Value` widgets have example numeric captions, and
  `JobsPanelInfo` has a caption containing literal backslash-n sequences. They need
  runtime population rather than displaying the source's example values.
- **Verified** by the installed resource test: `kenshi_init.xml` is a `MyGUI
  type="List"` document; its `List file="..."` entries resolve through the game's
  resource index. This loads skin, image, pointer, template, colour and layer files.
  `core_settings.xml` has nested `MyGUI` settings elements rather than named resources.
- **Observed** in installed `kenshi_init.xml`: it does not list `kenshi_fonts.xml`.
  The in-game resource loader explicitly adds that font catalog; reading only the
  startup list leaves named fonts unresolved and silently uses the fallback atlas.
- **Observed** in `common_skins.xml` / `kenshi_skins.xml`: `ResourceSkin` carries a
  reference `size` and a texture name; each `BasisSkin` carries `type`, `offset`,
  `align`, and named `State` elements. State offsets select atlas pixel rectangles.
  SubSkin corners and edges combine Left/Right/Top/Bottom with HStretch/VStretch/
  Stretch. SimpleText/EditText parts describe text rather than image slices.
- **Observed** in the install's `kenshi_skins.xml`: native UI styling can reuse
  `Kenshi_GenericWindowSkin`, `Kenshi_GenericWindowHeaderSkin`,
  `Kenshi_Button1Skin`, `Kenshi_SquadPanelSkin` and `Kenshi_InventorySlotSkin`.
  These all refer to the `Kenshi_UI.png` atlas; no textures are copied into Meitou.
- **Observed** in the install's `kenshi_fonts.xml`: standard variants use
  `Exo2-SemiBold.ttf` (Small/MediumSmall/Medium/Large sizes 14/16/20/24), painted
  variants use `Sentencia_1.ttf` (20/25/28). `Kenshi_UI_Messages` uses
  `MailartRubberstamp-Regular.ttf`, size 110; all specify resolution 50.
  All standard and painted fonts set `Antialias` false and `Hinting` use_native.
- **Verified** (decompile `FUN_1403e81a0`, run after loading and on the Font Size
  option): the game resets every loaded `ResourceTrueTypeFont` to
  `Size = (int)getDefaultHeight() + font size2` (settings.cfg, default 3), still in
  points at the resource's resolution; the default height is taken from the
  font as first loaded. MyGUI 3.2.3 (MyGUI.log) makes the default height ascent +
  descent in pixels. **Observed** (user screenshots at 1080 p, text widths): the
  heights fit FreeType's grid-fitted metrics, hhea ascender rounded up plus
  descender rounded down at the fractional em (each at least the OS/2 Windows value
  at the rounded ppem): Sentencia 25 pt draws at 22 pt (15.3 px), Exo2 20 pt stays
  20 pt (13.9 px). Rounding to the integer ppem instead (head flag bit 3) gives
  20 pt for Sentencia and does not fit. Meitou: `KenshiFontSize` (default 3).
- **Observed** in the install's font files: `SubstituteCode` 0, `Distance` 2, `TabWidth` 4,
  `Codes` `32 126` and `8127 8217` for the English fonts; `locale/ja_JP` uses
  `ume-pgs5.ttf` with `32 255`, `12288 12543`, `19968 40959`, `65280 65519`. A locale's
  `kenshi_fonts.xml` redefines the same resource names.
- **Verified** against MyGUI `MyGUI3.2.2` (`ResourceTrueTypeFont.cpp`, `TextView.cpp`,
  `EditText.cpp`; the game's MyGUI.log says 3.2.3, which has no tag, and the file's later
  commits up to 2017 are texture spacing, device loss and speed, **Observed** in its git log)
  how the original builds and draws a font:
  - The face is opened at `FT_Set_Char_Size(Size*64, 0, Resolution, Resolution)`: points,
    fractional ppem. Load flags by `Hinting`: `use_native` `FT_LOAD_DEFAULT` (the
    bytecode interpreter), `force_auto` `FORCE_AUTOHINT`, `disable_auto` `NO_AUTOHINT`,
    `disable_all` `NO_HINTING`. Glyphs are rendered with `FT_LOAD_RENDER`: 8-bit
    coverage. **There is no monochrome rendering in 3.2** (`HintingMonochrome` and
    `FT_LOAD_TARGET_MONO` exist only in later versions): `Antialias` false writes white
    luminance with the coverage as alpha, true writes the coverage into both. The
    crisp look comes from hinting and integer positions.
  - No kerning. The pen advances by the hinted `advance.x / 64`; a glyph's quad is at
    pen + `horiBearingX / 64` and `floor(ascent - horiBearingY / 64 - OffsetHeight)`
    below the line top. MyGUI rounds no glyph position; a line's width is `(int)ceil`, so
    centred and right-aligned line starts are whole pixels.
  - The line height is the default height: ascent and descent from the FreeType size
    metrics (`ascender >> 6`, `-descender >> 6`), each raised to the OS/2 `usWin` and `sTypo`
    values times `y_ppem / unitsPerEm`.
  - `Codes`: every `range` adds code points; with none the Basic Multilingual Plane
    (`0 65535`); `hide` ranges are removed afterwards; space is always built; a code
    point the face lacks is dropped. A character outside the set draws the
    `SubstituteCode` glyph if that is in the set, else the face's glyph 0 (`.notdef`):
    Kenshi's 0 is not in 32-126, so the substitute is the `.notdef` box.
  - `TabWidth` is the tab's advance in pixels (0: eight spaces). `Distance` is the gap
    between glyphs in MyGUI's own texture (default 1): no layout effect. Inline `#RRGGBB`
    colour codes are handled by `TextView` and are **not** implemented in Meitou.
- Meitou draws text through `UiFont` (`src/Meitou.Rendering/Ui/`): FreeType 2.13.2 (via
  FreeTypeSharp), one face per file, one size per `UiFont`, glyphs rasterised into an atlas
  on demand (any code point, UTF-16 pairs as one), the atlas grows to 2048 square and then
  starts over; text geometry caches are dropped when it moves. Measuring, wrapping and
  drawing read the same glyph records, so they agree for every script. `UiPainter.Mode`
  selects the look:
  - **Faithful** follows the rules above: the resource's `Hinting`, `Codes`,
    `SubstituteCode`, `TabWidth`, `OffsetHeight`; fractional size at the resource's
    resolution (1/16 pt steps) scaled by viewport height, so at 1080 lines exactly the
    original's request; MyGUI's line height; no kerning; glyphs on whole pixels. The
    TrueType interpreter is set to version 35 (full bytecode hinting; 2.13 defaults to
    version 40, which ignores most horizontal hints). **Observed**: Exo2 20 pt@50 dpi
    gives ppem 13.9, ascent 14, descent 3 and integer advances (9, 8, 13), which is
    `KenshiFontSize`'s default height. **Unknown**: the FreeType version of the original,
    so pixel-for-pixel equality of the hinted shapes is not claimed.
    `ume-pgs5.ttf`'s bytecode fails in 2.13.2 (`Invalid_Opcode`, both interpreters;
    **Observed**, `FT_LOAD_DEFAULT`/`NO_AUTOHINT`): the glyph falls back to the auto-hinter,
    the font after three failures. `Antialias` true is drawn like false (no luminance
    modulation); no Kenshi font sets it.
  - **Meitou** (default) keeps the earlier look: any glyph the face has (`Codes` are not a
    limit), the substitute, else '?'; whole pixel sizes; `Antialias` false fonts hinted
    lightly at 1x (`FT_LOAD_TARGET_LIGHT`) on whole pixels with unhinted advances, others
    (and explicit `FontPixels` sizes) unhinted at 2x oversampling, placed freely; the 1.25 em
    line; kerning from the font's `kern`/GPOS pairs (read with stb_truetype). `FontPixels`
    text uses the `Kenshi_StandardFont_Medium` resource's file, so a locale's font.
  Pre-built glyphs: the `Codes` ranges up to 1024 code points (Faithful), else ASCII and
  Latin-1; the rest on demand. Lines wrap at spaces and between CJK characters.
- Main in-game UI applies a user-requested minimum of 16 em pixels at 1080 lines,
  scaled with viewport height, including resource fonts and explicit pixel sizes.
  Larger headings retain their sizes. Short captions centre the line box and clip
  within the widget rather than shrinking the font. Loading-screen tips use the
  same baseline. Title, loading banners and settings retain independent typography.
  This is a Meitou display policy, not a
  claim about the original font resources; it applies in both UI modes (a size below the
  minimum is a whole-pixel request at 72 dpi, hinted as the mode says).
- **Verified** against all installed layouts by `AllInstalledLayoutsAndHudResourcesCanBeRead`:
  ResourceLayout expansion produces finite widget bounds. The source widgets are
  kept immutable; template Client regions receive application children, and
  template pieces resize against their original pixel reference dimensions.
- **Observed** in `kenshi_templates.xml`: window captions and Client regions are
  template children; window buttons carry `UserString Event` values `close` and
  `pin`. `Kenshi_ScrollBarH` uses its own name as its root skin. A template root's
  native skin must not be instantiated as another template with the same name.
- **Observed**: a Button whose ResourceSkin has no `SimpleText`/`EditText` part shows no caption (MyGUI draws text through that part):
  `MapCenterButton` has caption "O" on the image-only `Kenshi_CenterMapButtonSkin` and shows only its `> ● <` icon. Meitou skips such
  button captions; other widget types keep theirs (the new-game description needs it).
- **Unknown**: exact native widget skin registration, integer rounding, text/font
  sizing, inherited pick flags, localisation, tab button sizing and clipping
  equivalence with the original. No full MyGUI compatibility claim is made yet.

## Implementation (2026-10-13)

`Meitou.Data.Ui` reads layouts, properties and user strings, decodes skin atlas
states, follows resource lists once per resolved path (including cyclic lists),
and retains each original XML document in memory, including unsupported resource
types. XML external entities and DTDs are disabled. No original assets are stored
in the repository. Resource lookup uses the existing `AssetLocator` and enabled
mod resource folders; whole-mod UI parity has not been established.

Both `--ui faithful` / `--ui meitou` modes read `Kenshi_MainPanel.layout` and render
the install's SubSkin/MainSkin atlas pieces, with the same published simulation
snapshot and command queue. Mode differences and extension contracts:
[native UI](../native-ui.md). Gameplay bindings and fidelity gaps:
[Faithful parity](../faithful-ui-parity.md).

Both modes use the original inventory, stats, overview and Escape menu layouts,
including their ResourceLayout window frames. The screen host handles modal input,
close/pin events, window dragging, selected tabs, text entry and scrolling/selecting
dynamic lists. Synthetic tests cover these interactions and clipping. Stats reads
all trained skills; inventory renders equipment names and pocket cell footprints.
Escape opens the pause menu; its Options action opens Tab settings. Save/Load now
open the original dialogs, show real slots and CAMERA metadata, and queue named
save/load requests for the host's tick boundary. Original slots are load sources;
named writes use Meitou's save root. Exit, replacing a save and replacing the live
game use the original confirmation layout. Title/new-game layouts show installed
artwork/credits and NEW_GAME_STARTOFF data; Begin uses the existing spawn backend.
The map shows the installed image and supports pan/zoom/centre, player/camera
markers and right-click movement. Original map placement/zoom equivalence remains
unverified. Other overview gameplay bindings are unfinished.

The renderer stores targets using a lower-left convention, so top-left UI clip
rectangles are converted before issuing Vulkan scissors. **Verified** by
`UiRenderingTests` with pixel readback and Vulkan synchronization validation.

## Additional resource observations (2026-10-14)

- **Observed** in the installed `Kenshi_InventorySlotSkin`: `TileRect` states carry
  `TileSize`, `TileH` and `TileV` properties. The renderer repeats the atlas cell and
  crops partial cells at the bounds. Inventory presentation uses square cells at 25 px times viewport height / 1080; bag bounds follow the container dimensions, and trader stock
  keeps its full canvas behind a clipped viewport with wheel/arrow scrolling. This scaling policy is Meitou's approximation; the screenshot observations are in [UI screens](../game/ui-screens.md#6-inventory-trade-dialogue-build).
- **Observed** in installed image sets: Group selects a texture and a two-component
  image size; Index selects a named frame sequence; Frame point gives the atlas
  origin. Meitou currently draws the first frame. `Kenshi_TitleScreen` points at
  `title_screen1.dds`; map marker groups point into `Kenshi_UI.png`.
- **Verified** against the installed `Kenshi_TextBoxEmptySkin` by the installed
  resource test: its SimpleText normal state declares colour `0 0 0` without an
  image offset. Text states therefore need independent property decoding even when
  they contribute no atlas rectangle. Synthetic tests verify state fallback and
  explicit widget colour overrides. Standard text templates use dark brown ink;
  runtime parchment paragraphs/list rows use that ink while dark panels can set
  light text explicitly. No original colour rules are inferred from the debug
  overlay's light text default.
- **Observed** in `Kenshi_GenericTextBoxSkin` and `Kenshi_Button1Skin`: SimpleText
  `offset` / `align` specify an inset text rectangle, separately from the atlas
  pieces. The painter now wraps, aligns and clips inside that rectangle, after any
  additional Meitou `TextPadding`. Padding never changes the widget's background
  or clickable bounds. `UiPaddingTests` checks resized skin clients, explicit
  insets and small-widget clipping. The extra paragraph/list insets are a Meitou
  presentation choice requested during review, not a newly verified original rule.
