using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TermSnap.ViewModels;

namespace TermSnap.Views;

/// <summary>
/// 새 세션 선택 화면
/// </summary>
public partial class NewSessionSelectorView : UserControl
{
    public NewSessionSelectorView()
    {
        InitializeComponent();
    }

    private void Card_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(79, 195, 247)); // #4FC3F7
            border.Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)); // #2D2D30
        }
    }

    private void Card_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)); // #3C3C3C
            border.Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)); // #252526
        }
    }

    private void LocalTerminal_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is NewSessionSelectorViewModel vm)
        {
            vm.SelectLocalTerminalCommand.Execute(null);
        }
    }

    private void SshServer_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is NewSessionSelectorViewModel vm)
        {
            vm.SelectSshServerCommand.Execute(null);
        }
    }
}
