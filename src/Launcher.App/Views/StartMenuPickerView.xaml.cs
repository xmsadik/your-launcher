using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YourLauncher.App.ViewModels;

namespace YourLauncher.App.Views;

/// <summary>
/// "Import from Start menu" page. Keyboard focus stays in the filter box (type to filter); this view's
/// tunneling key handler drives the checklist: Up/Down/PageUp/PageDown move, Space toggles, Ctrl+A
/// checks/unchecks everything shown, Enter imports, Esc cancels. Space is taken from the box on purpose - the
/// filter matches every typed word anywhere in the name, so it never needs a literal space.
/// </summary>
public partial class StartMenuPickerView : UserControl
{
    private const int PageSize = 8;

    private StartMenuPickerViewModel? _subscribedTo;

    public StartMenuPickerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private StartMenuPickerViewModel? ViewModel => DataContext as StartMenuPickerViewModel;

    /// <summary>Called by MainWindow right after this page becomes visible.</summary>
    public void FocusFirstField()
    {
        FilterBox.Focus();
        Keyboard.Focus(FilterBox);
        FilterBox.SelectAll();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedTo is not null)
        {
            _subscribedTo.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribedTo = e.NewValue as StartMenuPickerViewModel;
        if (_subscribedTo is not null)
        {
            _subscribedTo.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StartMenuPickerViewModel.SelectedIndex) && ViewModel is { } vm
            && vm.SelectedIndex >= 0 && vm.SelectedIndex < vm.Items.Count)
        {
            PickList.ScrollIntoView(vm.Items[vm.SelectedIndex]);
        }
    }

    private void StartMenuPickerView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Down:
                vm.MoveSelection(1);
                e.Handled = true;
                break;

            case Key.Up:
                vm.MoveSelection(-1);
                e.Handled = true;
                break;

            case Key.PageDown:
                vm.MoveSelection(PageSize);
                e.Handled = true;
                break;

            case Key.PageUp:
                vm.MoveSelection(-PageSize);
                e.Handled = true;
                break;

            case Key.Space when !ctrl:
                vm.ToggleSelected();
                e.Handled = true;
                break;

            case Key.A when ctrl:
                vm.ToggleAllShown();
                e.Handled = true;
                break;

            case Key.Enter:
                vm.Confirm();
                e.Handled = true;
                break;

            case Key.Escape:
                vm.Cancel();
                e.Handled = true;
                break;
        }
    }

    /// <summary>A click selects the row and toggles it; the ListBox items are not focusable so the filter box keeps the keyboard.</summary>
    private void PickItem_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is { } vm && sender is ListBoxItem { DataContext: StartMenuPickItem item })
        {
            vm.SelectIndex(vm.Items.IndexOf(item));
            vm.ToggleSelected();
            e.Handled = true;
        }
    }
}
