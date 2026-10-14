# Privacy Policy

_Last updated: 2026-10-14_

Your Launcher is a keyboard-first Windows launcher: you build your own tree of apps, folders, files, URLs and
commands, and open it with a global hotkey. This policy explains what it does with your information.

## Summary

Your Launcher does **not** collect, transmit, sell, or share any personal information. The app itself makes
no network connections and has no telemetry, analytics, advertising or accounts. Everything it stores stays on
your device. (Plugins you install yourself are separate programs; see below.)

## What the app reads on your device

Only on your own computer, and only to do what you ask it to:

- **The items you add:** the paths, URLs, commands, arguments and names you enter, and the files, folders and
  shortcuts (`.lnk`, `.url`) you drag onto the panel. For a dragged shortcut it reads the shortcut's target,
  arguments, working folder and icon.
- **Icons and descriptions:** the system icon of each item's target, and the file description of programs you
  add, so the list can show them.
- **Browser bookmarks, only when you use "Import bookmarks…":** the bookmark files of Chromium-based browsers
  installed on your device (Chrome, Edge, Brave, Vivaldi, Chromium, Opera), or a bookmarks HTML file you pick.
  They are read once, when you choose a source; browser files are never modified, and nothing is synced.
- **Start menu, only when you use "Import from Start menu…":** the shortcuts in your Start menu folders and the
  list of installed apps, read once so you can pick which ones to add.
- **Open windows, while you type in the search box:** the titles and program names of your open windows, so
  you can switch to one. They are read on demand, kept in memory for under a second, and never stored.
- **File search, only when you type the file-search prefix (`f ` by default):** your search text is sent to
  the Windows Search index on your device, or to the "Everything" search tool if it is installed and running,
  both of which run locally. Results are shown and not stored, except for how often you opened a result,
  which is kept in `usage.json` like any other item.
- **Clipboard, only when you ask:** a calculator result or a plugin result is copied when you press Enter on it.
  The app never reads the clipboard.

## What the app stores

All of it in the app's own folder on your device (`%APPDATA%\Your Launcher`, or the app's package folder when
installed from the Microsoft Store):

- **`config.json`** (and `config.backup.json`): your item tree and settings.
- **`usage.json`:** how often and when you opened each item, used only to rank search results.
- **`icons\`:** copies of custom icon images you pick for items.
- **`error.log`:** technical details of unexpected errors (these may include file paths from your item list),
  so problems can be diagnosed. It is never sent anywhere.

When you export your configuration, the file goes wherever you choose to save it.

Items you launch run as normal programs on your device; what they do is governed by their own privacy terms.

## Plugins

Plugins are optional programs that **you** place in the `plugins` folder; the app does not download, install or
update any. When you type a plugin's keyword, the app starts that plugin and passes it the text you typed after
the keyword, and shows the results it returns. A plugin is not part of Your Launcher: it can do anything a
program on your computer can do, including network access, and is governed by whoever wrote it. Only install
plugins you trust.

## Removing your data

Uninstalling the Microsoft Store version removes the data it created. You can also delete the folders above
at any time.

## Changes to this policy

If this policy changes, the updated version will be published at this address with a new "Last updated" date.

## Contact

For questions about this policy, contact the publisher through the support link on the app's Microsoft Store
page.
