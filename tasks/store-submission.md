# Microsoft Store gönderimi — kopyala/yapıştır metinleri

Partner Center alanlarına aynen yapıştırılacak metinler aşağıda. Metinler İngilizce (en-us listing).
Kimlik: `ABAPer.YourLauncher`, Publisher `CN=48947E55-115C-445B-9841-39A5BCF271DD`, PublisherDisplayName `ABAPer`.

## Properties
- **Category:** Productivity. Alternatif: Utilities & tools.
- **Privacy policy URL:** ⚠ KARAR BEKLİYOR — `your-launcher` reposu **private**; `PRIVACY.md` herkese açık bir
  adreste yayınlanmalı (repo'yu public yapmak, ya da ayrı bir public sayfa/gist).
- **Website:** aynı karara bağlı (public repo ise repo URL'si).
- **Support contact:** aynı karara bağlı (public repo ise `/issues`), yoksa e-posta.

## Pricing and availability
- Free, all markets.

## Store listing (English (United States))

**Description**

```
Your Launcher is a keyboard-first launcher for Windows. Nothing is indexed behind your back: you build your own tree of apps, folders, files, web links and commands, and open it instantly with a global hotkey (Alt+Space by default).

• Your own tree: group items into folders and sub-folders, reorder them, and move them around by keyboard or drag and drop.
• Fast, forgiving search across the whole tree, ranked by what you open most.
• Five item types: apps (with arguments, working folder and run as administrator), files and folders, URLs, and shell commands (PowerShell, cmd or pwsh; visible or hidden).
• Drag files, folders, shortcuts and .url links from Explorer or the Start menu straight onto the panel.
• Import bookmarks from Chrome, Edge, Brave, Vivaldi, Opera or any browser's bookmarks HTML export.
• Automatic system icons, or pick your own.
• Fully keyboard driven: add, edit, cut, paste, duplicate and delete without touching the mouse.
• Light and dark themes with Mica/Acrylic, tray icon, and optional start with Windows (silently, in the tray).
• Opens on the monitor you're working on, and launched windows follow it.
• Your configuration is a plain JSON file you can back up, export and import.

Your Launcher has no network access and collects no data.
```

**Short description**

```
Keyboard-first launcher: your own tree of apps, folders, links and commands, one hotkey away.
```

**Search terms (en fazla 7)**
- launcher
- app launcher
- hotkey
- quick launch
- productivity
- shortcuts
- keyboard

**Features (satır satır)**
- Your own tree of apps, folders, files, URLs and commands
- Global hotkey, opens on the monitor you're working on
- Fast whole-tree search ranked by usage
- Drag and drop from Explorer and the Start menu
- Browser bookmark import
- Light/dark theme, tray icon, start with Windows
- No network access, no data collection

**Screenshots:** kullanıcı çekecek, 1–4 adet (en az 1366x768, tercihen 1920x1080). Önerilen:
1. Panel açık, klasörlü bir ağaç (koyu tema), ikonlarla.
2. Arama: birkaç harfle sonuç listesi.
3. Editör sayfası (bir app node'u düzenlenirken).
4. Ayarlar sayfası veya açık tema.

## Restricted capabilities — justification (alan sınırı 500 karakter)

**runFullTrust** (469 karakter)

```
Your Launcher is a desktop (WPF) app launcher, so it runs as a full-trust packaged process. Its core features need Win32 APIs unavailable in an AppContainer: RegisterHotKey for the global hotkey that opens it, Shell_NotifyIcon for its tray icon, and ShellExecuteEx/CreateProcess to start the apps, files, folders, URLs and commands the user added, only when the user picks one. It also reads icons and .lnk targets of those items. No network access, no data collection.
```

## Submission options → Notes for certification / Additional testing information

```
Your Launcher lives in the system tray and opens with a global hotkey.

To test:
1. Launch "Your Launcher" from Start. On first run the panel opens once, and a tray icon appears.
2. Press Alt+Space to show/hide the panel (if another app already uses Alt+Space, the settings page opens so a different hotkey can be chosen). Esc hides it.
3. Press Ctrl+N to add an item: choose "App", enter "notepad.exe" as the target, save with Ctrl+Enter. Press Enter on it to launch Notepad.
4. Ctrl+Shift+N adds a folder. Type a few letters to search the whole tree.
5. Ctrl+, opens Settings (hotkey, theme, "Start with Windows", import/export).
6. Right-click the tray icon for Show, Settings and Exit.

Requirements: Windows 11.
```

## Age rating (IARC)
Tüm sorulara "No": şiddet yok, kullanıcılar arası iletişim yok, veri paylaşımı yok, satın alma yok.
Not: uygulama kullanıcının girdiği URL'leri tarayıcıda açabilir ama kendisi web içeriği göstermez
("unrestricted web access" sorusuna "No"). Beklenen: 3+ / Everyone.
