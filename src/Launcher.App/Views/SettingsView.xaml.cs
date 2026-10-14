using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using YourLauncher.App.Plugins;
using YourLauncher.App.Services;
using YourLauncher.App.ViewModels;
using YourLauncher.Core.Bookmarks;
using YourLauncher.Core.Model;
using YourLauncher.Core.Theming;

namespace YourLauncher.App.Views;

/// <summary>
/// The settings page (<c>Ctrl+,</c>, spec §11, revised §10 items 1/11-13). Same shape as
/// <see cref="EditorView"/>: owns its own keyboard handling while visible, Enter/Ctrl+S saves, Esc
/// cancels. The hotkey box is the one field with extra behavior - see
/// <see cref="SettingsViewModel.BeginHotkeyCapture"/>.
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        ThemeCombo.ItemsSource = ThemeCatalog.Choices.Select(c => c.DisplayName).ToList();
        DataContextChanged += (_, _) => SyncCombosFromViewModel();
    }

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    /// <summary>Called by MainWindow right after this page becomes visible.</summary>
    public void FocusFirstField()
    {
        SyncCombosFromViewModel();
        HotkeyBox.Focus();
    }

    private void SyncCombosFromViewModel()
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        ThemeCombo.SelectionChanged -= ThemeCombo_OnSelectionChanged;
        ThemeCombo.SelectedIndex = IndexOfTheme(vm.Theme);
        ThemeCombo.SelectionChanged += ThemeCombo_OnSelectionChanged;

        ShellCombo.SelectionChanged -= ShellCombo_OnSelectionChanged;
        ShellCombo.SelectedIndex = (int)vm.DefaultShell;
        ShellCombo.SelectionChanged += ShellCombo_OnSelectionChanged;
    }

    private void SettingsView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            vm.RequestCancel();
            e.Handled = true;
            return;
        }

        // ↑/↓ in the Visible rows box adjust the value directly (spec §10 revision item 11), clamped
        // 3-20; the model itself (Settings.MaxVisibleItems) stays unclamped until Save re-clamps it too.
        if ((e.Key == Key.Up || e.Key == Key.Down) && ReferenceEquals(Keyboard.FocusedElement, MaxVisibleItemsBox))
        {
            vm.MaxVisibleItems = Math.Clamp(vm.MaxVisibleItems + (e.Key == Key.Up ? 1 : -1), 3, 20);
            e.Handled = true;
            return;
        }

        // Same for Background opacity, in steps of 5.
        if ((e.Key == Key.Up || e.Key == Key.Down) && ReferenceEquals(Keyboard.FocusedElement, OpacityBox))
        {
            vm.Opacity = Math.Clamp(vm.Opacity + (e.Key == Key.Up ? 5 : -5), Settings.MinOpacity, 100);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            vm.RequestSave();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            // Spec §10 revision item 11: Enter saves unless a ComboBox dropdown is open, in which case it
            // should commit/close the dropdown instead (default ComboBox behavior) - don't steal it.
            if (Keyboard.FocusedElement is ComboBox { IsDropDownOpen: true })
            {
                return;
            }

            vm.RequestSave();
            e.Handled = true;
        }
    }

    private void HotkeyBox_OnGotFocus(object sender, RoutedEventArgs e) => ViewModel?.BeginHotkeyCapture();

    private void HotkeyBox_OnLostFocus(object sender, RoutedEventArgs e) => ViewModel?.EndHotkeyCapture();

    /// <summary>
    /// Spec §10 item 1: Tab/Shift+Tab/Esc/Enter keep their normal meaning (focus move, page cancel, page
    /// save), Backspace resets to the saved hotkey; the key-to-text rules live in <see cref="HotkeyCapture"/>
    /// (shared with the item editor's hotkey box).
    /// </summary>
    private void HotkeyBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        switch (HotkeyCapture.Interpret(e, out var text))
        {
            case HotkeyCaptureAction.Backspace:
                vm.ResetHotkeyToSaved();
                break;
            case HotkeyCaptureAction.Captured:
                vm.HotkeyText = text;
                break;
        }
    }

    private void ThemeCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is { } vm && ThemeCombo.SelectedIndex >= 0)
        {
            vm.Theme = ThemeCatalog.Choices[ThemeCombo.SelectedIndex].Theme;
        }
    }

    private static int IndexOfTheme(Theme theme)
    {
        for (var i = 0; i < ThemeCatalog.Choices.Count; i++)
        {
            if (ThemeCatalog.Choices[i].Theme == theme)
            {
                return i;
            }
        }

        return 0;
    }

    private void ShellCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is { } vm && ShellCombo.SelectedIndex >= 0)
        {
            vm.DefaultShell = (ShellKind)ShellCombo.SelectedIndex;
        }
    }

    /// <summary>Creates the plugins folder on first use so there is always something to open (and to drop a plugin into).</summary>
    private void OpenPluginsFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        try
        {
            var folder = Path.Combine(vm.ConfigDirectory, PluginProviderHost.FolderName);
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(PackageContext.ToPhysicalPath(folder)) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[YourLauncher] Could not open the plugins folder: {ex.Message}");
        }
    }

    private void OpenConfigFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(PackageContext.ToPhysicalPath(vm.ConfigDirectory)) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            Debug.WriteLine($"[YourLauncher] Could not open the config folder: {ex.Message}");
        }
    }

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        WithAutoHideSuppressed(owner =>
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export config",
                FileName = $"your-launcher-export-{DateTime.Now:yyyyMMdd}.json",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = ".json",
            };

            if (dialog.ShowDialog(owner) == true)
            {
                vm.RequestExport(dialog.FileName);
            }
        });
    }

    private void ImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        WithAutoHideSuppressed(owner =>
        {
            var dialog = new OpenFileDialog { Title = "Import config", Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*" };
            if (dialog.ShowDialog(owner) == true)
            {
                vm.RequestImport(dialog.FileName);
            }
        });
    }

    /// <summary>
    /// "Import bookmarks…" (bookmark spec §2): opens a themed <see cref="ContextMenu"/> below the button
    /// (reusing <see cref="ThemedMenuFactory"/>, the same look the list's right-click menu uses) listing
    /// every discovered browser source by <see cref="BookmarkSource.DisplayName"/>, then a separator, then
    /// "From HTML file…" - no separator (and no sources above it) when nothing was discovered.
    /// </summary>
    private void ImportBookmarksButton_OnClick(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        var sources = vm.DiscoverBookmarkSources();
        var style = ThemedMenuFactory.CreateMenuItemStyle();
        var menu = new ContextMenu
        {
            Style = ThemedMenuFactory.CreateMenuStyle(),
            OverridesDefaultStyle = true,
            PlacementTarget = ImportBookmarksButton,
            Placement = PlacementMode.Bottom,
        };

        foreach (var source in sources)
        {
            var captured = source; // avoid capturing the loop variable itself, though C# 5+ already scopes foreach per-iteration.
            menu.Items.Add(ThemedMenuFactory.CreateItem(captured.DisplayName, style, () => vm.RequestImportBookmarksFromSource(captured)));
        }

        if (sources.Count > 0)
        {
            menu.Items.Add(ThemedMenuFactory.CreateSeparator(ThemedMenuFactory.CreateSeparatorStyle()));
        }

        menu.Items.Add(ThemedMenuFactory.CreateItem("From HTML file…", style, () => ImportBookmarksFromHtml(vm)));

        OpenThemedContextMenu(menu);
    }

    /// <summary>"Import from Start menu…": hands over to MainViewModel, which swaps this page for the app picker.</summary>
    private void ImportStartMenuButton_OnClick(object sender, RoutedEventArgs e) => ViewModel?.RequestImportStartMenu();

    private void ImportBookmarksFromHtml(SettingsViewModel vm)
    {
        WithAutoHideSuppressed(owner =>
        {
            var dialog = new OpenFileDialog { Title = "Import bookmarks", Filter = "HTML bookmarks (*.html;*.htm)|*.html;*.htm|All files (*.*)|*.*" };
            if (dialog.ShowDialog(owner) == true)
            {
                vm.RequestImportBookmarksFromHtml(dialog.FileName);
            }
        });
    }

    /// <summary>Same auto-hide suppression MainWindow's list context menu uses (spec §5 there) - opening this menu must not hide the panel either.</summary>
    private void OpenThemedContextMenu(ContextMenu menu)
    {
        if (Window.GetWindow(this) is not MainWindow owner)
        {
            menu.IsOpen = true;
            return;
        }

        owner.BeginSuppressAutoHide();
        menu.Closed += (_, _) => owner.EndSuppressAutoHide();
        menu.IsOpen = true;
    }

    /// <summary>The panel hides itself on Deactivated (spec §5.1), which would otherwise fire the instant a dialog takes focus - same suppression pattern as EditorView's Browse… buttons.</summary>
    private void WithAutoHideSuppressed(Action<Window> action)
    {
        var owner = Window.GetWindow(this);
        if (owner is not MainWindow mainWindow)
        {
            if (owner is not null)
            {
                action(owner);
            }

            return;
        }

        mainWindow.BeginSuppressAutoHide();
        try
        {
            action(mainWindow);
        }
        finally
        {
            mainWindow.EndSuppressAutoHide();
            mainWindow.Activate();
        }
    }
}
