using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using YourLauncher.App.Services;
using YourLauncher.App.ViewModels;
using YourLauncher.Core.Icons;

namespace YourLauncher.App.Views;

/// <summary>
/// Ctrl+I icon picker (spec §4, revised §7.7-§7.12). Owns its own keyboard handling while visible, same
/// pattern as <see cref="EditorView"/>: this UserControl's <see cref="PreviewKeyDown"/> handles the global
/// shortcuts (Esc, Ctrl+Tab/Ctrl+Shift+Tab, Ctrl+1..4, Ctrl+0), tunneling down first; each tab's TextBox
/// then gets a chance via its own PreviewKeyDown to forward Up/Down/PageUp/PageDown/Enter to that tab's
/// grid (always) and Left/Right (only when the box is empty) - spec §7.8. Focus never leaves the active
/// tab's field/button.
/// </summary>
public partial class IconPickerView : UserControl
{
    public IconPickerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private IconPickerViewModel? ViewModel => DataContext as IconPickerViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is IconPickerViewModel oldVm)
        {
            oldVm.PropertyChanged -= ViewModel_OnPropertyChanged;
        }

        if (e.NewValue is IconPickerViewModel newVm)
        {
            newVm.PropertyChanged += ViewModel_OnPropertyChanged;
        }
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IconPickerViewModel.ActiveTab))
        {
            // Let the tab's Visibility bindings apply before moving focus into it (MainWindow's own pattern for page switches).
            Dispatcher.BeginInvoke(DispatcherPriority.Input, FocusFirstField);
        }
    }

    /// <summary>Called by MainWindow right after this page becomes visible, and whenever the active tab changes.</summary>
    public void FocusFirstField()
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        switch (vm.ActiveTab)
        {
            case IconPickerTab.Glyph:
                GlyphFilterBox.Focus();
                GlyphFilterBox.SelectAll();
                break;

            case IconPickerTab.Emoji:
                EmojiBox.Focus();
                EmojiBox.SelectAll();
                break;

            case IconPickerTab.File:
                BrowseFileButton.Focus();
                break;

            case IconPickerTab.Exe:
                ExePathBox.Focus();
                ExePathBox.SelectAll();
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Tab strip.
    // ---------------------------------------------------------------------------------------------

    private void GlyphTabButton_OnClick(object sender, RoutedEventArgs e) => ViewModel?.SetTab(IconPickerTab.Glyph);

    private void EmojiTabButton_OnClick(object sender, RoutedEventArgs e) => ViewModel?.SetTab(IconPickerTab.Emoji);

    private void FileTabButton_OnClick(object sender, RoutedEventArgs e) => ViewModel?.SetTab(IconPickerTab.File);

    private void ExeTabButton_OnClick(object sender, RoutedEventArgs e) => ViewModel?.SetTab(IconPickerTab.Exe);

    // ---------------------------------------------------------------------------------------------
    // Global shortcuts (spec §7.8), tunneling in before any tab's own field handler.
    // ---------------------------------------------------------------------------------------------

    private void IconPickerView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            vm.Cancel();
            e.Handled = true;
            return;
        }

        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Tab:
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    vm.PreviousTab();
                }
                else
                {
                    vm.NextTab();
                }

                e.Handled = true;
                break;

            case Key.D1 or Key.NumPad1:
                vm.SetTab(IconPickerTab.Glyph);
                e.Handled = true;
                break;

            case Key.D2 or Key.NumPad2:
                vm.SetTab(IconPickerTab.Emoji);
                e.Handled = true;
                break;

            case Key.D3 or Key.NumPad3:
                vm.SetTab(IconPickerTab.File);
                e.Handled = true;
                break;

            case Key.D4 or Key.NumPad4:
                vm.SetTab(IconPickerTab.Exe);
                e.Handled = true;
                break;

            case Key.D0 or Key.NumPad0:
                vm.ResetToDefault();
                e.Handled = true;
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Glyph tab.
    // ---------------------------------------------------------------------------------------------

    private void GlyphFilterBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                vm.MoveGlyphSelection(GridDirection.Up);
                e.Handled = true;
                break;
            case Key.Down:
                vm.MoveGlyphSelection(GridDirection.Down);
                e.Handled = true;
                break;
            case Key.PageUp:
                vm.MoveGlyphSelection(GridDirection.PageUp);
                e.Handled = true;
                break;
            case Key.PageDown:
                vm.MoveGlyphSelection(GridDirection.PageDown);
                e.Handled = true;
                break;
            case Key.Enter:
                vm.ApplyGlyphSelection();
                e.Handled = true;
                break;
            case Key.Left when GlyphFilterBox.Text.Length == 0:
                vm.MoveGlyphSelection(GridDirection.Left);
                e.Handled = true;
                break;
            case Key.Right when GlyphFilterBox.Text.Length == 0:
                vm.MoveGlyphSelection(GridDirection.Right);
                e.Handled = true;
                break;
        }
    }

    private void GlyphList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is { } vm && GlyphList.SelectedIndex >= 0)
        {
            vm.GlyphSelectedIndex = GlyphList.SelectedIndex;
        }
    }

    private void GlyphList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => ViewModel?.ApplyGlyphSelection();

    // ---------------------------------------------------------------------------------------------
    // Emoji tab.
    // ---------------------------------------------------------------------------------------------

    private void EmojiBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel?.ApplyEmoji();
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // File tab.
    // ---------------------------------------------------------------------------------------------

    private void BrowseFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        WithAutoHideSuppressed(owner =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select icon file",
                Filter = "Icon images (*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.gif",
            };

            if (dialog.ShowDialog(owner) != true)
            {
                return;
            }

            try
            {
                var iconsDir = Path.Combine(ConfigService.ResolveConfigDirectory(), "icons");
                var imported = IconFileStore.Import(dialog.FileName, iconsDir);
                vm.ApplyImportedFile(imported);
            }
            catch (ArgumentException)
            {
                // Unsupported extension despite the dialog's filter (e.g. a renamed file) - leave the picker open.
            }
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Exe/DLL tab.
    // ---------------------------------------------------------------------------------------------

    private void ExePathBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                vm.MoveExeSelection(GridDirection.Up);
                e.Handled = true;
                break;
            case Key.Down:
                vm.MoveExeSelection(GridDirection.Down);
                e.Handled = true;
                break;
            case Key.PageUp:
                vm.MoveExeSelection(GridDirection.PageUp);
                e.Handled = true;
                break;
            case Key.PageDown:
                vm.MoveExeSelection(GridDirection.PageDown);
                e.Handled = true;
                break;
            case Key.Enter:
                e.Handled = true;
                _ = HandleExeEnterAsync(vm);
                break;
            case Key.Left when ExePathBox.Text.Length == 0:
                vm.MoveExeSelection(GridDirection.Left);
                e.Handled = true;
                break;
            case Key.Right when ExePathBox.Text.Length == 0:
                vm.MoveExeSelection(GridDirection.Right);
                e.Handled = true;
                break;
        }
    }

    private static async Task HandleExeEnterAsync(IconPickerViewModel vm)
    {
        if (vm.HasExePathChangedSinceLastLoad)
        {
            await vm.LoadExeIconsAsync();
        }
        else
        {
            vm.ApplyExeSelection();
        }
    }

    private void BrowseExeButton_OnClick(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        WithAutoHideSuppressed(owner =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select exe or dll",
                Filter = "Programs and libraries (*.exe;*.dll)|*.exe;*.dll|All files (*.*)|*.*",
            };

            if (dialog.ShowDialog(owner) == true)
            {
                vm.ExePath = dialog.FileName;
            }
        });

        ExePathBox.Focus();
    }

    private async void LoadExeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            await vm.LoadExeIconsAsync();
        }
    }

    private void ExeIconsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is { } vm && ExeIconsList.SelectedIndex >= 0)
        {
            vm.ExeSelectedIndex = ExeIconsList.SelectedIndex;
        }
    }

    private void ExeIconsList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => ViewModel?.ApplyExeSelection();

    // ---------------------------------------------------------------------------------------------
    // Shared with EditorView: the panel hides itself on Deactivated (spec §5.1), which would otherwise
    // fire the instant a file dialog takes focus.
    // ---------------------------------------------------------------------------------------------

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
