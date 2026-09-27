# Your Launcher

A keyboard-first Windows launcher. Nothing is indexed automatically — you build your own tree of
folders, apps, files/paths, shell commands and URLs, and open it instantly with a global hotkey.

This is **Phase 6 (Polish)** per `tasks/todo.md`: Phase 1's hotkey/panel/navigation/launching, Phase 2's
fuzzy Turkish-aware whole-tree search, Phase 3's fully keyboard-driven add/edit/delete/move/cut-paste/
duplicate of nodes, Phase 4's automatic system icons + `Ctrl+I` icon picker, Phase 5's single-instance
guard/tray/"Start with Windows"/live config watching/DPI-correct positioning/Mica-Acrylic panel, Phase 6
Part A's dark/light theme (`Ctrl+,` settings page included), usage-based search tie-breaking, and config
import/export, plus Phase 6 Part B's mouse prerequisite (click-to-select, double-click-to-open), right-click
context menu, drag & drop of files/`.lnk`/`.url` from Explorer or the Start menu, and in-list drag to
reorder/move into a folder (see `launcher-spec.md` §5-§9/§14 and `tasks/phase6-spec.md` for the full spec
these implement).

## Build / run / test

```powershell
dotnet build                     # whole solution, warnings are errors
dotnet test                      # Launcher.Core.Tests (xUnit)
dotnet run --project src/Launcher.App
```

Requires .NET SDK 10.0.401+ (Windows). The solution is `YourLauncher.sln`.

To point the app at a different config directory (used for the corrupt-config smoke test, or to keep a
throwaway config while developing), set `YOURLAUNCHER_CONFIG_DIR` before launching. This also disables the
"Start with Windows" registry sync for that run (so a scratch/test run never touches the real
`HKCU\...\Run` value); `YOURLAUNCHER_NO_STARTUP_REG=1` disables just that sync without redirecting the
config directory. Registry sync is also always off in Debug builds.

```powershell
$env:YOURLAUNCHER_CONFIG_DIR = "C:\scratch\yl-config"
dotnet run --project src/Launcher.App
```

## Install

Build a standalone copy with the exact command the project is published/measured with (spec §15, D7 — Table
below):

```powershell
dotnet publish src/Launcher.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=true
```

The single self-contained exe lands at
`src\Launcher.App\bin\Release\net10.0-windows\win-x64\publish\YourLauncher.exe` — copy just that file
anywhere you like (a few native WPF DLLs and `.pdb`s sit next to it in the publish folder but aren't required
next to the exe once copied elsewhere; `.pdb`s are only for crash diagnostics). No installer — run it
directly, or right-click → "Pin to taskbar"/create your own shortcut.

**First run**: `%APPDATA%\Your Launcher\config.json` doesn't exist yet, so the app creates it with an empty
root and default settings (`Alt+Space` hotkey, dark-follows-system theme, `Start with Windows` off), then
shows the panel once so you can see it worked. The tray icon appears immediately; `Ctrl+N`/`Ctrl+Shift+N`
build your tree from there, or hand-edit `config.json` (see "Config" below) — `config.example.json` is a
good starting point to copy in.

**Start with Windows** isn't on by default — turn it on from the settings page (`Ctrl+,` → "Start with
Windows" toggle) or the tray menu's **Settings…**; it writes `HKCU\...\Run\Your Launcher` immediately and
keeps it in sync with `settings.startWithWindows` on every subsequent startup/config reload (see "Tray,
single instance, startup, and file watching" below).

## Phase 1–6 status

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
- A single-keystroke-away exit (`Ctrl+Q`), also available from the tray icon's **Exit** item (Phase 5).

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
  a usage score from `UsageScorer` (frecency: use count × an age-bucket weight read from `usage.json`,
  Phase 6), then depth asc, then Turkish alphabetical.
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

Implemented (Phase 5 — system integration, spec §1–§8/§9, revised per `tasks/phase5-spec.md` §10):
- **Single instance** (`Services/SingleInstanceService.cs`): a named mutex keyed by a SHA-256 hash of the
  config directory (`Local\YourLauncher-<12 hex>`), so a `YOURLAUNCHER_CONFIG_DIR` test run never collides
  with the real instance. The first instance also runs a `NamedPipeServerStream` (`PipeOptions.
  CurrentUserOnly`) loop; a second instance connects, sends `"show"`, and exits before ever creating a
  window. `MainWindow.RequestShow()` re-foregrounds an already-visible panel without resetting it to root
  (only a from-hidden show does); foreground is force-taken via `SetForegroundWindow` + an Alt key-press
  fallback for the foreground-lock timeout, and the panel hides again rather than sit unfocused if even
  that fails.
- **Tray icon** (`Services/TrayService.cs`): **own hand-written `Shell_NotifyIcon` interop**, not WinForms
  `NotifyIcon` — see decision D4 below for why. Tooltip shows the current hotkey; left-click/double-click
  shows the panel; right-click opens a dark-styled WPF `ContextMenu` at the cursor (Show, Settings…, Open
  config file, Open config folder, Reload config, Restore from backup/Keep my current version when
  applicable, Exit). Balloon notifications (`NIF_INFO`) for a non-zero hidden-command exit code, a hotkey
  registration failure, and a failed config reload. Re-adds itself on Explorer restart
  (`TaskbarCreated`); `NIM_DELETE` on exit so no ghost icon is left behind.
- **Start with Windows** (`Services/StartupService.cs` + Core's `Startup/StartupSync.cs`): syncs
  `HKCU\...\Run` value `"Your Launcher"` to `settings.startWithWindows` on every startup and after a
  config reload — writes (or rewrites, if the exe moved) when enabled, deletes when disabled, no-ops
  otherwise. The registry write/delete/none *decision* is pure Core logic, unit tested including quoting
  and case-insensitive path comparison; the actual registry IO is skipped entirely in Debug builds, when
  `YOURLAUNCHER_CONFIG_DIR` is set, or when `YOURLAUNCHER_NO_STARTUP_REG=1` — the real Run value is never
  touched by a dev/test run.
- **Config file watching + reload + recovery** (`Services/ConfigWatcherService.cs`, AC10/AC11): a
  `FileSystemWatcher` on config.json, debounced 200 ms, with a byte-hash check against the last write this
  instance itself made (not a timestamp guess) so our own saves never trigger a spurious reload. A reload
  applies onto the *existing* config instance in place — the folder stack stays in the same folder if it
  still exists, the search index and icon cache are rebuilt/invalidated, cut state clears. If the file
  can't be parsed, it's re-read once more after 500 ms (Notepad-style truncate-then-write tolerance)
  before being declared genuinely corrupt: the in-memory tree is kept, the panel goes read-only with an
  error line, and a tray balloon fires. Recovery is one keystroke away (`Ctrl+Shift+R`, or the matching
  tray menu item):
  - **Reload found a corrupt file, but we already had good data** → **"Keep my current version"**:
    archives the corrupt file as `config.corrupt-<yyyyMMdd-HHmmss>.json` and re-saves the in-memory tree
    over it.
  - **The file was already corrupt at startup** (no good in-memory data yet) → **"Restore from backup"**:
    archives the corrupt file the same way, then copies `config.backup.json` over it and reloads.
  - A reload that arrives while the Editor/TypePicker/IconPicker is open is deferred until you return to
    the list (or hide the panel); attempting to save at that point is refused ("your edit was not saved;
    reopen and redo it") rather than silently overwriting the external change.
  - `Ctrl+R` (or the tray's "Reload config") re-reads config.json on demand, same path as the watcher.
- **Hotkey failure**: a tray balloon ("Hotkey X is already in use..." or "...is not a valid key
  combination") in addition to the existing panel error line; re-registering on a config reload
  unregisters the old hotkey first and restores it if the new one fails to register.
- **DPI-correct positioning**: the panel centers on the monitor under the cursor using *that* monitor's own
  DPI (`GetDpiForMonitor`) rather than the window's current DPI, so a hotkey press with the cursor on a
  differently-scaled monitor no longer mis-centers the panel; re-runs on `OnDpiChanged`. Verified by code
  review only (this development machine has a single monitor).
- **Mica/Acrylic + rounded corners** (Windows 11 build ≥ 22000, backdrop needs ≥ 22621): applied via
  `WindowChrome` + `DwmSetWindowAttribute` (`DWMWA_WINDOW_CORNER_PREFERENCE`, `DWMWA_SYSTEMBACKDROP_TYPE
  = DWMSBT_TRANSIENTWINDOW`, `DWMWA_USE_IMMERSIVE_DARK_MODE`) once the HWND exists. Any failure along the
  way (Windows 10, or a DWM call not returning `S_OK`) leaves the original solid `#1E1E1E` panel background
  and square corners untouched — readability over effect.
- **Idle memory** (Release only): `ConcurrentGarbageCollection=false`, `TieredPGO=false`,
  `UseSystemResourceKeys=true`, `SatelliteResourceLanguages=en` in `Launcher.App.csproj`. No working-set
  trimming hack — see "Memory" below for measured numbers.

Implemented (Phase 6 Part A — polish: theme, settings, import/export, usage, spec §2/§8.4/§10/§11):
- **Theme** (`Services/ThemeService.cs`, `Themes/Dark.xaml` + `Themes/Light.xaml`): every color in the app
  (panel background/tint, border, foreground, secondary text, accent, selection, hover, error, warning,
  missing-target badge, input background) is a named brush key shared by both dictionaries; swapping which
  one is merged into `Application.Resources` re-themes the whole panel through `DynamicResource` bindings
  (or `SetResourceReference` for the handful of things set from code: `HighlightedText`'s match-accent runs,
  `MainWindow.RootBorder`'s Mica/Acrylic tint, the tray context menu's styles, rebuilt fresh from the
  current theme's resources on every open). `Theme.System` reads
  `HKCU\...\Personalize\AppsUseLightTheme` (missing value = light, the Windows default); while System is
  the configured setting, `MainWindow`'s existing `HwndSource` hook also watches for
  `WM_SETTINGCHANGE`/`"ImmersiveColorSet"` and re-resolves live if the user flips Windows' own light/dark
  mode. `DWMWA_USE_IMMERSIVE_DARK_MODE` is re-applied by `MainWindow.ApplyTheme(bool dark)` whenever the
  effective theme changes (settings save, a config reload that changed `settings.theme`, or a live
  System-mode OS change).
- **Settings page** (`Ctrl+,`, `ViewModels/SettingsViewModel.cs` + `Views/SettingsView.xaml`, `PanelPage.
  Settings`): every field from spec §11 (Hotkey, Theme, Start with Windows, Close after launch, Visible
  rows, Default shell, Remember last location, Show hint bar, Open config folder/Export…/Import…/Import
  bookmarks… — see "Bookmark import" below), fully
  keyboard-usable (Tab order top-to-bottom, Enter/Ctrl+S save unless a ComboBox dropdown is open, Esc
  cancels, Space toggles the focused checkbox, ↑/↓ in Visible rows adjust with a 3-20 clamp). Opened by
  `Ctrl+,`, the tray's **Settings…**, or a hotkey-registration failure (which also focuses the Hotkey box
  with the error already showing). The **Hotkey** field is a capture box: focusing it unregisters the live
  global hotkey (so `WM_HOTKEY` doesn't eat the combination being typed) via an App-level seam
  (`MainViewModel`/`SettingsViewModel` stay Win32-free); the next key combination with ≥1 modifier
  (Ctrl/Alt/Shift — Win is never offered, it's reserved by Windows) plus a non-modifier key is shown in the
  same text form `HotkeyService.TryParse` accepts (e.g. `Ctrl+Alt+K`); Tab/Esc/Enter/Backspace keep their
  normal page-level meaning instead of being captured (Backspace resets to the currently-saved value).
  Saving re-registers the hotkey (restoring the old one on failure, same error text as the tray-balloon
  path) *before* writing settings to config.json; StartWithWindows/theme are applied afterward through an
  App-level `SettingsApplied` event, everything else (maxVisibleItems/showHintBar/closeAfterLaunch/
  defaultShell) through the same `ApplySettings()` projection a config reload also uses. Read-only (corrupt
  config) → the page still opens (with the read-only error already showing and Export/Import disabled),
  but Save is refused.
  - **§1.2 Remember last location**: when on, `ShowLauncher()` (via `MainViewModel.ResetToRoot()`) reopens
    the folder that was current at the last hide, by id (falling back to root if it no longer exists,
    exactly like a config reload's folder-stack rebuild), instead of always going to root. In-memory only —
    not persisted across restarts.
- **Usage statistics** (spec §8.4, `Core/Usage/` + `Services/UsageService.cs`): every successful launch
  (all node types except folders — folders never reach `LaunchService.Launch`) bumps `useCount`/
  `lastUsedUtc` for that node id in `<config dir>\usage.json`, a debounced (2 s) atomic write kept
  completely separate from config.json (which stays hand-editable and clean). `UsageScorer.Score` is a
  deterministic frecency (`useCount × ageWeight`, weight buckets ≤1 day ×4 / ≤7 days ×2 / ≤30 days ×1 /
  older ×0.5) fed into `SearchEngine` as its existing usage tie-breaker parameter — it only ever breaks a
  tie between two equally-scored search results, navigation-mode order is unaffected. Pruned (dropping
  entries for ids no longer in the tree) once at startup and again after a delete or an import. A missing
  or corrupt usage.json starts empty rather than crashing or blocking; a Windows shutdown that skips the
  app's normal exit path can lose up to the last 2 seconds of unflushed usage (documented limitation, spec
  §10 revision item 10).
- **Import / Export** (spec §10/§11, `Core/Config/ConfigImport.cs` + the Settings page's Export…/Import…
  buttons): **Export** writes the current config (settings + tree) with the existing serializer via a
  `SaveFileDialog` (default name `your-launcher-export-yyyyMMdd.json`). **Import** parses the chosen file
  with the existing tolerant loader; on success an inline panel prompt (`PanelPage.ConfirmImport`, the same
  bottom-bar pattern as delete confirmation) offers **M**erge / **R**eplace / **Esc** cancel:
  - **Merge** appends the imported root's children to the current root; any imported id that collides with
    an id already in either tree (including a collision introduced earlier in the same merge) gets a fresh
    one. No folder-name merging — an imported "Dev" becomes a second sibling "Dev", it does not fold into
    an existing one.
  - **Replace** swaps in the imported tree wholesale; **current settings are kept** (importing someone
    else's hotkey/startup flag would be surprising) - only `Root` changes, `Settings` is untouched.
  - Either way: normal post-edit refresh (save, rebuild the search index, prune usage.json, back to root),
    with a status line ("Imported N items (merged|replaced).") reusing the same message row as errors/
    warnings elsewhere. Read-only → refused; Export/Import are both disabled in read-only mode too.

Implemented (Phase 6 Part B — mouse, context menu, drag & drop, spec §5-§9/§14):
- **Mouse prerequisite**: every `ListBoxItem` is `Focusable="False"`, so a click never steals keyboard focus
  away from the search box. Left-click selects a row; double-click opens it (folder → enter it, anything
  else → launch it); right-click selects the row first, then opens its context menu. Opening any menu
  suppresses the panel's `Deactivated` auto-hide the same way the Browse…/tray menus already do.
- **Context menu** (right-click a list item, also works on search results): **Open**, **Edit** (`F2`),
  **Change icon** (`Ctrl+I`), **Cut** (`Ctrl+X`), **Paste here** (`Ctrl+V`, enabled only while a node is
  cut), **Duplicate** (`Ctrl+D`), **Delete** (`Del`), a separator, then **Open file location** — enabled
  only for an `app`/`path` node whose target actually resolves (same `EnvExpander` → `PathResolver`/
  `TargetCheck` chain the missing-target badge uses); it runs `explorer.exe /select,"<path>"`, which for a
  directory target opens its parent with the directory itself selected. Every item is disabled in read-only
  mode except Open. Right-click on empty list space (or the empty-folder message) instead shows **Paste
  here** / **New item** (`Ctrl+N`) / **New folder** (`Ctrl+Shift+N`). Every menu item calls the exact same
  `MainViewModel` method its keyboard shortcut does — no separate logic path — and the menu itself is built
  fresh from a shared theme-aware style factory (`Services/ThemedMenuFactory.cs`, also used by the tray
  menu) so it always matches the current theme.
- **Drag & drop from Explorer/Start menu** (AC12): dropping one or more files onto the panel adds them to
  the current folder, in drop order, with one save at the end and the last-dropped item selected. Accepted
  only on the List page (not search results, not any other panel page) and refused outright in read-only
  mode. Mapping (`Core/Config/DropMapper.cs`, resolved via `Interop/ShellLink.cs`'s hand-written
  `IShellLinkW`/`IPersistFile` interop for `.lnk`):
  - `.lnk` with a resolvable target → **app** node if the target is `.exe`/`.bat`/`.cmd`/`.com`/`.msc`,
    **path** node otherwise; arguments/working directory/icon carried over; an *advertised* (MSI) shortcut
    with no resolvable target becomes an **app** node pointing at the `.lnk` file itself (so it still
    launches the way double-clicking it in Explorer would).
  - `.url` (Internet Shortcut) → **url** node, target read from its `[InternetShortcut]` section.
  - `.exe`/`.bat`/`.cmd`/`.com`/`.msc` (dropped directly) → **app** node.
  - Anything else (any other file, or a directory) → **path** node.
  - Every mapped node's name comes from the same lookup the editor's Target field uses
    (`TargetNameHelper`/exe `FileDescription`), and environment-variable/relative paths in a `.lnk`'s raw
    target are preserved exactly as `IPersistFile.GetPath(SLGP_RAWPATH)` returns them (no `.Resolve()` call).
  - **Known limitation**: clicking on Explorer to start a drag also fires the panel's `Deactivated` event —
    indistinguishable at that instant from a plain click elsewhere. The panel defers hiding for up to ~50 ms
    while the left mouse button is still down, giving a *press-and-drag in one continuous motion* time to
    reach the panel; a plain click that isn't followed by a drag still hides the panel as before, just
    delayed by up to one poll interval. After a drop, the panel is always left either foreground or hidden —
    never a visible-but-deactivated `Topmost` window.
- **In-list drag** (reorder / move into a folder): navigation mode only (not search results, not any other
  panel page), disabled in read-only mode, using `DragDrop.DoDragDrop` with a private data format so it
  never collides with the Explorer file-drop path above. Dragging past the system drag threshold
  (`SystemParameters.MinimumHorizontal/VerticalDragDistance`) shows an insertion line above/below the row
  under the cursor; hovering over the **middle 50%** of a folder row instead highlights the whole row as
  "move into". Dropping between rows calls `TreeOps.MoveToGroupIndex` (reorders within the node's own
  display group — folders vs. everything else — clamped to that group's bounds, so a node never jumps
  across the folders/items boundary); dropping into a folder calls the existing `TreeOps.MoveTo`. One save
  at the end; the dragged node stays selected.

## Keyboard

### Nav mode (search box empty)

| Key | Behavior |
|---|---|
| `Alt+Space` (configurable) | Show/hide the panel |
| `↑` / `↓` | Move selection, wraps at the ends |
| `Enter` | Folder → enter it; anything else → launch it |
| `→` / `Tab` | Enter the selected folder (no-op on non-folders) |
| `←` / `Backspace` (when search is empty) | Go up one folder (no-op at root) |
| `Esc` | Hide the panel (from any folder — `Backspace` goes up a folder) |
| `Home` / `End` | Jump to first / last item |
| `PageUp` / `PageDown` | Move by `settings.maxVisibleItems` |
| Any printable character | Typed into the search box, switches to search mode |
| `Ctrl+Q` | Quit the app |
| `Ctrl+R` | Reload config.json from disk now (Phase 5) |
| `Ctrl+Shift+R` | Recover from a corrupt config.json — "keep current version" or "restore from backup", whichever applies (Phase 5) |
| `Ctrl+,` | Open the settings page (Phase 6) |

### Mouse (Phase 6 Part B, spec §5)

| Action | Behavior |
|---|---|
| Click a row | Selects it (never steals focus from the search box — rows are `Focusable="False"`) |
| Double-click a row | Opens it — folder → enter it, anything else → launch it |
| Right-click a row | Selects it, then opens its context menu: **Open**, **Edit** (`F2`), **Change icon** (`Ctrl+I`), **Cut** (`Ctrl+X`), **Paste here** (`Ctrl+V`, only while a node is cut), **Duplicate** (`Ctrl+D`), **Delete** (`Del`), then **Open file location** (app/path only, only when the target resolves) |
| Right-click empty list space | **Paste here** (`Ctrl+V`), **New item** (`Ctrl+N`), **New folder** (`Ctrl+Shift+N`) |
| Drag a file/`.lnk`/`.url`/folder from Explorer onto the panel | Adds it to the current folder (List page only, not read-only — see "Drag & drop" above) |
| Drag a row within the list | Reorders it, or moves it into a folder dropped on (nav mode only, not read-only — see "Drag & drop" above) |

Edit-related menu items and both drag types are disabled/refused in read-only mode; the rest (Open, Open
file location) still work.

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

### Settings page (`Ctrl+,`, Phase 6)

| Key | Behavior |
|---|---|
| `Tab` / `Shift+Tab` | Move between fields (normal WPF focus order) |
| Any key with ≥1 modifier + a non-modifier key, while the Hotkey box has focus | Captures that combination as the new hotkey (Win is never offered as a modifier) |
| `Backspace`, while the Hotkey box has focus | Resets the box back to the currently-saved hotkey |
| `Space`, on a focused checkbox | Toggles it (normal WPF behavior) |
| `↑` / `↓`, while the Visible rows box has focus | Adjusts the value by 1, clamped 3-20 |
| `Enter` | Save, unless a ComboBox dropdown is currently open (lets it commit/close instead) |
| `Ctrl+S` | Save, unconditionally |
| `Esc` | Cancel, back to the list |

### Import confirm bar (Settings page's Import…, Phase 6)

| Key | Behavior |
|---|---|
| `M` | Merge - append the imported tree, renaming any colliding ids |
| `R` | Replace - swap in the imported tree wholesale, keeping current settings |
| Any other key (including `Esc`) | Cancel, nothing imported |

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

## Tray, single instance, startup, and file watching (Phase 5)

- **Only one instance ever runs.** Launching a second copy asks the first one to show its panel (over a
  named pipe) and exits immediately without creating a window.
- **Tray icon** (bottom-right, may be in the overflow flyout on Windows 11): left-click/double-click shows
  the panel; right-click opens a menu — **Show**, **Settings…** (opens the settings page, Phase 6),
  **Open config file**, **Open config folder**, **Reload config**, **Restore from backup** / **Keep my
  current version** (only shown while config.json is corrupt), **Exit**.
- **Start with Windows** follows `settings.startWithWindows` in config.json, and is also a checkbox on the
  settings page (Phase 6) — either way it takes effect immediately (settings page) or on the next startup/
  config reload (hand-edited config.json).
- **External edits to config.json are picked up live** — no restart needed. If you break the JSON while
  editing by hand, the panel keeps showing what it had before, goes read-only, and tells you exactly what
  to do next:
  - Corrupted config.json while the app already had a good tree loaded → error line says "kept the
    previous version"; `Ctrl+Shift+R` (or the tray's **Keep my current version**) archives the broken file
    as `config.corrupt-<timestamp>.json` next to it and writes your last-known-good tree back out.
  - Corrupted config.json already at startup → error line says the file couldn't be read; `Ctrl+Shift+R`
    (or the tray's **Restore from backup**) archives the broken file the same way and restores
    `config.backup.json` instead (present once you've saved at least once before).
  - `Ctrl+R` (or the tray's **Reload config**) re-reads config.json on demand at any time.
- A hidden (`window: hidden`) command that exits with a non-zero code pops a tray balloon naming it and the
  exit code, since there's no visible window to show the failure otherwise.

## Config

Location: `%APPDATA%\Your Launcher\config.json` (override with the `YOURLAUNCHER_CONFIG_DIR`
environment variable). Comments and trailing commas are accepted; unknown/missing fields are ignored /
defaulted, never fatal. See `config.example.json` for a populated example: every node type (`folder`
(nested three levels deep), `app` with an environment-variable path, `path`, `command` — one example per
supported shell, `pwsh`/`powershell`/`cmd` — and `url`), plus `keywords`/`description` on a few nodes for
search.

Usage statistics (Phase 6, spec §8.4) live in a separate `usage.json` next to config.json - never in
config.json itself, so a hand-edited config stays free of frequently-changing data. It's keyed by node id
(`{ "entries": { "<id>": { "useCount": 3, "lastUsedUtc": "..." } } }`), written debounced/atomically, and
only ever consulted as a search-ranking tie-breaker (see "Search" below) - deleting it is always safe, it
starts back at zero.

**Import/export** (Phase 6, the settings page's Export…/Import… buttons): Export writes the exact shape
below (settings + tree) to a `.json` file you choose. Import parses a chosen file the same tolerant way
config.json itself is read, then asks Merge (append, renaming any colliding ids) or Replace (swap the tree,
keep your current settings) - see the "Settings page" section above for the exact prompt and keys. **Merging
your own export back in duplicates everything** — Merge has no idea two folders named "Dev" mean the same
thing, so it always appends the imported tree as new siblings (with fresh ids for anything that collides)
rather than folding it into what's already there; only use Merge to bring in a *different* tree (e.g. from
another machine), and use Replace (or just don't import) if you meant to restore your own export.

Shape (spec §4.2–§4.5):

```jsonc
{
  "version": 1,
  "settings": {
    "hotkey": "Alt+Space",
    "theme": "system",           // system | light | dark
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

Every non-folder node also accepts `"confirmLaunch": true` (editor: **Ask before launching**). Launching
such a node — Enter, double-click or the context menu's Open — first shows an inline bar
"Launch '<name>'? Enter = launch, Esc = cancel"; any key other than Enter cancels. Meant for items like
shut down / restart / log out. The field is omitted from config.json while false.

## Bookmark import

The settings page's **Import bookmarks…** button (next to Export…/Import…, same read-only rule) imports
bookmarks from a real browser into the tree, as a new folder. One-time import — there's no ongoing sync
back to the browser, and nothing is ever written to the browser's own files (they're only ever read).

Clicking it opens a menu:

- Every Chromium-family profile with bookmarks found on this machine, auto-detected: **Chrome**, **Edge**,
  **Brave**, **Vivaldi**, **Chromium** (each profile under `%LOCALAPPDATA%\<browser>\User Data\`), plus
  **Opera**/**Opera GX** (`%APPDATA%\Opera Software\Opera <Stable|GX Stable>`, single profile). A browser
  with more than one profile shows each as e.g. "Chrome (Work)"; a single-profile browser just shows
  "Chrome". Browsers that aren't installed, or whose profile can't be read, are silently left off the list.
- **From HTML file…** — any browser's own "export bookmarks to HTML" file (Netscape bookmark format),
  which covers browsers the auto-detection above doesn't reach, most notably **Firefox**: in Firefox,
  Library (`Ctrl+Shift+O`) → Import and Backup → **Export Bookmarks to HTML…**, then pick that file here.
  Chrome/Edge/Safari can export the same way if you'd rather point at a file than use the profile menu.

Either way, the browser's bookmark folders become a new folder named "`<Browser>` bookmarks" (or
"Bookmarks (`<file name>`)" for an HTML import), placed in whatever folder the panel was showing when you
opened Settings — no Merge/Replace prompt, it's applied immediately. `javascript:` bookmarklets are
skipped, and any folder that ends up with nothing in it (recursively) is dropped rather than imported empty.

**Re-importing the same source replaces the previous import** in place — it finds its own previously
imported folder (even if you've since renamed or moved it) and swaps just its contents, keeping the name/
icon/position you gave it; it does not create a second copy. Importing a *different* source (a different
browser, profile, or HTML file) always adds a new folder alongside whatever's already there.

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
field's matched positions are highlighted. Ties break by: usage score (frecency from `usage.json`, Phase
6 — see "Usage statistics" under Config), then tree depth (shallower first), then Turkish alphabetical
order. The whole tree is
flattened once into a `FlatIndex` per config load, not re-walked per keystroke; 5,000 nodes stay well
under the spec's 16 ms budget (median ~8 ms / p95 ~10 ms measured in `SearchPerformanceTests`).

## Memory and publish size (Phase 5/6, spec §8/§15)

Measured on a self-contained, single-file, ReadyToRun Release publish:

```powershell
dotnet publish src/Launcher.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=true
```

| Metric | Measured | Target |
|---|---|---|
| Published exe size (`YourLauncher.exe`) | ~134 MB (140,262,650 bytes) | (not targeted) |
| Working Set, idle | ~176 MB | < 80 MB |
| Private Bytes, idle | ~104 MB | (not separately targeted) |

(Working Set/Private Bytes: process launched with `YOURLAUNCHER_CONFIG_DIR` pointed at a scratch config
directory, panel left showing, measured via `Get-Process` after a 15 s settle, Phase 6 remeasurement — an
earlier Phase 5 measurement that hid the panel before settling read ~148 MB/~85 MB; either way the number is
well above target for the reason below, so the exact idle/shown split isn't the deciding factor.)

**Target missed.** No working-set-trimming hack was applied to game this number (`SetProcessWorkingSetSize`
was deliberately left out — see spec §8's own risk note, `tasks/phase6-spec.md` §10 item 14, and
`tasks/todo.md`'s Phase 5 review); the Release build only turns off concurrent GC/TieredPGO and satellite
resource languages (`Launcher.App.csproj`). A self-contained WPF app carries its own CLR + WPF renderer
regardless of app size, and that baseline is well above 80 MB in practice — the original plan flagged this
as a tight-to-miss target back at the start (`tasks/todo.md` §0 risks: "WPF boşta bellek ~50–70 MB; hedef
sınırda"). Shrinking this further (e.g. trimming, a non-self-contained/framework-dependent publish, or a
non-WPF UI stack) is a larger change than this project's scope and is left as a known limitation.

## Known limitations

A quick-reference list — most of these are explained in more detail in the section named alongside them:

- **Idle memory is ~176 MB Working Set / ~104 MB Private Bytes**, well above the spec's < 80 MB target
  (see "Memory and publish size" above) — no trimming hack applied, by design.
- **Multi-word search doesn't match across fields** — every token of a multi-word query must match within
  the *same* field (name, or the same keyword, or description, or command text); a query where one word
  only matches the name and another only matches a keyword won't find that node (see "Search" above).
- **Orphaned custom icon files aren't cleaned up.** Switching a node away from a custom `file` icon (or
  deleting the node) leaves its copy behind in `<config dir>\icons\`; deleting that folder yourself is
  always safe (see "Icons" above).
- **Panel positioning uses only the monitor under the cursor** at the moment the hotkey fires; per-monitor
  DPI is read fresh each time (`GetDpiForMonitor`), but this was verified by code review only — the
  development machine has a single monitor (see "Phase 1–6 status" → Phase 5).
- **A Windows shutdown that skips the app's normal exit path can lose up to the last ~2 seconds of unflushed
  usage statistics** — `usage.json` writes are debounced 2 s and flushed on a clean exit, but not on a hard
  power-off/forced shutdown (see "Usage statistics" under "Phase 1–6 status" → Phase 6 Part A).
- **The Win key is never offered as a hotkey modifier** (for the global hotkey or the settings page's Hotkey
  capture box) — it's reserved by Windows itself, so `RegisterHotKey`/the capture box only accept
  Ctrl/Alt/Shift combinations.
- **`Enter`, `Tab`, `Esc`, and `Backspace` can't be captured as a hotkey key** from the settings page's
  Hotkey box — each already has a fixed meaning on that page (save/move-focus/cancel/reset-to-saved). To use
  one of them as part of a hotkey, edit `settings.hotkey` in `config.json` directly.
- **A press that isn't a continuous drag from Explorer still hides the panel** (see "Drag & drop" above) —
  only up to ~50 ms slower than before, while the panel waits to see whether the mouse button comes back up
  without a drag ever reaching it.

## Key decisions (D1–D8, see `tasks/todo.md` §0 for full rationale)

- **D1** — .NET 10 (not the spec's .NET 8): only SDK 10.0.401 is installed, and .NET 8 support ends
  2026-11-10.
- **D2** — App name "Your Launcher"; project/namespace names are the boxed `YourLauncher`.
- **D3** — CommunityToolkit.Mvvm for MVVM (source-generated, no reflection cost).
- **D4** — **Changed in Phase 5.** Originally planned as WinForms `NotifyIcon` (`UseWindowsForms=true`);
  built instead as a **hand-written `Shell_NotifyIcon` interop** (`Services/TrayService.cs`), so
  `UseWindowsForms` never has to go on alongside `UseWPF` at all. Reasons: (1) memory — WinForms drags in
  its own runtime pieces alongside WPF's for no real benefit here; (2) a dark-themed context menu is just a
  normal WPF `ContextMenu` with an explicit `Style`/`ControlTemplate` this way, whereas WinForms'
  `ContextMenuStrip` would need a separate, uglier renderer to look right next to the rest of the app; (3)
  it avoids the exact ambiguous-type collision (`Application`, `KeyEventArgs`, `MessageBox`, ...) the
  original plan flagged as a risk in the first place.
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
  Startup/                       # StartupSync (pure Run-registry write/delete/none decision, Phase 5)
  Usage/                         # UsageData, UsageScorer (frecency), UsageSerializer (Phase 6)
  Config/ConfigImport.cs         # Merge (id-collision rename) / Replace tree logic for import (Phase 6A)
  Config/DropMapper.cs           # Explorer/Start-menu drop path -> Node mapping (Phase 6B)
                                  # TreeOps also gained MoveToGroupIndex (in-list drag, Phase 6B)
  Bookmarks/                     # ChromiumBookmarkLocator, ChromiumBookmarkParser, NetscapeBookmarkParser
                                  # (HTML export), BookmarkImport (add/replace-in-place), BookmarkImportException
src/Launcher.App/                # net10.0-windows, WPF
  Interop/Win32.cs               # RegisterHotKey, cursor/monitor, SHGetFileInfo/ExtractIconEx/SHDefExtractIconW/DestroyIcon,
                                  # Shell_NotifyIcon/NOTIFYICONDATA, DWM (Mica/rounded corners), named-pipe/mutex interop (Phase 5)
  Interop/ShellLink.cs           # IShellLinkW/IPersistFile interop for resolving dropped .lnk files (Phase 6B)
  Services/                      # HotkeyService, ConfigService, LaunchService, SearchService, IconService, IconGlyphs,
                                  # SingleInstanceService, TrayService, StartupService, ConfigWatcherService (Phase 5),
                                  # ThemeService, UsageService (debounced atomic usage.json writer, Phase 6A)
  Services/ThemedMenuFactory.cs  # Shared theme-aware ContextMenu/MenuItem style factory (tray menu + list context menu, Phase 6B)
  ViewModels/                    # MainViewModel, ListItemViewModel, PanelPage, TypePickerViewModel, EditorViewModel,
                                  # IconPickerViewModel, SettingsViewModel (Phase 6A)
  ViewModels/DropIndicator.cs    # None/Above/Below/Into - in-list drag row indicator (Phase 6B)
  Views/                         # MainWindow (the panel), TypePickerView, EditorView, IconPickerView, SettingsView (Phase 6A)
  Converters/                    # StringEmptyToVisibilityConverter, StringNonEmptyToVisibilityConverter,
                                  # UrlOrTargetLabelConverter, AdvancedToggleTextConverter, NullToVisibilityConverter,
                                  # IconTierVisibilityConverter, MissingTargetTooltipConverter
  Controls/                      # HighlightedText (attached property for match highlighting)
  Themes/                        # Dark.xaml, Light.xaml — DynamicResource brush dictionaries (Phase 6A)
  app.ico                        # Multi-size (16/24/32/48/256) app + tray icon (Phase 5)
tests/Launcher.Core.Tests/       # xUnit: config round-trip, ConfigStore, CommandLineBuilder matrix, Search/, TreeOps
                                  # (incl. MoveToGroupIndex, Phase 6B), TargetNameHelper, Icons/ (IconKey, IconFileStore,
                                  # TargetCheck, PathResolver), StartupSyncTests (Phase 5), Usage/ (UsageScorerTests),
                                  # Config/ (ConfigImportTests (Phase 6A), DropMapperTests (Phase 6B)),
                                  # Bookmarks/ (locator/Chromium+Netscape parsers/BookmarkImport)
config.example.json
```
