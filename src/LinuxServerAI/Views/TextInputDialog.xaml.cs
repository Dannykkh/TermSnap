using System.Windows;

namespace Nebula.Views;

/// <summary>
/// 텍스트 입력 대화상자
/// </summary>
public partial class TextInputDialog : Window
{
    public string InputText => InputTextBox.Text;

    public TextInputDialog(string prompt, string title = "입력", string defaultValue = "")
    {
        InitializeComponent();

        Title = title;
        PromptTextBlock.Text = prompt;
        InputTextBox.Text = defaultValue;

        InputTextBox.Focus();
        InputTextBox.SelectAll();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
