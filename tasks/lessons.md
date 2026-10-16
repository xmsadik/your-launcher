# Lessons

## 2026-09-27 — "0 results" from a real-data sanity run is a red flag, not a pass
- What happened: the bookmark-import sanity run reported "Chrome Default: 0 bookmarks (all roots empty)" and it was
  accepted as correct. The user does have Chrome bookmarks — signed-in Chrome stores them in `AccountBookmarks`,
  not `Bookmarks`.
- Rule: when a read against the user's real data returns zero/empty for something the user is likely to use, check
  the data source before reporting (list the directory, look for sibling/alternate files, check sizes) instead of
  explaining the empty result away.
- Rule: for third-party file formats (browser profiles, app data), verify the current on-disk layout on this machine
  rather than relying on the historical/documented layout.

## 2026-09-28 — Real user data leaked into repo files, found only when going public
- What happened: names from the user's real bookmarks (customer/work folders) seen in a sanity run were written
  into tasks/todo.md, a code comment and a test fixture. Making the repo public required a history rewrite and a
  new repo (force-pushed-away commits stay reachable by SHA on GitHub).
- Rule: never copy real user data (bookmark/folder/file names, paths, account names) into repo files, tests or
  commit messages — describe results with counts and neutral examples.
- Rule: before a repo goes public, scan files *and* full history (`git log -p --all`) for personal/customer data.
- 2026-09-29 (0.1.1 release): `pack-store.ps1` writes the same `dist\YourLauncher_<v>_<arch>.msix` paths as
  `pack.ps1`, so running it after `pack.ps1 -Sign` silently replaced the signed GitHub packages with unsigned ones.
- Rule: build the Store bundle first, the `-Sign`ed packages last, and check each with `signtool verify /pa`
  before uploading (for self-signed, "terminated in a root ... not trusted" = signed).

## 2026-10-14 — Python string inside a shell heredoc silently dropped a report
- What happened: a long Turkish report was inserted with a Python heredoc; a Windows path in it (`examples\plugins\uuid`)
  formed a `\u` escape, Python failed, and the chained `git commit` still ran, so the commit message claimed a
  report that was not in the file.
- Rule: write long text with the Write tool (or a raw file), never as a Python/heredoc string literal; chain
  `git commit` with `&&` only after a step that really checks the content landed.

## 2026-10-14 — Agents write binary protocols from memory
- What happened: the Everything IPC backend was written from memory and only tested against a fake server
  written from the same memory.
- Rule: for a third-party wire format, check constants and struct layout against the vendor's official header
  or docs before shipping, and say plainly when no real instance was available to test against.

## 2026-10-14 — Check the user's actual machine before trusting an API's documented behavior
- What happened: SetSuspendState(false) hibernates on Modern Standby machines; `powercfg /a` showed the user's
  laptop has no S3. Likewise GlazeWM's cloaked windows share the cloak reason with Store-app ghosts; listing the
  real windows showed the class names that separate them.
- Rule: when a feature depends on OS state (power states, window manager behavior), measure it on the user's
  machine with a read-only probe before choosing the rule.

## 2026-10-15 — Manifest schema written from memory, caught only by packing
- What happened: the app execution alias was added as `uap5:AppExecutionAlias` > `desktop:ExecutionAlias`. Both the
  reviewing and the implementing agent "knew" this was right; build and tests passed. `makeappx pack` rejected it
  (`desktop:ExecutionAlias` belongs under `uap3:AppExecutionAlias`; under uap5 it must be `uap5:ExecutionAlias`).
- Rule: after any AppxManifest change, pack a *scratch copy* of `dist\layout-<arch>` with the substituted manifest
  (makeappx pack /d <copy> /p <scratch>.msix) before calling it done. Never run pack.ps1/pack-store.ps1 for this —
  they overwrite the signed packages in dist\.

## 2026-10-15 — UI measurements from a non-DPI-aware PowerShell
- What happened: window rects read from PowerShell were DPI-virtualized (2560x1440 reported on a 3840x2160 @150%
  screen); a report said "100% scale". CopyFromScreen then captured the wrong region.
- Rule: call SetProcessDpiAwarenessContext(-4) first in any measuring/capturing script; state the real resolution
  and scale. Also: a manually started YourLauncher shows its panel on start (only `--silent` / the startup task
  doesn't) — start the first test instance with `--silent` when testing the "show" request, and wait for
  WaitForExit() + a pause between instances on the same config dir (the single-instance mutex lingers).

## 2026-10-15 — Screen capture missed the layered (docked) panel
- What happened: `Graphics.CopyFromScreen` returned frames without the docked panel, because plain BitBlt skips
  layered windows; .NET also rejects `SourceCopy | CaptureBlt` as an enum value.
- Rule: capture with P/Invoke `BitBlt(..., SRCCOPY | CAPTUREBLT = 0x40CC0020)` when the target may be a layered
  window, and check the desktop is not in use by another session before trusting frame timings.
