using System.Windows.Controls;
using System.Windows.Input;
using YourLauncher.App.ViewModels;

namespace YourLauncher.App.Views;

/// <summary>Ctrl+N type picker (spec §9 step 1 / decision D6). Owns its own keyboard handling while it's visible - see MainViewModel/PanelPage for why the search box's key handler never runs here.</summary>
public partial class TypePickerView : UserControl
{
    public TypePickerView()
    {
        InitializeComponent();
    }

    public void FocusFirstField() => Focus();

    private void TypePickerView_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not TypePickerViewModel vm)
        {
            return;
        }

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

            case Key.Enter:
                vm.ConfirmSelection();
                e.Handled = true;
                break;

            case Key.Escape:
                vm.Cancel();
                e.Handled = true;
                break;

            default:
                var letter = LetterFromKey(e.Key);
                if (letter is not null && vm.TrySelectByKey(letter.Value))
                {
                    e.Handled = true;
                }

                break;
        }
    }

    private static char? LetterFromKey(Key key) =>
        key is >= Key.A and <= Key.Z ? (char)('A' + (key - Key.A)) : null;
}
