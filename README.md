# Your Launcher

A keyboard-first Windows launcher. Nothing is indexed automatically — you build your own tree of
folders, apps, files/paths, shell commands and URLs, and open it instantly with a global hotkey.

This is **Phase 4 (Icons)** per `tasks/todo.md`: Phase 1's hotkey/panel/navigation/launching, Phase 2's
fuzzy Turkish-aware whole-tree search, Phase 3's fully keyboard-driven add/edit/delete/move/cut-paste/
duplicate of nodes, plus automatic system icons for `app`/`path` targets, a missing-target warning badge,
and a `Ctrl+I` icon picker (glyph / emoji / file / exe-DLL). Tray/single-instance, drag & drop and theming
are later phases (see `launcher-spec.md` §14 and `tasks/todo.md`) — the code is structured so they slot in
without reshaping what's here.

## Build / run / test

```powershell
dotnet build                     # whole solution, warnings are errors
dotnet test                      # Launcher.Core.Tests (xUnit)
dotnet run --project src/Launcher.App
```

Requires .NET SDK 10.0.401+ (Windows). The solution is `YourLauncher.sln`.

To point the app at a different config directory (used for the corrupt-config smoke test, or to keep a
throwaway config while developing), set `YOURLAUNCHER_CONFIG_DIR` before launching:

```powershell
$env:YOURLAUNCHER_CONFIG_DIR = "C:\scratch\yl-config"
dotnet run --project src/Launcher.App
```

## Phase 1–3 status

Implemented (Phase 1):
- Global hotkey (default `Alt+Space`, `settings.hotkey`), toggling the panel.
- Panel positions itself on the monitor under the cursor, horizontally centered, top edge at 1/3 of the
  work-area height; per-monitor DPI aware (`app.manifest`, PerMonitorV2).
- config.json load/create-default/atomic-save (temp file + `File.Replace`, previous version kept as
  `config.backup.json`); corrupt JSON never crashes the app and never touches the bad file — the panel
  starts with an empty in-memory tree and shows why.
- Keyboard navigation over the stored tree; folders always listed before other node types, otherwise in
  stored order.
- Launching `app` / `path` / `command` / `url` nodes (`LaunchService` + Core's `CommandLineBuilder`),
  including `pwsh`/`powershell`/`cmd`, visible/hidden, keep-open, admin elevation, and environment
  variable expansion.
- A single-keystroke-away exit (`Ctrl+Q`) since there's no tray icon yet (Phase 5).

Implemented (Phase 2 — search, spec §7):
- `TextNormalizer` (Core): Turkish-aware, 1:1 length-preserving case/diacritic folding
  (`İ/I` → `i`/`ı`, `ş→s`, `ğ→g`, `ü→u`, `ö→o`, `ç→c`, plus a few common Latin diacritics).
- `FuzzyScorer` (Core): five deterministic tiers, best to worst — Exact, Prefix, WordStart (acronym /
  greedy word-prefix matching, e.g. `visco` → **Vis**ual Studio **Co**de), Substring, Fuzzy (scattered,
  in-order chars) — with a backtracking DP for WordStart and a leftmost-then-tightened pass for Fuzzy.
  Multi-word queries split into space-separated tokens that must all match.
- `FlatIndex` (Core): the whole tree flattened once per config load into normalized name/keywords/
  description/command text, breadcrumb, depth, and parent chain per node.
- `SearchEngine` (Core): scores name (×1.0) / keywords (×0.8, best of) / description (×0.6) / command
  text (×0.5, `command` nodes only) and keeps the best weighted field per node; sorts by score desc, then
  a pluggable usage score (defaults to 0 until Phase 6's `usage.json`), then depth asc, then Turkish
  alphabetical.
- `SearchService` (App): holds the current `FlatIndex`; `Rebuild(config)` is ready for Phase 3/5 to call
  after edits/file-watcher reloads.
- `MainViewModel`: search mode (search box non-blank) lists ranked whole-tree results with a per-row
  breadcrumb instead of the folder-relative secondary text; nav mode is unchanged. Search-mode keyboard
  behavior per spec §6.2 (see table below).
- Matched name characters are highlighted (accent + SemiBold) via an attached property
  (`Controls.HighlightedText`) that rebuilds a TextBlock's `Inlines`, so `TextTrimming` still works.
- Search time is logged via `Debug.WriteLine` (`Stopwatch` around each `SearchService.Search` call).

Implemented (Phase 3 — editing, spec §6.3/§9):
- `TreeOps` (Core): pure, in-place tree edits used by every editing flow — `FindParent`/`FindParentById`/
  `FindById`, `Add`, `Remove` (removes a folder's whole subtree with it), `MoveUp`/`MoveDown` (see below),
  `MoveTo` (cut/paste, rejects moving a folder into itself or its own subtree, no-ops if already in the
  target), `Duplicate` (deep copy via a JSON round-trip through the same source-gen context config.json
  uses, so the clone shares no mutable list/object with the original — fresh ids for it and every
  descendant, `" (copy)"` name suffix, inserted right after the original), `NewId`.
  `MoveUp`/`MoveDown` operate within the node's **display group** (folder vs. non-folder — the panel
  always lists folders first, spec §5.2) rather than raw stored order, so the visible result is always a
  clean one-step move and never appears to skip over items from the other group.
- `TargetNameHelper` (Core): suggests a display name from a raw target string — an existing directory's
  name, an exe's `FileVersionInfo.FileDescription` → `ProductName` → file name (the exe lookup itself is
  injected so Core stays free of `System.Diagnostics.FileVersionInfo`; `App.LookUpExeDescription` supplies
  the real one), any other existing file's name without extension, or a URL's host (with or without an
  explicit scheme).
- `ConfigService.Save()` (App): the single write path every mutation goes through — atomic (reuses Phase
  1's `ConfigStore`), records `LastSelfWriteUtc` on success (`// Phase 5` marks where the future
  FileSystemWatcher should consult it to ignore our own writes), and is **refused outright** when the
  on-disk config failed to load at startup (`IsReadOnly`) rather than ever overwriting that untouched
  corrupt file — every mutating entry point in `MainViewModel` checks this *before* touching the tree, so
  a corrupt config truly blocks edits rather than accepting one it can't persist. An ordinary IO failure
  on write (locked file, disk full, ...) reports "Could not save config: ..." but keeps the in-memory
  change either way.
- Panel pages (`MainViewModel.CurrentPage` / `PanelPage`): `List` (unchanged Phase 1/2 view) plus
  `TypePicker`, `Editor` (both swap in a `UserControl` under `Views/` and own their own keyboard handling
  entirely — the search box's key handler simply never has focus while they're showing) and
  `ConfirmDelete` (an inline bar at the bottom of the still-visible List page, not a separate view).
  Esc always returns to List with the previous selection intact; the breadcrumb grows a page suffix
  (`Root › Dev · New item` / `· Edit`) and the search box is hidden on TypePicker/Editor.
- Type picker (`Ctrl+N`, decision D6): 5 rows (Folder/App/File or folder/Command/URL), glyph + name + dim
  key-hint letter (F/A/P/C/U); ↑/↓ wrap, Enter or the letter selects, Esc cancels. `Ctrl+Shift+N` skips it
  and opens the editor directly for a new folder.
- Editor (`Ctrl+N` after a type, or `F2` on the selection): one `EditorViewModel` built from a node copy
  (Add: a blank node of the chosen kind; Edit: field values read out of the real node) — nothing touches
  the tree until Enter validates and saves. Fields follow spec §4.3 per type, with an "Advanced ▸"
  collapsible section (`Alt+A`, or click) that starts expanded in Edit mode if any advanced field is
  non-default. Enter saves everywhere except inside the multi-line Command box (Ctrl+Enter saves there;
  plain Enter inserts a newline); Esc cancels; Tab/Shift+Tab are plain WPF focus order. Browse…/Folder…
  use `Microsoft.Win32.OpenFileDialog`/`OpenFolderDialog` (WPF, no WinForms needed); `MainWindow`'s
  Deactivated-triggered auto-hide is suppressed for the duration via a small suppression counter
  (`BeginSuppressAutoHide`/`EndSuppressAutoHide`) so the panel doesn't vanish under the dialog. Choosing or
  leaving a Target field re-suggests Name via `TargetNameHelper` whenever Name is still blank or still
  equals the last auto-suggestion. Validation on save: blank Name → inline "Name is required", doesn't
  save; app/path target missing after env expansion → non-blocking "Target not found — saved anyway.";
  a URL Target without a scheme gets `https://` prepended. Editing doesn't support changing a node's type.
- Delete (`Delete`): inline confirm bar — "Delete 'X'? ..." or, for a non-empty folder, "Delete folder 'X'
  and its N items? ..."; Enter confirms, anything else (including Esc) cancels.
- `Ctrl+↑`/`Ctrl+↓` (nav mode only — search order is by score): `TreeOps.MoveUp`/`MoveDown`.
- `Ctrl+X`/`Ctrl+V`: marks the selection as cut (dimmed + italic in the list, hint bar shows
  "Cut: X — go to a folder and press Ctrl+V") until pasted into the current folder, another node is cut,
  or the panel is hidden; pasting a folder into itself/its own subtree shows "Cannot move a folder into
  itself." instead of moving it.
- `Ctrl+D`: duplicates the selection and selects the copy.
- After every successful mutation: save → rebuild the search index → refresh the list → reselect the
  affected node (new/edited/duplicated/moved node, or — after Delete — the item now at the same index,
  clamped). All of the above work in **both** nav and search mode except `Ctrl+↑`/`Ctrl+↓`; `Ctrl+N` while
  searching adds to the folder you searched from and then shows it there (clearing the search), while
  F2/Delete/Ctrl+D re-run the current search afterwards and keep the selection where possible.

Implemented (Phase 4 — icons, spec §4.4/§9, revised per `tasks/phase4-spec.md` §7):
- `IconKey`/`TargetCheck`/`PathResolver`/`IconFileStore` (Core, `Icons/`): a stable cache key per node/
  custom-icon-spec (null for glyph/emoji, which are text, not images); whether an app/path target is
  worth a disk existence check (never a UNC path or a URI/`shell:` target — only a fully-qualified local
  path, or a bare name like `notepad.exe` resolved against `%PATH%`×`%PATHEXT%` first); copying a
  user-picked icon file into `<config dir>\icons\` under a content hash so re-importing identical bytes
  reuses the existing copy instead of duplicating it.
- `IconService` (App): extraction runs on the thread pool (`Task.Run`, gated by a 2-wide semaphore, never
  a dedicated thread), wrapped in try/catch → null on any failure. Two caches: one keyed by `IconKey`
  (dedupes concurrent requests for the same node/spec and caches a failed lookup so it's never retried),
  one keyed by the shell's own icon *location* (the file + index that actually holds the resource) so
  e.g. every `.txt` row shares one bitmap. Auto icons use `SHGetFileInfo(SHGFI_ICONLOCATION)` →
  `SHDefExtractIconW` at 32px (48px above 125% DPI); a missing/unreachable target falls back to
  `SHGFI_USEFILEATTRIBUTES` (extension only, no disk/network access). Custom `exe`/`dll` icons use the
  same extraction, capped at 1,024 icons and filled progressively for the picker's Exe/DLL tab; custom
  `file` icons decode via `IconBitmapDecoder` (`.ico`, closest frame to the target size) or `BitmapImage`
  (everything else), both over a `FileStream` so the file is never left locked.
- Row icon (`ListItemViewModel`, `MainWindow`'s `ItemTemplate`): display priority `IconImage` →
  `EmojiText` → `Glyph`. Both `IconImage` and the missing-target check are **lazy** — the fetch only
  starts the first time the property is *read*, which for the virtualized `ListBox` means only realized
  rows ever call into `IconService` or touch the filesystem; a `_listGeneration` counter on
  `MainViewModel` discards a result that comes back after the list has since been rebuilt. A missing
  `app`/`path` target dims the row (`Opacity 0.55`) and overlays a small orange warning glyph at the
  icon's bottom-right, plus a "Target not found" tooltip.
- Icon picker (`Ctrl+I`, `IconPickerViewModel`/`IconPickerView`): four tabs — **Glyph** (a curated ~70-icon
  grid of Segoe MDL2 Assets glyphs actually present in the Windows 10 range `E700`–`E9FF`, with a filter
  box), **Emoji** (a single grapheme, `System.Globalization.StringInfo`-validated), **File** (Browse… →
  `IconFileStore.Import`), **Exe/DLL** (a path box, defaulting to the node's own target if it's an exe/dll
  else `imageres.dll`, plus a Browse…/Load pair and the extracted-icon grid). Opens on the node's current
  icon (tab + selection preselected) with a "Current: ..." preview in the header; `Ctrl+Tab`/
  `Ctrl+Shift+Tab` or `Ctrl+1..4` switch tabs, arrow keys move a fixed 10-column grid (wrapping by row/
  column) while focus stays in the tab's own filter/path/emoji box, `Ctrl+0` resets to automatic from any
  tab, `Esc` cancels. Applying saves through the same atomic-save path as the editor, invalidates that
  one cache key, and returns to the list with the same node selected.
- `EditorViewModel` gains a read-only "Icon" line (glyph/emoji + a short description, not the real bitmap
  for a custom file/exe icon — see the Phase 4 review in `tasks/todo.md` for why) plus "Ctrl+I in the list
  to change"; its "Target not found — saved anyway" warning now shares `TargetCheck.IsMissing` with the
  row badge so both always agree.

Deliberately not yet implemented (see `// TODO Phase N` markers in code):
- Tray icon, single-instance guard, config file-watcher (so `IconService.Invalidate()` has nothing to call
  it yet), Mica/Acrylic backdrop — Phase 5.
- Drag & drop, `.lnk` resolution, settings screen (`Ctrl+,`), import/export, usage-based ranking, theme
  switching, right-click context menu — Phase 6.

## Keyboard

### Nav mode (search box empty)

| Key | Behavior |
|---|---|
| `Alt+Space` (configurable) | Show/hide the panel |
| `↑` / `↓` | Move selection, wraps at the ends |
| `Enter` | Folder → enter it; anything else → launch it |
| `→` / `Tab` | Enter the selected folder (no-op on non-folders) |
| `←` / `Backspace` (when search is empty) | Go up one folder (no-op at root) |
| `Esc` | In a subfolder → go up; else at root → hide the panel |
| `Home` / `End` | Jump to first / last item |
| `PageUp` / `PageDown` | Move by `settings.maxVisibleItems` |
| Any printable character | Typed into the search box, switches to search mode |
| `Ctrl+Q` | Quit the app |

### Search mode (search box non-blank) — spec §6.2

| Key | Behavior |
|---|---|
| `↑` / `↓` | Move through ranked results, wraps at the ends; focus stays in the box |
| `Enter` | Result is a folder → clear search and navigate into it; anything else → launch it |
| `Ctrl+Enter` | Clear search, open the result's containing folder, and select it there |
| `Esc` | Clear the search box, back to nav mode in the folder you were in before searching |
| `Backspace` | Normal text deletion; box becomes empty → back to nav mode |
| `←` / `→` | Move the caret (normal TextBox behavior) - do not navigate |
| `Home` / `End` | Normal TextBox caret behavior - does not jump the list selection |
| `Tab` | No special behavior; focus stays in the search box |
| `PageUp` / `PageDown` | Page through results, same as nav mode |

### Editing shortcuts (spec §6.3, Phase 3) — act on the selected node, valid in both nav and search mode

| Key | Behavior |
|---|---|
| `Ctrl+N` | Open the type picker, then the editor, for a new node in the current folder |
| `Ctrl+Shift+N` | Skip the type picker, open the editor for a new folder directly |
| `F2` | Edit the selected node |
| `Delete` | Show the inline confirm bar; `Enter` deletes, anything else cancels |
| `Ctrl+↑` / `Ctrl+↓` | Move the node within its display group (folders vs. others) — **nav mode only** |
| `Ctrl+X` / `Ctrl+V` | Cut the node, then paste (move) it into the current folder |
| `Ctrl+D` | Duplicate the node and select the copy |
| `Ctrl+I` | Open the icon picker for the node (Phase 4) |

### Type picker page

| Key | Behavior |
|---|---|
| `↑` / `↓` | Move the selection, wraps at the ends |
| `Enter` | Create the highlighted type |
| `F` / `A` / `P` / `C` / `U` | Create Folder / App / File or folder / Command / URL immediately |
| `Esc` | Cancel, back to the list |

### Editor page

| Key | Behavior |
|---|---|
| `Tab` / `Shift+Tab` | Move between fields (normal WPF focus order) |
| `Enter` | Save (inserts a newline instead, inside the multi-line Command field) |
| `Ctrl+Enter` | Save — works everywhere, including inside the Command field |
| `Alt+A` (or click) | Toggle the "Advanced ▸" section |
| `Esc` | Cancel, back to the list with the previous selection |

### Icon picker page (`Ctrl+I`, Phase 4)

| Key | Behavior |
|---|---|
| `Ctrl+Tab` / `Ctrl+Shift+Tab` or `Ctrl+1`..`Ctrl+4` | Switch tabs (Glyph / Emoji / File / Exe/DLL) |
| `↑`/`↓`/`←`/`→` (Glyph, Exe/DLL tabs) | Move within the 10-column grid, wrapping by row/column; `←`/`→` only move it when the tab's own text box is empty, otherwise they move the caret |
| `PageUp` / `PageDown` | Jump 5 rows in the grid |
| `Enter` | Glyph/Emoji: apply the selection. Exe/DLL: load icons from the path if it changed since the last load, else apply the selected icon. File: Browse… |
| `Ctrl+0` | Reset to the automatic icon, from any tab |
| `Esc` | Cancel, back to the list with the previous selection |

## Icons (spec §4.4, Phase 4)

- `icon: null` (the default) — automatic: `folder`/`command`/`url` get a fixed type glyph; `app`/`path`
  get the target's real system icon (`SHGetFileInfo`), or a type-by-extension icon if the target can't be
  found, or a bare name like `notepad.exe` doesn't resolve on `%PATH%`.
- `icon: { "kind": "glyph", "value": "\uE8B7" }` — a Segoe MDL2 Assets glyph. Only code points the picker
  itself curates (the Windows-10-safe `E700`–`E9FF` range) are accepted as valid; anything else in a
  hand-edited config falls back to the type default.
- `icon: { "kind": "emoji", "value": "🚀" }` — a single emoji/symbol, rendered with Segoe UI Emoji.
- `icon: { "kind": "file", "value": "icons\\<16-hex-hash>.png" }` — a custom PNG/ICO/JPG/BMP/GIF, copied
  into `<config dir>\icons\` (named by content hash, so re-picking the same file reuses the existing copy)
  when chosen via the picker's File tab. The stored value is **relative to the config directory** so the
  whole config folder stays portable; an absolute path (e.g. hand-edited) also still works.
- `icon: { "kind": "exe", "value": "C:\\...\\a.exe", "index": 0 }` — one icon out of an exe/dll, picked
  visually in the Exe/DLL tab (or typed by hand with a known index; negative indexes address a resource ID
  directly, same as `ExtractIconEx`).

Change any node's icon with `Ctrl+I` on it in the list. Orphaned files left behind in `icons\` (e.g. after
switching a node away from a custom file icon) are not cleaned up automatically — deleting the folder is
safe, it's only ever read from and copied into, never relied on for anything else.

## Config

Location: `%APPDATA%\Your Launcher\config.json` (override with the `YOURLAUNCHER_CONFIG_DIR`
environment variable). Comments and trailing commas are accepted; unknown/missing fields are ignored /
defaulted, never fatal. See `config.example.json` for a populated example (nested folders, an app with
an environment-variable path, a `path` node, visible/hidden commands, a URL).

Shape (spec §4.2–§4.5):

```jsonc
{
  "version": 1,
  "settings": {
    "hotkey": "Alt+Space",
    "theme": "system",           // system | light | dark (Phase 6)
    "startWithWindows": true,     // Phase 5
    "closeAfterLaunch": true,
    "maxVisibleItems": 8,
    "defaultShell": "pwsh",       // pwsh | powershell | cmd
    "rememberLastLocation": false,
    "showHintBar": true
  },
  "root": {
    "id": "root", "type": "folder", "name": "Root",
    "children": [
      { "id": "...", "type": "folder", "name": "Dev", "children": [ /* ... */ ] },
      { "id": "...", "type": "app", "name": "VS Code", "target": "...", "arguments": "", "workingDirectory": null, "runAsAdmin": false },
      { "id": "...", "type": "path", "name": "Documents", "target": "%USERPROFILE%\\Documents" },
      { "id": "...", "type": "command", "name": "Build", "command": "git pull && npm run build", "shell": "pwsh", "window": "visible", "keepOpen": true, "runAsAdmin": false },
      { "id": "...", "type": "url", "name": "Anthropic", "target": "https://www.anthropic.com" }
    ]
  }
}
```

Every node also accepts optional `icon` (see "Icons" above), `keywords` and `description` fields — the
latter two are searched (weighted below name, see "Search" below). `config.example.json` has a few
populated to demonstrate keyword search.

## Search (spec §7, Phase 2)

Typing into the search box searches the **whole tree**, not just the current folder. Matching is
Turkish-aware (`sifre` finds "Şifre", `IZMIR` finds "İzmir") and case-insensitive, and scores in five
deterministic tiers, best to worst:

1. **Exact** — the field equals the query.
2. **Prefix** — the field starts with the query.
3. **WordStart** — every query char matches, in order, at word starts (spaces/`-_./\(`/camelCase), e.g.
   `vsc` as an acronym, or `visco` as **Vis**ual Studio **Co**de (greedy word-prefix runs).
4. **Substring** — the query occurs contiguously anywhere in the field.
5. **Fuzzy** — the query's chars occur scattered but in order (e.g. `vsc` inside "**v**i**s**ual basi**c**").

Each node is scored on `name` (weight 1.0), the best-matching `keywords` entry (0.8), `description`
(0.6), and — for `command` nodes — the command text (0.5); the best weighted field wins and only that
field's matched positions are highlighted. Ties break by: usage score (0 for everyone until Phase 6's
`usage.json`), then tree depth (shallower first), then Turkish alphabetical order. The whole tree is
flattened once into a `FlatIndex` per config load, not re-walked per keystroke; 5,000 nodes stay well
under the spec's 16 ms budget (median ~8 ms / p95 ~10 ms measured in `SearchPerformanceTests`).

## Key decisions (D1–D8, see `tasks/todo.md` §0 for full rationale)

- **D1** — .NET 10 (not the spec's .NET 8): only SDK 10.0.401 is installed, and .NET 8 support ends
  2026-11-10.
- **D2** — App name "Your Launcher"; project/namespace names are the boxed `YourLauncher`.
- **D3** — CommunityToolkit.Mvvm for MVVM (source-generated, no reflection cost).
- **D4** — Tray will use WinForms `NotifyIcon` when it's built in Phase 5 (not added yet — see the note
  in `Launcher.App.csproj` about why `UseWindowsForms` is deliberately left off for now).
- **D5** — System.Text.Json with a source-generated context; polymorphic node reading is done with a
  small hand-written converter (`NodeJsonConverter`) rather than STJ's built-in
  `[JsonPolymorphic]`/`[JsonDerivedType]`, because that built-in mechanism's source-generated path only
  recognizes the `"type"` discriminator when it is the *first* JSON property — and config.json is
  hand-edited, with the spec's own examples putting `"id"` before `"type"`. The converter buffers each
  node and reads `"type"` from wherever it appears.
- **D6** — The type picker (Phase 3) uses glyph icons per type plus a dim key-hint letter, not
  letter-only shortcuts.
- **D7** — Publish as self-contained single-file win-x64, ReadyToRun on, trimming off (WPF doesn't
  support trimming). Not exercised in Phase 1; `dotnet publish` command is in `tasks/todo.md`.
- **D8** — All UI text is English; the spec is Turkish. Turkish-character-insensitive search
  normalization (Phase 2, `TextNormalizer`) follows the spec as written.

## Solution layout

```
YourLauncher.sln
Directory.Build.props           # Nullable, TreatWarningsAsErrors, LangVersion latest, ImplicitUsings
src/Launcher.Core/               # net10.0, no UI references
  Model/                         # Node + FolderNode/AppNode/PathNode/CommandNode/UrlNode, Settings, LauncherConfig, IconSpec
  Json/                          # LauncherJsonContext (source-gen), NodeJsonConverter, camelCase enum converter
  Config/                        # ConfigSerializer, ConfigStore (atomic save + backup), TreeOps, TargetNameHelper
  Launch/                        # EnvExpander, LaunchPlan, CommandLineBuilder
  Search/                        # TextNormalizer, FuzzyScorer, FlatIndex, SearchEngine
  Icons/                         # IconKey, TargetCheck, PathResolver, IconFileStore (Phase 4)
src/Launcher.App/                # net10.0-windows, WPF
  Interop/Win32.cs               # RegisterHotKey, cursor/monitor, SHGetFileInfo/ExtractIconEx/SHDefExtractIconW/DestroyIcon
  Services/                      # HotkeyService, ConfigService, LaunchService, SearchService, IconService, IconGlyphs
  ViewModels/                    # MainViewModel, ListItemViewModel, PanelPage, TypePickerViewModel, EditorViewModel, IconPickerViewModel
  Views/                         # MainWindow (the panel), TypePickerView, EditorView, IconPickerView
  Converters/                    # StringEmptyToVisibilityConverter, StringNonEmptyToVisibilityConverter,
                                  # UrlOrTargetLabelConverter, AdvancedToggleTextConverter, NullToVisibilityConverter,
                                  # IconTierVisibilityConverter, MissingTargetTooltipConverter
  Controls/                      # HighlightedText (attached property for match highlighting)
tests/Launcher.Core.Tests/       # xUnit: config round-trip, ConfigStore, CommandLineBuilder matrix, Search/, TreeOps,
                                  # TargetNameHelper, Icons/ (IconKey, IconFileStore, TargetCheck, PathResolver)
config.example.json
```
