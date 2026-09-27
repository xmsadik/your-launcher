# Bookmark import — implementation spec (2026-09-27)

User decisions: **Chromium family auto-detected + any browser via its HTML export file**; **one-time import, re-import
of the same source replaces the previous import**. No new NuGet dependency. UI text English. Build 0 warnings
(Debug + Release), all existing tests (243) stay green, pure logic in Core with tests. Match existing style
(read `README.md`, `tasks/todo.md` decisions, `tasks/phase6-spec.md` §10 for conventions).

## 1. Core — `src/Launcher.Core/Bookmarks/`

### 1.1 `ChromiumBookmarkLocator`
- `IReadOnlyList<BookmarkSource> Discover(string localAppData, string roamingAppData)` — roots injected so tests use a temp dir.
- Known browsers (display name → user-data dir):
  - Chrome → `%LOCALAPPDATA%\Google\Chrome\User Data`
  - Edge → `%LOCALAPPDATA%\Microsoft\Edge\User Data`
  - Brave → `%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data`
  - Vivaldi → `%LOCALAPPDATA%\Vivaldi\User Data`
  - Chromium → `%LOCALAPPDATA%\Chromium\User Data`
  - Opera → `%APPDATA%\Opera Software\Opera Stable`, Opera GX → `%APPDATA%\Opera Software\Opera GX Stable` (the `Bookmarks` file sits directly in that dir; treat as a single profile)
- For user-data dirs: profiles are subdirs `Default` and `Profile *` that contain a `Bookmarks` file. Profile display name from
  `Local State` JSON → `profile.info_cache.<dirName>.name` when readable; otherwise the dir name.
- `BookmarkSource(string Browser, string ProfileDir, string ProfileName, string BookmarksPath)` + `string SourceKey`
  (stable, lowercase, e.g. `chrome/default`, `edge/profile 1`, `opera/`) + `string DisplayName`
  ("Chrome", or "Chrome (Work)" when the browser has more than one profile).
- Never throws for missing/unreadable dirs or malformed `Local State` — skip silently.

### 1.2 `ChromiumBookmarkParser`
- `FolderNode Parse(string json, string folderName)` using `JsonDocument` (no reflection serialization).
- Roots: `roots.bookmark_bar` → folder "Bookmarks bar", `roots.other` → "Other bookmarks", `roots.synced` → "Mobile bookmarks".
  Recurse `children`: `type == "folder"` → `FolderNode`, `type == "url"` → `UrlNode { Name, Target = url }`.
- Skip `javascript:` URLs (bookmarklets) and any node without a URL. Empty title → URL host (or the URL if no host).
  Empty folder name → "(unnamed)". **Drop folders that end up empty** (recursively), including empty roots.
- Preserve order. All nodes get fresh ids (`TreeOps.NewId()`).
- Malformed JSON → throw a dedicated `BookmarkImportException` with a user-readable message (caller shows it).

### 1.3 `NetscapeBookmarkParser` (HTML export — Firefox, Chrome, Edge, Safari all use this format)
- `FolderNode Parse(string html, string folderName)`; tolerant tokenizer (regex over tags is fine), case-insensitive tags,
  unclosed `<DT>`/`<p>` are normal in this format. `<H3>name</H3>` followed by `<DL>` = folder; `<A HREF="...">title</A>` = url;
  `</DL>` closes the current folder. Decode entities with `System.Net.WebUtility.HtmlDecode` (names and hrefs).
- Same rules as 1.2 for javascript:/empty title/empty folders. The top-level `<H1>` is ignored; top-level folders/links go
  directly into the result folder.
- No bookmarks found at all → `BookmarkImportException("No bookmarks found in <file>.")`.

### 1.4 `BookmarkImport.Apply(FolderNode root, FolderNode targetFolder, FolderNode imported, string sourceKey) → (FolderNode folder, bool replaced, int count)`
- Result folder id = `"bookmarks:" + sourceKey` (deterministic — this is how re-import finds the previous one; HTML key =
  `html/<file name lowercased>`).
- If a node with that id exists anywhere under root and is a folder → **replace its Children** with the imported children
  (keep the existing folder's Name, Icon, Keywords, Description, position — the user may have renamed/moved it). Otherwise
  append `imported` (with that id) to `targetFolder`.
- `count` = number of UrlNodes imported. Tests: add, replace-in-place (moved + renamed folder keeps name/position), count,
  empty-folder pruning, nested order.

Folder name for a new import: `"<DisplayName> bookmarks"` (e.g. "Chrome bookmarks", "Edge (Work) bookmarks"); HTML:
`"Bookmarks (<file name without extension>)"`.

## 2. App

- File reads: `FileShare.ReadWrite | FileShare.Delete` (the browser may have the file open). IO errors → user-readable error.
- Settings page (`SettingsView.xaml`): a third button **"Import bookmarks…"** next to Export…/Import…, same style, same
  `CanExportImport` enable rule (disabled read-only). Click opens a themed `ContextMenu` (reuse `Services/ThemedMenuFactory`,
  placement below the button, auto-hide suppression like the list context menu) listing every discovered source by
  `DisplayName`, a separator, then **"From HTML file…"** (OpenFileDialog, filter `*.html;*.htm`). No sources found → only the
  HTML item.
- Flow mirrors the existing config Import (`SettingsViewModel.RequestImport` → `ImportParsed` event → MainViewModel): parse in
  the Settings VM (errors → the settings page's own error line, nothing changes), raise `BookmarksParsed(FolderNode, sourceKey)`;
  MainViewModel applies it **immediately (no Merge/Replace prompt)** to the folder that was current when Settings opened,
  saves once, `_pruneUsage()` (replace drops old ids), returns to the List page at that folder with the imported folder
  selected, status line `"Imported N bookmarks from <source> (replaced the previous import)."` / `"Imported N bookmarks into '<folder>'."`.
  Unsaved settings edits are discarded exactly like the config-import path does — check what it does and do the same.
- Read-only guard in MainViewModel too (`BlockIfReadOnly`).

## 3. Docs
- README: new "Bookmark import" subsection (sources, HTML export hint for Firefox: Library → Import and Backup → Export
  Bookmarks to HTML; replace semantics; javascript: bookmarklets skipped; empty folders dropped; one-time, no sync), plus the
  settings-page button in the settings description. `tasks/todo.md`: add a short "Ek özellik: yer imi içe aktarma" review
  section in Turkish in the existing style (what, tests, what wasn't live-verified).

## 4. Verification
- `dotnet build` Debug + Release 0 warnings; `dotnet test` green (the `Search_5000Nodes_MedianUnder16Milliseconds` timing test
  is known-flaky under load — rerun once if it alone fails, report it).
- A read-only sanity run of the parsers against the real files on this machine is allowed **only by reading** them from a test
  harness/scratch console (never modify browser files); report counts. Do NOT launch the app, no mouse/keyboard automation,
  no screenshots. Never touch `%APPDATA%\Your Launcher\`.
- Do not commit.
