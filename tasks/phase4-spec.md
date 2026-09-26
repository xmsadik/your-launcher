# Phase 4 — Icons: implementation spec

Source of truth: `launcher-spec.md` §4.4, §5 (row icon 20–24 px), §6.3 (`Ctrl+I`), §8.1 (missing target), AC4.
Read `tasks/todo.md` (decisions D1–D8, Phase 1–3 review notes) and the existing code before starting.
UI text is **English**. Match existing code style (file-scoped namespaces, XML doc comments, CommunityToolkit.Mvvm).
Build must stay at **0 warnings** (warnings-as-errors). All existing 114 tests must stay green.

## 1. Core (testable, no WPF)

1. `Launcher.Core/Icons/IconKey.cs` — `static string For(IconSpec? spec, Node node)` → stable cache key.
   - custom: `"{kind}|{normalized value}|{index}"` (value env-expanded via existing `EnvExpander`, path case-insensitive → lower-invariant for file/exe).
   - auto app/path: `"sys|{expanded target lower}"`. auto folder/command/url: `"glyph|default-{type}"`.
2. `Launcher.Core/Icons/IconFileStore.cs` — `string Import(string sourcePath, string iconsDir)`:
   copies file into `iconsDir` (created if missing) as `{first 16 hex of SHA-256 of content}{ext lower}`;
   if an identical file already exists, reuse it; returns the full destination path. Allowed ext: `.png .ico .jpg .jpeg .bmp .gif` (else `ArgumentException`).
3. `Launcher.Core/Icons/TargetCheck.cs` — `bool ShouldCheckExistence(string expandedTarget)`:
   true only for fully-qualified local paths (`Path.IsPathFullyQualified`), **false** for UNC (`\\`), URIs/shell: (`shell:`, `x:` with scheme > 1 char), bare names like `notepad.exe` (resolved by PATH). Plus `bool IsMissing(string target)` = ShouldCheck && !File.Exists && !Directory.Exists (env expanded).
4. Tests (xUnit, `tests/Launcher.Core.Tests/Icons/`): IconKey stability/distinctness, IconFileStore (hash naming, dedupe, bad extension, creates dir — use temp dir, clean up), TargetCheck matrix.

## 2. App: IconService (`Services/IconService.cs`)

- Extraction runs on **one dedicated STA background thread** (COM-initialized; SHGetFileInfo/ExtractIconEx require it) fed by a queue; results are `BitmapSource`s converted from HICON via `Imaging.CreateBitmapSourceFromHIcon`, then `Freeze()`d; always `DestroyIcon` the HICON.
- Memory cache `ConcurrentDictionary<string, ImageSource?>` keyed by `IconKey`. Also cache failures (null) so we don't retry each render. `Invalidate()` clears it (call on config reload).
- API: `bool TryGetCached(string key, out ImageSource? img)`; `Task<ImageSource?> GetAsync(IconSpec? spec, Node node)`; `Task<IReadOnlyList<ImageSource>> ExtractAllAsync(string exeOrDllPath)` (for the picker; `ExtractIconEx(path, -1, ...)` for count, cap at 300).
- Sources:
  - auto app/path → `SHGetFileInfo(SHGFI_ICON | SHGFI_LARGEICON)` on the expanded target (works for dirs, exes, docs). If the target is missing, use `SHGFI_USEFILEATTRIBUTES` with the extension so it still gets a type icon.
  - `exe` → `ExtractIconEx(path, index, large, ...)`.
  - `file` → `BitmapImage` with `CacheOption=OnLoad`, `DecodePixelWidth=48`, `CreateOptions=IgnoreColorProfile`, frozen. `.ico` also via BitmapImage (fine). svg unsupported → null (fall back to default glyph).
  - `glyph` / `emoji` → **not images**; rendered as text (see §3).
- Add needed P/Invokes to `Interop/Win32.cs` (`SHGetFileInfo`, `ExtractIconEx`, `DestroyIcon`, `SHFILEINFO`).

## 3. List rows

- `ListItemViewModel` becomes an `ObservableObject` with: `Glyph` (string, default type glyph or custom glyph), `EmojiText` (string?, custom emoji), `IconImage` (ImageSource?, set async), `IsTargetMissing` (bool).
  Display priority in the row template: IconImage → EmojiText (Segoe UI Emoji) → Glyph (`Segoe Fluent Icons, Segoe MDL2 Assets`). Icon box 24×24 DIP.
- When items are built (`MainViewModel` ~line 726–760): if the cache has the image, set it synchronously (no flicker); else show the glyph and kick off `GetAsync`, setting `IconImage` on the dispatcher when done — ignore results for items no longer in the list.
- Missing target (app/path, `TargetCheck.IsMissing`): computed on the background thread together with the icon (never block UI on disk IO); row gets `Opacity 0.55` and a small warning badge (glyph ``, accent-orange) overlaid bottom-right of the icon, plus tooltip "Target not found".

## 4. Icon picker (`PanelPage.IconPicker`, `Ctrl+I` on selected list item; also a "Change icon… (Ctrl+I)" button in the editor's Advanced section is **not** required)

- New `IconPickerViewModel` + `Views/IconPickerView.xaml` (UserControl, same pattern as TypePicker/Editor, visibility via wrapper Grid — see Phase 3 note about DataContext+Visibility).
- Blocked via existing `BlockIfReadOnly()` when config is read-only.
- Tabs (keyboard: `Ctrl+Tab`/`Ctrl+Shift+Tab` or `Ctrl+1..5`; click too): **Glyph**, **Emoji**, **File**, **Exe/DLL**, **Default**.
  - Glyph: grid of ~60 curated Segoe Fluent glyphs (folder, app, terminal, globe, document, code, settings, star, heart, home, mail, calendar, cloud, database, server, lock, key, user, people, music, video, photo, camera, game, chart, briefcase, bug, rocket-ish, lightning, flag, bookmark, pin, link, download, upload, sync, search, edit, delete, save, print, phone, chat, shop, money, map, car, airplane, book, lab/flask, wrench, shield, terminal/powershell, git-ish/branch...). Store as `{ "kind":"glyph", "value":"\uXXXX" }`. Include a name per glyph and a filter TextBox at the top (typing filters by name). Arrow keys move in the grid (wrap by row), Enter applies.
  - Emoji: single TextBox; Enter applies the text (trimmed, must be non-empty, max 8 UTF-16 chars) as `emoji`. Note: WPF renders emoji monochrome — acceptable.
  - File: "Browse…" button (Enter triggers) → `OpenFileDialog` (png/ico/jpg/jpeg/bmp/gif) → `IconFileStore.Import` into `<configDir>\icons\` (configDir = `ConfigService.ResolveConfigDirectory()` so `YOURLAUNCHER_CONFIG_DIR` is respected) → `file` icon with the copied path.
  - Exe/DLL: path TextBox prefilled with the node's target if it's an .exe/.dll, else `%SystemRoot%\System32\shell32.dll`; "Load" (Enter in the box) → grid of extracted icons (async, show "Loading…"); arrows + Enter applies `{ kind: exe, value: <path as typed, env vars kept>, index }`. Browse… button for exe/dll.
  - Default: Enter → sets `icon = null`.
- Applying: set `node.Icon`, save via the same path the editor uses (atomic save), invalidate that key, return to list with the same item selected. `Esc` returns to list without changes.
- Hint bar text for the page (like `TypePickerHint`).
- Update the list hint bar/help to mention `Ctrl+I icon` if space allows.

## 5. Docs / plan

- README: short "Icons" section (kinds, where custom files are copied, Ctrl+I).
- `tasks/todo.md`: tick Phase 4 items, add a "Aşama 4 — İkonlar (tamamlandı …)" review section in the same style/language (Turkish) as earlier phases: build/test counts, deviations, deliberately deferred items.

## 6. Verification you must do before reporting

- `dotnet build` 0 warnings; `dotnet test` all green.
- Launch the app with `YOURLAUNCHER_CONFIG_DIR` pointing to a scratch dir under `%TEMP%` containing a copy of `config.example.json` (never touch the real `%APPDATA%\Your Launcher\config.json`), show it (hotkey Alt+Space or whatever the config says), and take screenshots (call `SetProcessDPIAware` then full-screen capture via System.Drawing) of: the list with real system icons; a missing-target row; the icon picker Glyph tab and Exe/DLL tab. Look at the screenshots yourself and fix visual issues. Report the screenshot paths.
- Do **not** commit.

## 7. Revisions after critique (these OVERRIDE conflicting text above)

1. **DPI-sized icons.** Target px = 32 at ≤125 % DPI, 48 above (use the window's DPI scale). Extract with `SHDefExtractIconW(file, index, 0, out hIcon, IntPtr.Zero, targetPx)` for `exe` kind and the picker; for auto icons use `SHGetFileInfo(SHGFI_ICONLOCATION)` → `SHDefExtractIconW`, falling back to `SHGFI_ICON|SHGFI_LARGEICON`. Negative indexes (resource IDs) must work. Row `Image` gets `RenderOptions.BitmapScalingMode="HighQuality"`.
2. **No blocking on network paths.** If `TargetCheck.ShouldCheckExistence` is false for a path-shaped target (UNC, etc.), use `SHGFI_USEFILEATTRIBUTES` (extension-only, no disk access). Add Core `PathResolver.FindOnPath(name)` (`%PATH%` × `PATHEXT`, tested) — bare names like `notepad.exe` are resolved and then used for both icon and missing-check.
3. **Threading/caching:** drop the hand-rolled STA thread; use `Task.Run` gated by `SemaphoreSlim(2)`. Cache `Task<ImageSource?>` per key (`GetOrAdd`) → dedupes in-flight work and caches failures. Auto icons additionally share bitmaps by the shell's icon location (file+index), so all `.txt` rows share one bitmap. Every extraction wrapped in try/catch → null.
4. **Lazy loading:** start the load the first time `IconImage` is read (first bind), so the virtualized ListBox only loads realized rows. Stale results: `MainViewModel` keeps a `_listGeneration` counter incremented on each rebuild; the item captures it; completion applies only if unchanged (and marshals to the dispatcher).
5. **Portable file icons:** `IconFileStore.Import` returns and config stores a **relative** value `icons\<hash>.<ext>`; IconService resolves non-rooted `file` values against the config directory. Absolute paths (hand-edited) still work.
6. **.ico** via `IconBitmapDecoder` over a `FileStream` (OnLoad, stream disposed), choose frame closest to target px; other formats via `StreamSource` + OnLoad (no file lock).
7. **Dialogs:** File/Exe "Browse…" must use `BeginSuppressAutoHide()/EndSuppressAutoHide()` like `EditorView.xaml.cs`. IconPicker plumbing: `MoveFocusToCurrentPage` case, detach on `ResetToRoot()`, breadcrumb suffix " · Icon", `HintText`, `IsListPage` false.
8. **Picker keyboard:** focus stays in the tab's TextBox (filter / path / emoji); its `PreviewKeyDown` forwards Up/Down/PageUp/PageDown/Enter to the grid always and Left/Right only when the box is empty. Fixed 10-column grid. Exe tab: Enter in path box when path changed since last load = Load, otherwise Enter = apply selected icon. `Esc` always cancels. Tabs: **Glyph, Emoji, File, Exe/DLL** (no "Default" tab) — `Ctrl+0` resets to default from any tab (shown in hint bar).
9. **Open on current icon:** initial tab = node's current kind (Glyph if null/auto) with current value preselected; small "Current:" preview in the picker header.
10. **Emoji validation:** exactly one grapheme (`StringInfo.LengthInTextElements == 1`). Verify how .NET 10 WPF renders it (color or mono) and note it in the review.
11. **Exe/DLL:** default path `%SystemRoot%\System32\imageres.dll`; cap 1024 icons; fill the grid progressively in batches.
12. **Glyphs:** curate only from code points present in **Segoe MDL2 Assets** (E700–E9FF range, Win10-safe). Invalid custom glyph value (not exactly one PUA char) → default type glyph.
13. **Editor:** in the Advanced section add a read-only "Icon" line showing the current icon preview + text "Ctrl+I in the list to change". Record as deviation from launcher-spec §9 in the review.
14. `EditorViewModel`'s "Target not found — saved anyway" must use `TargetCheck.IsMissing` (with PathResolver) so editor and row badge agree.
15. `IconKey.For` returns null for glyph/emoji (never enqueued). `IconService.Invalidate()` is a Phase 5 hook (config reload) — call it from any existing reload path if one exists, else leave a `// Phase 5` note. Mention in README that orphaned files in `icons\` are not cleaned up.
