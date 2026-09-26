# Phase 6 — Polish: implementation spec

Source of truth: `launcher-spec.md` §6.3 (`Ctrl+,`), §6.4, §8.4, §9 (drag & drop, context menu), §10 (import/export), §11, §13 AC12, §15.
Read `tasks/todo.md` (decisions D1–D8, all review notes incl. Phase 5), `README.md` and the existing code first. UI text **English**. Match existing style (comment density, naming, MVVM with CommunityToolkit, in-panel pages via `PanelPage`).
Build: **0 warnings**; all existing tests (180) stay green; pure logic goes to Core with tests.
Never touch the real `%APPDATA%\Your Launcher\` files or the real HKCU Run value "Your Launcher" — every live run uses `YOURLAUNCHER_CONFIG_DIR` = scratch dir.

Implemented in two passes: **Part A** (§1–§4), then **Part B** (§5–§8). Each pass ends green (build + tests + smoke).

---

## Part A

### 1. Settings page (`PanelPage.Settings`, spec §11)
- New in-panel page `SettingsView` + `SettingsViewModel`, opened by `Ctrl+,` (both modes), the tray's **Settings…**, and a click on the hotkey-failure balloon (replace the `// Phase 6` placeholders that open config.json). Opening from tray/balloon = `ShowLauncher()` then navigate to Settings.
- Fields (keyboard-only usable, Tab order top→bottom):
  - **Hotkey** — a capture box: when focused, the next key combination with ≥1 modifier (Ctrl/Alt/Shift/Win) + a non-modifier key is recorded and shown in the same text form `HotkeyService` parses (e.g. `Alt+Space`, `Ctrl+Shift+K`). Tab/Shift+Tab leave the box normally (not captured); Esc is not captured (goes to page cancel); Backspace resets to the current saved value.
  - **Theme** — System / Light / Dark.
  - **Start with Windows** — toggle.
  - **Close after launch** — toggle.
  - **Visible rows** — 3…20 (numeric; ↑/↓ adjust).
  - **Default shell** — pwsh / powershell / cmd.
  - **Remember last location** — toggle (see §1.2).
  - **Show hint bar** — toggle.
  - Buttons: **Open config folder**, **Export…**, **Import…** (§4).
- Semantics = same as the node editor: **Enter / Ctrl+S saves, Esc cancels** (no live-apply while editing, except theme may preview live and revert on cancel — optional). Save = validate → write `Settings` into the in-memory config → `ConfigService.Save()` → apply:
  - hotkey changed → re-register through the existing Phase 5 re-registration path (unregister old, register new, restore old on failure). On failure: stay on the page, error line "Hotkey X is already in use." / "…is not a valid key combination.", nothing saved.
  - startWithWindows → `StartupService.SetEnabled` immediately (still skipped in test/DEBUG mode per Phase 5 rules — log only).
  - maxVisibleItems / showHintBar / closeAfterLaunch / defaultShell / theme applied live (reuse the Phase 5 "apply settings on reload" code — one `ApplySettings()` path for reload and settings save).
- Read-only (corrupt config) → page opens but Save is refused with the existing read-only error text.
- If a file-watcher reload arrives while Settings is open → same deferred-reload rule as the editor (Phase 5 §10 item 5).
- Hint bar on this page: `Tab move  Enter save  Esc cancel`.

#### 1.2 Remember last location
- When `rememberLastLocation` is true, `ShowLauncher()` reopens the folder that was current at the last hide (by id; fall back to root if it no longer exists) instead of `ResetToRoot()`'s root. Search text is still cleared. Keep in memory only (not persisted across restarts).

### 2. Theme (`Services/ThemeService.cs`)
- Two ResourceDictionaries `Themes/Dark.xaml`, `Themes/Light.xaml` with the **same keys** (brushes: panel background/tint, border, foreground, secondary text, accent, selection, hover, error, separator, input background, hint bar, …). Replace every hard-coded color in XAML (≈73) and code (≈11) with `DynamicResource` / resource lookups. Dark must look pixel-identical to today.
- `ThemeService.Apply(Theme)`: System → read `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` (missing = dark? no: missing = light, Windows default), subscribe `SystemEvents.UserPreferenceChanged` (category `General`) while in System mode and re-evaluate on the Dispatcher; unsubscribe on exit.
- Swap the merged dictionary in `Application.Resources`; flip `DWMWA_USE_IMMERSIVE_DARK_MODE` (the `// Phase 6` note in MainWindow) and the acrylic tint for the resolved theme. Tray context menu follows the theme too.
- Light theme must be readable over acrylic (screenshot both themes).

### 3. Usage statistics (spec §8.4)
- Core `Usage/UsageData.cs` + `UsageScorer` (pure): per node id `{ useCount, lastUsedUtc }`; `Record(id, nowUtc)`; `Score(id, nowUtc)` = deterministic frecency, e.g. `useCount * weight(age)` with weight buckets (≤1 d: 4, ≤7 d: 2, ≤30 d: 1, older: 0.5). `Prune(existingIds)`. JSON (de)serialization via the source-generated context. Unit tests: ordering, determinism with fixed `now`, pruning, corrupt/missing file → empty.
- App `Services/UsageService.cs`: `%config dir%\usage.json`; record on every **successful** launch (all node types except folders); debounced atomic save (2 s, temp + move); flush on exit; corrupt file → start empty, don't crash, don't overwrite until next record.
- Feed `SearchEngine`'s existing usage tie-breaker (`SearchService` comment "Phase 6 passes a usage-based…"). Navigation-mode order is never affected.
- Unit test: two equal-score search hits ordered by usage.

### 4. Import / Export (spec §10, §11)
- **Export…** → `SaveFileDialog` (default name `your-launcher-export-yyyyMMdd.json`) → writes the current config (settings + tree) with the existing serializer. Dialog suppresses auto-hide (existing `_suppressAutoHideCount`).
- **Import…** → `OpenFileDialog` → parse with the existing loader; invalid → error line, nothing changes. Valid → in-panel prompt: **M** Merge · **R** Replace · **Esc** cancel (with a line explaining each).
  - **Replace**: replace the root tree with the imported tree; **current settings are kept** (importing someone's hotkey/startup flag is surprising). The previous version stays in `config.backup.json` through the normal save.
  - **Merge**: append the imported root's children to the current root; every imported id that collides with an existing id (anywhere in either tree, including within the imported subtree after renaming) gets a fresh id. No folder-name merging (documented).
  - Core `Config/ConfigImport.cs` (pure) with tests: id collision renaming incl. nested, order preserved, imported tree not aliased with the source, replace keeps settings.
- After import: normal post-edit refresh (search rebuild, icon invalidate, go to root).
- Read-only → refused.

---

## Part B

### 5. Context menu (spec §9)
- Right-click on a list item selects it, then opens a menu styled like the tray menu (theme-aware): **Open**, **Edit** (F2), **Change icon** (Ctrl+I), **Move** (= cut, Ctrl+X; paste with Ctrl+V as today), **Duplicate** (Ctrl+D), **Delete** (Del → existing confirm page), separator, **Open file location** (app/path only, enabled when the target exists → `explorer.exe /select,"<path>"`; for a directory target open its parent with it selected). Show the shortcut text in the menu's gesture column.
- Each item calls the **same commands** as the keyboard shortcuts — no duplicated logic. Edit items disabled in read-only mode.
- Also works on search results (Open file location, Open, Edit, Change icon, Delete; Move/Duplicate may also work — they already do from the keyboard in search mode, mirror that).
- Opening the menu must not trigger auto-hide.

### 6. Drag & drop from Explorer / Start menu (AC12)
- Problem: clicking Explorer to start a drag deactivates the panel and auto-hides it. Rule: on `Deactivated`, if the left mouse button is currently down (`GetAsyncKeyState(VK_LBUTTON)`), **defer** the auto-hide: poll (~50 ms timer) until the button is released; if a `DragEnter` with `FileDrop` arrives meanwhile, keep the panel until `Drop`/`DragLeave`; after a drop re-activate the panel (foreground trick from Phase 5) and keep it open; if the button is released with no drag over the panel and the panel is not active, hide as before.
- `AllowDrop` on the panel; accept only `DataFormats.FileDrop` (effect `Copy`/`Link`), reject everything else. Drop target = current folder (in search mode: current folder too). Read-only → reject with error line.
- Mapping (pure part in Core `Config/DropMapper.cs`, tested with fake shortcut info):
  - `.lnk` → resolve via `IShellLinkW` + `IPersistFile` (App `Interop/ShellLink.cs`, `[GeneratedComInterface]` or classic `ComImport` — must stay 0 warnings, trimming-safe not required): target path, arguments, working dir, icon location + index. Target is an existing `.exe` → **app** node (target, args, workingDir); target is a file/dir → **path** node; no target (advertised/MSI shortcut, e.g. some Start-menu entries) → **app** node whose target is the `.lnk` itself (ShellExecute can run it). Name = `.lnk` file name without extension. Icon: explicit icon location → exe/dll `IconSpec` (location, index); otherwise default (auto icon).
  - `.url` (internet shortcut) → **url** node (read `URL=` from `[InternetShortcut]`); name = file name.
  - `.exe` → **app** node; name via existing `TargetNameHelper` (FileDescription).
  - anything else (file or directory) → **path** node; name via `TargetNameHelper`.
- Multiple files → all added in drop order, one save, last one selected.
- Tests: mapping table above, env-var/relative paths preserved as returned, name derivation.

### 7. In-list drag (reorder / move into folder)
- Navigation mode only (not search results, not other pages). Mouse drag of a list item (threshold `SystemParameters.MinimumHorizontal/VerticalDragDistance`): an insertion line shows between items; hovering over a **folder** item's middle 50 % highlights it = "move into".
- Drop between items → reorder within the current folder (Core `TreeOps.MoveToIndex(root, node, newIndex)` — respect the existing folders-first/items grouping rules of MoveUp/MoveDown: a node can't be placed outside its group; clamp). Drop on folder → existing `TreeOps.MoveTo`. One save; selection follows the node.
- Uses in-process `DragDrop.DoDragDrop` with a private data format so it doesn't collide with the Explorer FileDrop path. Read-only → disabled.
- Tests for `MoveToIndex` (clamping, groups, same index no-op, not in tree).

### 8. Delivery (spec §15)
- `config.example.json` still parses (smoke test) and shows every node type + nested folders + one command per shell.
- README: install (publish command, where the exe goes, first run, "Start with Windows"), full shortcut table (incl. `Ctrl+,`, context menu, drag & drop), config format incl. `usage.json`, import/export semantics, theme, decisions table (update D4 already done; add any Phase 6 decisions), known limitations (memory numbers, multi-word cross-field search, orphan `icons\` files, cursor-monitor-only).
- `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=true` succeeds; record exe size + idle memory again.

---

## 9. Verification before reporting (each part)
- `dotnet build` 0 warnings (Debug + Release); `dotnet test` green.
- Live smoke with `YOURLAUNCHER_CONFIG_DIR` scratch dir, screenshots (SetProcessDPIAware, full-screen, panel verified foreground via `GetForegroundWindow` before capture) into the scratchpad folder named in your prompt:
  - A: settings page (dark + light), hotkey capture → save → new hotkey toggles the panel and the old one doesn't; Visible rows change applied live; theme System follows registry (just verify the read; don't change the user's Windows theme); usage.json written after a launch and tie-break visible; export file round-trips; import Merge (id collision renamed) and Replace (settings kept).
  - B: context menu screenshot + each action; drag a `.lnk` (create one in scratch via `WScript.Shell` with args + icon) and a `.url`, `.exe`, folder, txt onto the panel (simulate with a scripted OLE drag if needed — if real drag automation is impractical, verify the drop handler through a debug-only entry point is **not** acceptable; instead drive a real drag via `SendInput` mouse down/move/up from Explorer, or report honestly what could not be automated); in-list reorder + move into folder.
- Update README + `tasks/todo.md` (tick Phase 6 items, Turkish review section in the established style, update "Devam noktası": Phase 5 **is** committed as `94454b6`).
- Kill every launched process; delete scratch dirs/test registry values. Do **not** commit.

---

## 10. Revisions after critique (these OVERRIDE conflicting text above)

**Order.** Part A: §2 theme **first** (with before/after dark screenshot diff) → §1 settings (written with DynamicResource from the start) → §4 import/export → §3 usage. Part B: mouse prerequisite (item 5 below) → §5 → §6 → §7 → §8. §7 is still required (launcher-spec §9) but goes last.

1. **Hotkey capture:** while the capture box has keyboard focus, unregister the live global hotkey (else `WM_HOTKEY` eats it); on blur/cancel re-register the old one, on successful save register the new one. Ignore modifier-only presses; read Alt combos via `e.SystemKey` when `e.Key == Key.System`; emit WPF `Key` enum names (`OemComma`, `D1`, …) since `HotkeyService.TryParse` uses `Enum.TryParse<Key>`. Win modifier: not offered (reserved by Windows) — documented. Enter/Tab/Esc/Backspace aren't capturable (edit config.json for those) — documented.
2. **Save seam:** inject `Func<string, string?> tryApplyHotkey` (returns error text or null) into the settings VM (same style as `fileDescriptionLookup`); call it **before** `ConfigService.Save()`; failure → nothing saved. Raise a `SettingsApplied` event that App handles for `StartupService.SetEnabled` + `ThemeService.Apply`. Extract only the live-apply part (MaxVisibleItems / ShowHintBar / …) into a shared `ApplySettings()` used by both reload and settings save — do not call `OnConfigReloadedSuccess` on a settings save. An external reload that changes `theme` also calls `ThemeService.Apply`.
3. **Theme — code-held brushes:** `HighlightedText` accent → `SetResourceReference`; `TrayService` static styles → built per `BuildMenu()` from a shared theme-aware menu style factory (reused by §5); `MainWindow.ApplyTheme(bool dark)` re-applies `DWMWA_USE_IMMERSIVE_DARK_MODE` and picks tint-vs-solid background keys ({dark,light}×{acrylic,solid}). Rule: theme keys referenced **only** via `DynamicResource`, including styles inside `UserControl.Resources`. Move EditorView's custom ComboBox/ComboBoxItem templates into the theme dictionaries so Settings reuses them.
4. **Theme detection:** use `WM_SETTINGCHANGE` with `lParam == "ImmersiveColorSet"` in the panel's existing `HwndSource` hook instead of `SystemEvents` (no extra thread / lifecycle). Missing registry value = light.
5. **Mouse prerequisite (Part B, before §5):** `ListBoxItem` `Focusable=False` so clicks never steal focus from SearchBox; `PreviewMouseLeftButtonDown`/`RightButtonDown` maps the container to an index and sets the VM `SelectedIndex` (binding is OneWay today); **left-click selects, double-click opens**.
6. **Drag & drop details:** (a) documented limitation — only a press-and-drag in one motion from Explorer survives; a click on Explorer that isn't a drag hides the panel as before. (b) After a drop: panel ends foreground or hidden — never a visible deactivated Topmost panel; verify. (c) Accept drops only on the List page. (d) `.lnk`: no `IShellLinkW.Resolve`; `IPersistFile.Load` + `GetPath(SLGP_RAWPATH)` (env vars preserved). (e) Classic `[ComImport]` interfaces + `ShellLink` coclass (matches the DllImport precedent; 0 warnings required). (f) `.bat/.cmd/.com/.msc` targets (direct or via `.lnk`) → **app**, like `.exe`.
7. **Context menu:** **Cut** (Ctrl+X) + **Paste here** (Ctrl+V, enabled while a node is cut) instead of "Move". Right-click on empty list area shows a menu with Paste here / New item (Ctrl+N) / New folder (Ctrl+Shift+N). "Open file location" resolves the target with the same `EnvExpander` + `PathResolver`/`TargetCheck` chain as the missing-target badge.
8. **In-list drag:** Core `TreeOps.MoveToGroupIndex(root, node, groupIndex)` (index within the node's folder / non-folder group, clamped), tested. Indicator via a `DropIndicator` (None/Above/Below/Into) property on `ListItemViewModel` bound in the row template — no Adorner.
9. **Import prompt:** `PanelPage.ConfirmImport` inline bar (ConfirmDelete pattern) holding the parsed config in the VM; M/R/Esc. Success → List at root + status line "Imported N items (merged|replaced)". Export disabled in read-only mode too. Document: merging your own export duplicates everything. Prune usage after Replace/delete.
10. **Usage wiring:** inject `Action<string> recordUsage` into MainViewModel; call only when `_launchService.Launch` returns true. Construct `UsageService` before `SearchService`. `Prune` on load and after import/delete. Add the usage dictionary type to a Core source-gen context. Flush on exit (Windows shutdown without exit may lose ≤2 s of usage — documented).
11. **Settings key semantics:** Enter saves unless a ComboBox dropdown is open; Space toggles the focused CheckBox; ↑/↓ in Visible rows adjust with clamp 3–20 (model itself stays unclamped).
12. **Page plumbing checklist:** add `PanelPage.Settings`/`ConfirmImport` to the deferred-reload page check, `MoveFocusToCurrentPage`, breadcrumb ("· Settings"), `HintText`, and the visibility wrappers (DataContext/Visibility-on-wrapper rule from Phase 3). Hide discards unsaved settings (same as editor) — documented. §1.2 reuses the id-walk from `OnConfigReloadedSuccess` to rebuild the folder stack; update the "always starts at root" comments.
13. **Hotkey failure (launcher-spec §6.4):** on a startup/reload hotkey failure → balloon **and** `ShowLauncher()` + Settings page with the error line and the Hotkey box focused.
14. **Verification:** drag & drop via a throwaway external drag-source exe in scratch (`DoDragDrop(FileDrop)` on mouse-down while `SendInput` moves onto the panel and releases) — a real OLE drag, no debug entry point in the app. Dark-theme screenshot before/after the DynamicResource conversion. Start-with-Windows toggle can't be live-verified under the scratch-dir rule — log line + unit tests only; say so.
