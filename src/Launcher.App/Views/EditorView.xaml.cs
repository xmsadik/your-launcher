using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using YourLauncher.App.ViewModels;
using YourLauncher.Core.Model;

namespace YourLauncher.App.Views;

/// <summary>
/// The add/edit form (spec §9, Phase 3). Owns its own keyboard handling while it's visible: Tab/Shift+Tab
/// are plain WPF focus traversal, Enter saves (except inside <see cref="CommandBox"/>, where Enter
/// inserts a newline and Ctrl+Enter saves instead), Esc cancels, Alt+A toggles the Advanced section.
/// Shell/Window are plain ComboBoxes kept in sync with the ViewModel by hand (ComboBoxItem order matches
/// the corresponding enum's declaration order, so the cast is just the selected index) rather than a
/// converter, since both directions (VM -> UI on open, UI -> VM on selection) are one-shot glue, not a
/// binding that needs to react to the VM changing the enum itself later.
/// </summary>
public partial class EditorView : UserControl
{
    public EditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => SyncCombosFromViewModel();
    }

    private EditorViewModel? ViewModel => DataContext as EditorViewModel;

    /// <summary>Called by MainWindow right after this page becomes visible (spec §9: Target first for app/path, otherwise Name).</summary>
    public void FocusFirstField()
    {
        SyncCombosFromViewModel();
        if (ViewModel?.FocusTargetFirst == true)
        {
            TargetBox.Focus();
            TargetBox.SelectAll();
        }
        else
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }
    }

    private void SyncCombosFromViewModel()
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        ShellCombo.SelectionChanged -= ShellCombo_OnSelectionChanged;
        ShellCombo.SelectedIndex = (int)vm.Shell;
        ShellCombo.SelectionChanged += ShellCombo_OnSelectionChanged;

        WindowCombo.SelectionChanged -= WindowCombo_OnSelectionChanged;
        WindowCombo.SelectedIndex = (int)vm.Window;
        WindowCombo.SelectionChanged += WindowCombo_OnSelectionChanged;
    }

    private void EditorView_OnPreviewKeyDown(object sender, KeyEventArgs e)
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

        if (e.Key == Key.Enter)
        {
            var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var inCommandBox = ReferenceEquals(Keyboard.FocusedElement, CommandBox);

            if (inCommandBox && !ctrl)
            {
                return; // let the multi-line TextBox insert a newline (spec §9).
            }

            vm.RequestSave();
            e.Handled = true;
            return;
        }

        // Alt-held key presses arrive as "system keys": e.Key is Key.System and the actual key is in
        // e.SystemKey, rather than e.Key itself (a WPF-specific quirk, unlike WinForms' separate
        // SystemKeyDown event).
        var effectiveKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (effectiveKey == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            vm.IsAdvancedExpanded = !vm.IsAdvancedExpanded;
            e.Handled = true;
        }
    }

    private void AdvancedToggle_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.IsAdvancedExpanded = !vm.IsAdvancedExpanded;
        }
    }

    private void ShellCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is { } vm && ShellCombo.SelectedIndex >= 0)
        {
            vm.Shell = (ShellChoice)ShellCombo.SelectedIndex;
        }
    }

    private void WindowCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is { } vm && WindowCombo.SelectedIndex >= 0)
        {
            vm.Window = (WindowMode)WindowCombo.SelectedIndex;
        }
    }

    private void TargetBox_OnLostFocus(object sender, RoutedEventArgs e) => ViewModel?.SuggestNameFromTarget();

    private void BrowseFile_OnClick(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        WithAutoHideSuppressed(owner =>
        {
            var dialog = new OpenFileDialog { Title = "Select file" };
            if (dialog.ShowDialog(owner) == true)
            {
                vm.Target = dialog.FileName;
                vm.SuggestNameFromTarget();
            }
        });

        TargetBox.Focus();
    }

    private void BrowseFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        WithAutoHideSuppressed(owner =>
        {
            var dialog = new OpenFolderDialog { Title = "Select folder" };
            if (dialog.ShowDialog(owner) == true)
            {
                vm.Target = dialog.FolderName;
                vm.SuggestNameFromTarget();
            }
        });

        TargetBox.Focus();
    }

    private void BrowseWorkingDirectory_OnClick(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        WithAutoHideSuppressed(owner =>
        {
            var dialog = new OpenFolderDialog { Title = "Select working directory" };
            if (dialog.ShowDialog(owner) == true)
            {
                vm.WorkingDirectory = dialog.FolderName;
            }
        });

        WorkingDirectoryBox.Focus();
    }

    /// <summary>
    /// The panel hides itself on Deactivated (spec §5.1), which would otherwise fire the instant a
    /// file/folder dialog takes focus. MainWindow exposes a suppression counter for exactly this.
    /// </summary>
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
