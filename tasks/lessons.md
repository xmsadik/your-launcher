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
