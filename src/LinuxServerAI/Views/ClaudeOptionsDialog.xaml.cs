using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Nebula.Views;

/// <summary>
/// Claude Code 실행 옵션 다이얼로그
/// </summary>
public partial class ClaudeOptionsDialog : Window
{
    /// <summary>
    /// 생성된 명령어
    /// </summary>
    public string GeneratedCommand { get; private set; } = "claude";

    /// <summary>
    /// 초기 프롬프트 (있으면 실행 후 전송)
    /// </summary>
    public string? InitialPrompt => string.IsNullOrWhiteSpace(InitialPromptTextBox.Text)
        ? null
        : InitialPromptTextBox.Text.Trim();

    public ClaudeOptionsDialog()
    {
        InitializeComponent();
        UpdateCommandPreview();
    }

    /// <summary>
    /// 옵션 변경 시 명령어 미리보기 업데이트
    /// </summary>
    private void OnOptionChanged(object sender, RoutedEventArgs e)
    {
        UpdateCommandPreview();
    }

    private void OnOptionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCommandPreview();
    }

    private void OnOptionChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateCommandPreview();
    }

    private void OnOptionChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCommandPreview();
    }

    /// <summary>
    /// 명령어 미리보기 업데이트
    /// </summary>
    private void UpdateCommandPreview()
    {
        if (CommandPreviewTextBox == null) return;

        var parts = new List<string> { "claude" };

        // 권한 모드
        if (PermissionModeCombo?.SelectedItem is ComboBoxItem permItem)
        {
            var tag = permItem.Tag?.ToString();
            if (!string.IsNullOrEmpty(tag))
                parts.Add(tag);
        }

        // 모델
        if (ModelCombo?.SelectedItem is ComboBoxItem modelItem)
        {
            var tag = modelItem.Tag?.ToString();
            if (!string.IsNullOrEmpty(tag))
                parts.Add(tag);
        }

        // 출력 형식
        if (OutputFormatCombo?.SelectedItem is ComboBoxItem outputItem)
        {
            var tag = outputItem.Tag?.ToString();
            if (!string.IsNullOrEmpty(tag))
                parts.Add(tag);
        }

        // 최대 턴 수
        if (MaxTurnsSlider?.Value > 0)
        {
            parts.Add($"--max-turns {(int)MaxTurnsSlider.Value}");
        }

        // 체크박스 옵션들
        if (VerboseCheck?.IsChecked == true)
            parts.Add("--verbose");

        if (NoConfigCheck?.IsChecked == true)
            parts.Add("--no-config");

        if (ResumeCheck?.IsChecked == true)
            parts.Add("--resume");

        if (ContinueCheck?.IsChecked == true)
            parts.Add("--continue");

        // 시스템 프롬프트
        if (!string.IsNullOrWhiteSpace(SystemPromptTextBox?.Text))
        {
            var prompt = SystemPromptTextBox.Text.Trim().Replace("\"", "\\\"");
            parts.Add($"--system-prompt \"{prompt}\"");
        }

        GeneratedCommand = string.Join(" ", parts);
        CommandPreviewTextBox.Text = GeneratedCommand;
    }

    /// <summary>
    /// 명령어 복사
    /// </summary>
    private void CopyCommand_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(GeneratedCommand);
        MessageBox.Show("명령어가 클립보드에 복사되었습니다.", "복사 완료",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// 취소
    /// </summary>
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// 실행
    /// </summary>
    private void Run_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
