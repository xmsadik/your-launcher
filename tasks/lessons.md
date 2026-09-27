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
