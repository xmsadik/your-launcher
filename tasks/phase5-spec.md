# Phase 5 — System integration: implementation spec

Source of truth: `launcher-spec.md` §4.5 (file watching), §5.1 (window: rounded corners, Mica/Acrylic), §6.4, §8.2 (hidden command exit code → tray notification), §10, AC1/AC2/AC10/AC11.
Read `tasks/todo.md` (decisions D1–D8, all review notes incl. Phase 4) and the existing code first. UI text **English**. Match existing style.
Build: **0 warnings**; all existing tests (172) stay green; add Core tests where logic is testable.
Never touch the real `%APPDATA%\Your Launcher\config.json` or the real `HKCU\...\Run` value "Your Launcher" in tests — see §9.

## 1. Single instance (`Services/SingleInstanceService.cs`)
- Named mutex `Local\YourLauncher-{sha256(configDir lower) first 12 hex}` — keyed by config dir so a `YOURLAUNCHER_CONFIG_DIR` test instance does not collide with the real one.
- First instance: owns mutex, starts a `NamedPipeServerStream` (same suffix, current-user only via `PipeOptions.CurrentUserOnly`) loop on a background task; on message `"show"` → dispatcher → `MainWindow.ShowLauncher()` (must actually take foreground: use the existing show path; if `SetForegroundWindow` is refused, use the `AttachThreadInput`/Alt-key trick already used or add it).
- Second instance: connect (timeout 1 s), send `"show"`, exit with code 0 before creating any window. If connect fails (first instance hung), just exit.
- Dispose pipe/mutex on exit.

## 2. Tray (`Services/TrayService.cs`)
- Enable `UseWindowsForms` in the App csproj **only if** type ambiguities can be contained: prefer adding `<UseWindowsForms>true</UseWindowsForms>` plus a global `using` alias strategy / fully-qualified names so that WPF types (`Application`, `KeyEventArgs`, `MessageBox`, `Clipboard`, ...) are not broken; build must stay 0 warnings. Alternative if that gets ugly: a minimal own `Shell_NotifyIcon` interop. Pick one, record why in the review.
- Icon: generate an app icon (`app.ico`, multi-size 16/20/24/32/48/256, simple rocket/launcher glyph on accent background — generate programmatically, commit the .ico) and use it for both the exe (`ApplicationIcon` is already conditionally wired) and the tray.
- Tooltip "Your Launcher (<hotkey>)". Left click / double click → show panel.
- Context menu (English): **Show**, **Settings…** (Phase 6 page doesn't exist yet → for now opens `config.json` in the default editor; leave `// Phase 6` note), **Open config file**, **Open config folder**, **Reload config**, separator, **Exit**.
- Exit path replaces "Ctrl+Q only" (keep Ctrl+Q too). `ShutdownMode` stays explicit; dispose tray icon on exit so no ghost icon remains.
- Balloon/toast notifications via `NotifyIcon.ShowBalloonTip` (or `NIF_INFO`): used for (a) hidden command finished with non-zero exit code: "<name> exited with code N" (wire into LaunchService: for `window: hidden` commands, `EnableRaisingEvents` + `Exited`; don't hold the Process longer than needed), (b) hotkey registration failure (§5), (c) config reload failure (§4).

## 3. Start with Windows (`Services/StartupService.cs`)
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value name **"Your Launcher"**, data = `"<full exe path>"` (quoted). Exe path = `Environment.ProcessPath`.
- On startup: sync registry to `settings.startWithWindows` — true → write if missing or path differs (exe moved); false → delete value if present.
- Expose `SetEnabled(bool)` for the Phase 6 settings toggle.
- Skip registry sync entirely when running from a `dotnet` host / Debug build? **No** — instead skip when `YOURLAUNCHER_CONFIG_DIR` is set (test mode) *or* env `YOURLAUNCHER_NO_STARTUP_REG=1`; log reason. Default settings value is `true`, so a normal first run registers itself — that is intended by the spec.
- Put the pure decision logic in Core (`Startup/StartupSync.cs`: given desired flag, current registry value (string?), exe path → action Write/Delete/None), unit-tested including quoting and case-insensitive path compare.

## 4. Config file watching + reload (AC10, AC11)
- `FileSystemWatcher` on the config directory, filter `config.json`, events Changed/Created/Renamed (editors often write via rename). Debounce ~200 ms (restart timer on each event).
- Ignore self-writes: use `ConfigService.LastSelfWriteUtc` (already present) — skip if the event arrives within ~1 s after our own save **and** the file's last-write time matches what we wrote. Better: store a SHA-256 of the bytes we last wrote and skip reload when the file's current hash equals it (robust, no timing guesses). Use the hash approach.
- Reading: retry up to 5× with 100 ms backoff on `IOException` (file locked by editor).
- Reload = parse via existing ConfigStore load path. `ConfigService` must become reloadable: `Reload()` returns a result; on success replace the in-memory config (make `Config` settable / raise a `ConfigReloaded` event), clear read-only state, `IconService.Invalidate()`, rebuild `SearchService` index, `MainViewModel` goes back to root (or stays in the same folder if its id still exists), clears cut state, closes any open editor/picker page without saving.
- Corrupt on reload: keep the **current in-memory** config (don't switch to empty), set read-only mode, show error line + tray balloon "config.json has errors — kept the previous version." Offer **"Load backup"**: tray menu item "Restore from backup" enabled when `config.backup.json` exists and the current file is corrupt → copies backup over config (keeping the corrupt one as `config.corrupt-<timestamp>.json`) and reloads. Same action available from the panel via the error line hint "Ctrl+Shift+R: restore backup" (only while corrupt).
- Startup with a corrupt file (already handled read-only) gets the same restore option.
- Also "Reload config" tray item and `Ctrl+R` in the panel → same reload path.
- Settings changes on reload: re-register hotkey if `settings.hotkey` changed (§5), re-sync startup registry, apply maxVisibleItems/showHintBar/closeAfterLaunch live.

## 5. Hotkey failure
- If `RegisterHotKey` fails at startup or on reload: tray balloon "Hotkey <x> is already in use. Open settings to choose another." + clicking the balloon (or the tray's Settings…) opens config.json for now (`// Phase 6`: open settings page). Keep the existing panel error line.
- Hotkey re-registration must unregister the old one first; if the new one fails, try to restore the old one.

## 6. Window: DPI / monitor positioning
- Manifest already declares PerMonitorV2. Verify the current positioning code: it divides the target monitor's work area by **the window's current DPI**, which is wrong when the target monitor has a different DPI. Fix by positioning in physical pixels: compute target rect in device pixels using the target monitor's DPI (`GetDpiForMonitor` MDT_EFFECTIVE_DPI) and window size × that scale, then `SetWindowPos` (no activate/zorder change flags as appropriate) before showing. Panel appears on the monitor with the cursor, horizontally centered, top edge at 1/3 of work-area height (keep existing rule).
- Keep show latency low (AC2 < 100 ms): add a `Stopwatch` debug trace from hotkey message to `Activated`, written to `Debug.WriteLine` only.

## 7. Window chrome: Mica/Acrylic + rounded corners
- Win11 (build ≥ 22000): `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND)`, and backdrop `DWMWA_SYSTEMBACKDROP_TYPE = 38` → `DWMSBT_TRANSIENTWINDOW (3)` (acrylic, suits a transient popup) with `DWMWA_USE_IMMERSIVE_DARK_MODE = 20` = 1 (dark UI). For the backdrop to show, the WPF window needs a transparent client background: `WindowChrome` with `GlassFrameThickness=-1` / `DwmExtendFrameIntoClientArea(-1)` and a semi-transparent panel background; check `AllowsTransparency` is **false** (layered windows break DWM backdrops) — if the current window uses `AllowsTransparency=True`, switch approach and keep the visual look (border, radius).
- Fallback (Win10 or any DWM call fails): the current solid background + existing border/radius, unchanged.
- Readability first: if the acrylic makes text low-contrast in screenshots, increase panel tint opacity.

## 8. Memory
- After hiding the panel, call `SetProcessWorkingSetSize(-1,-1)` (optional trim; spec risk note) — only if it doesn't hurt show latency (measure).
- Measure: `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=true` into a scratch dir, run with a scratch config dir, show+hide once, wait 5 s, record Working Set / Private Bytes. Report numbers in the review (target < 80 MB idle; if missed, report — don't hack).

## 9. Verification before reporting
- `dotnet build` 0 warnings; `dotnet test` green.
- Live smoke tests with `YOURLAUNCHER_CONFIG_DIR` = scratch dir under `%TEMP%` (startup registry sync is skipped in that mode — so test `StartupService` itself only against a **different, test-only** Run value name, e.g. via an injectable value name, then delete it).
  1. Start instance A; start instance B → B exits within ~1 s, A's panel shows. Screenshot.
  2. Tray icon visible with menu (screenshot the open menu), Exit removes the icon and ends the process.
  3. Edit config.json externally (rename a node via PowerShell write) → panel reflects it without restart. Write invalid JSON → app keeps previous tree, error line/balloon shown, file untouched; restore-from-backup works.
  4. Save from inside the app (e.g. Ctrl+N add) → no spurious reload (log/trace).
  5. Hidden command `exit 3` (pwsh, window hidden) → balloon "… exited with code 3".
  6. Screenshot of the panel with Mica/Acrylic + rounded corners.
  7. Publish + memory numbers (§8).
- Screenshots: SetProcessDPIAware + full-screen capture; if `SendKeys` Ctrl chords misbehave use `keybd_event` (Phase 4 note). Save in the session scratchpad `phase5\` folder given in your prompt.
- Update README (tray, single instance, startup, file watching, restore backup, Ctrl+R / Ctrl+Shift+R) and `tasks/todo.md` (tick Phase 5, Turkish review section in the established style).
- Kill every launched process at the end; delete any test Run registry value. Do **not** commit.

## 10. Revisions after critique (these OVERRIDE conflicting text above)

1. **Foreground:** `ShowLauncher` currently has no AttachThreadInput/Alt trick — add it. Instance B calls `AllowSetForegroundWindow(ASFW_ANY)` before sending "show". In A: after `Show()`, if `GetForegroundWindow() != hwnd` → `keybd_event(VK_MENU)` down/up + `SetForegroundWindow` again; if still not foreground, `Hide()` (never leave an unfocused Topmost panel that can't auto-hide). If the panel is **already visible** (or a Browse dialog is open, `_suppressAutoHideCount > 0`), a pipe/tray "show" only re-foregrounds — no `ResetToRoot()`. Hotkey keeps `ToggleLauncher`.
2. **Reload in place:** do NOT replace the `LauncherConfig` instance. Assign `Config.Root/Settings/Version` from the loaded config into the existing instance, then raise `ConfigService.ConfigReloaded`. VM handler: rebuild folder stack (stay in the same folder if its id exists in the new tree, else root), `SearchService` rebuild, `IconService.Invalidate()`, clear cut state, re-apply `ItemsList.MaxHeight` (maxVisibleItems) and hint bar. `LoadError` becomes privately settable and is cleared on a successful reload. Remove the now-unused `LastSelfWriteUtc` (replaced by the hash).
3. **Watcher threading:** FSW (`IncludeSubdirectories=false`, filter `config.json`, Changed/Created/Renamed; also `Error` → schedule reload) → restart a debounce timer (200 ms) → read bytes + hash off-thread (retry 5×100 ms on IOException) → if hash == last self-written hash, skip → otherwise parse + apply on the **Dispatcher**. On parse failure, re-read once after ~500 ms before declaring corrupt (Notepad-style truncate-then-write).
4. **Corrupt semantics:** reload-corrupt → keep in-memory tree, read-only, error line + balloon; recovery action = **"Keep my current version"** (archive the corrupt file as `config.corrupt-<yyyyMMdd-HHmmss>.json`, re-save in-memory). Startup-corrupt (in-memory empty) → **"Restore from backup"** (archive corrupt file the same way, copy `config.backup.json` over, reload). Tray shows whichever applies; panel shortcut `Ctrl+Shift+R` does the applicable one (hint in the error line).
5. **Deferred reload while editing:** if the current page is Editor/TypePicker/IconPicker, set `_reloadPending` and apply when returning to List or on hide. If a save is attempted while `_reloadPending`, refuse with error line "config.json changed on disk — your edit was not saved; reopen and redo it." then apply the reload (never silently overwrite the external change).
6. **Tray = own `Shell_NotifyIcon` interop (no WinForms)** — decision D4 changes (memory + dark-themed menu): `NOTIFYICONDATAW`, callback `WM_APP+1` through the existing HwndSource hook, re-add on `RegisterWindowMessage("TaskbarCreated")`, balloons via `NIF_INFO` (+ `NIN_BALLOONUSERCLICK` for the hotkey-failure balloon), menu = WPF `ContextMenu` styled dark, opened at the cursor; call `SetForegroundWindow(hwnd)` before and `PostMessage(hwnd, WM_NULL)` after so it dismisses on outside click. Opening the menu must not trigger the panel's auto-hide logic incorrectly. `NIM_DELETE` on exit. Record D4 change in README decisions + todo.md.
7. **App icon:** hand-write a small ICO packer script (PNG-encoded frames 16/24/32/48/256 rendered via WPF `RenderTargetBitmap` from a simple vector: rounded accent square + white glyph) in a throwaway scratch script, commit only the resulting `src/Launcher.App/app.ico`. Tray loads it via `LoadImage` from the exe's embedded icon (or from the .ico resource) at SM_CXSMICON size.
8. **Hidden command exit codes:** keep `LaunchService.MonitorHiddenProcessAsync`; add a separate event `HiddenCommandExited(string name, int exitCode)` raised for non-zero codes; App marshals it to the Dispatcher → balloon. `ErrorOccurred` stays for panel errors.
9. **Startup registry:** skip sync in `#if DEBUG` builds, when `YOURLAUNCHER_CONFIG_DIR` is set, or `YOURLAUNCHER_NO_STARTUP_REG=1` (Debug.WriteLine the reason). `StartupService` takes the Run value name as a ctor parameter so tests can use a throwaway name.
10. **DPI positioning (replaces §6 approach):** keep WPF Left/Top. Fix only the centering term: `targetScale = GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI)/96`, `currentScale` = window DPI; center with `Width * targetScale / currentScale`. Override `OnDpiChanged` to re-run the positioning once. No raw SetWindowPos. Verification = code review (single monitor environment), state that in the review.
11. **Mica/Acrylic (replaces §7 details):** apply in `OnSourceInitialized`: `WindowChrome` (CaptionHeight 0, ResizeBorderThickness 0, GlassFrameThickness -1) with `ResizeMode=CanResize` so DWM rounds corners (keep the window effectively non-resizable), `HwndSource.CompositionTarget.BackgroundColor = Transparent`, `DWMWA_USE_IMMERSIVE_DARK_MODE=1` (`// Phase 6: theme switch flips this`), corners `DWMWCP_ROUND` (build ≥ 22000), backdrop `DWMWA_SYSTEMBACKDROP_TYPE=DWMSBT_TRANSIENTWINDOW` only if build ≥ 22621 **and** the HRESULT is 0; panel background becomes a semi-transparent tint. Any failure → exactly the current solid look (background #1E1E1E, border, radius). Readability over effect.
12. **Single instance details:** `new Mutex(true, name, out createdNew)`; `AbandonedMutexException` = acquired. Acquire mutex + start pipe server **first thing** in `OnStartup` (before config/window). B retries connect up to ~2 s, then `Shutdown()` **and return** before any window is created. Server: new `NamedPipeServerStream` per connection, swallow ObjectDisposed/IO on teardown. `Local\` = per session (document).
13. **Hotkey:** subscribe `HotkeyPressed` unconditionally; on reload skip if unchanged; distinguish parse failure ("Hotkey 'X' is not a valid key combination") from "in use"; on failure of new one, restore old.
14. **Memory:** trim OFF by default (don't game Working Set). Add to the App csproj (Release): `ConcurrentGarbageCollection=false`, `TieredPGO=false`, `UseSystemResourceKeys=true`, `SatelliteResourceLanguages=en`. Report Working Set **and** Private Bytes idle.
15. **Verification tweaks:** Win11 tray icon may sit in the overflow flyout — open it for the screenshot. Balloons may be suppressed in this session — verify via Debug trace that the balloon call succeeded. Extend the existing show Stopwatch trace to start at WM_HOTKEY. README decisions table: note `Ctrl+R`/`Ctrl+Shift+R` additions, cursor-monitor-only positioning, Settings… → config.json placeholder until Phase 6.
