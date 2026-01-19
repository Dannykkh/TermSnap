using System.Windows;

namespace Nebula.Views;

/// <summary>
/// 명령어 확인 및 편집 대화상자
/// </summary>
public partial class CommandConfirmDialog : Window
{
    public string EditedCommand { get; private set; }

    public CommandConfirmDialog(string command, string? explanation = null)
    {
        InitializeComponent();

        CommandTextBox.Text = command;
        EditedCommand = command;

        if (!string.IsNullOrWhiteSpace(explanation))
        {
            ExplanationTextBlock.Text = explanation;
        }
        else
        {
            ExplanationTextBlock.Text = "명령어 설명을 가져올 수 없습니다.";
        }

        // 포커스를 명령어 텍스트박스에
        CommandTextBox.Focus();
        CommandTextBox.SelectAll();
    }

    private void ExecuteButton_Click(object sender, RoutedEventArgs e)
    {
        EditedCommand = CommandTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(EditedCommand))
        {
            MessageBox.Show(
                "명령어가 비어있습니다.",
                "오류",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
