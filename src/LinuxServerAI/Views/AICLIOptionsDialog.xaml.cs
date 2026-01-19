using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Nebula.Models;
using Nebula.Services;

namespace Nebula.Views;

/// <summary>
/// AI CLI 실행 옵션 다이얼로그
/// </summary>
public partial class AICLIOptionsDialog : Window
{
    private List<AICLIInfo> _cliList = new();
    private AICLIInfo? _selectedCLI;
    private bool _isManualEdit = false; // 사용자가 직접 명령어를 편집 중인지

    /// <summary>
    /// 생성된 명령어
    /// </summary>
    public string GeneratedCommand { get; private set; } = "claude";

    /// <summary>
    /// 자동 모드 여부
    /// </summary>
    public bool IsAutoMode => AutoModeCheck?.IsChecked == true;

    /// <summary>
    /// 초기 프롬프트 (있으면 실행 후 전송)
    /// </summary>
    public string? InitialPrompt => string.IsNullOrWhiteSpace(InitialPromptTextBox.Text)
        ? null
        : InitialPromptTextBox.Text.Trim();

    /// <summary>
    /// 선택된 CLI 정보
    /// </summary>
    public AICLIInfo? SelectedCLI => _selectedCLI;

    public AICLIOptionsDialog()
    {
        InitializeComponent();
        LoadCLIList();
        LoadSavedSettings();
        UpdateCommandPreview();
    }

    /// <summary>
    /// CLI 목록 로드
    /// </summary>
    private void LoadCLIList()
    {
        _cliList = AICLISettings.GetAvailableCLIs();
        CLICombo.ItemsSource = _cliList;
    }

    /// <summary>
    /// 저장된 설정 로드
    /// </summary>
    private void LoadSavedSettings()
    {
        var config = ConfigService.Load();
        var settings = config.AICLISettings;

        // 마지막 선택한 CLI 찾기
        var lastCLI = _cliList.FirstOrDefault(c => c.Id == settings.SelectedCLI);
        if (lastCLI != null)
        {
            CLICombo.SelectedItem = lastCLI;
        }
        else
        {
            CLICombo.SelectedIndex = 0; // 기본: Claude
        }

        // 자동 모드 설정
        AutoModeCheck.IsChecked = settings.LastAutoMode;

        // 커스텀 명령어
        if (!string.IsNullOrEmpty(settings.CustomCommand))
        {
            CustomCommandTextBox.Text = settings.CustomCommand;
        }
    }

    /// <summary>
    /// 설정 저장
    /// </summary>
    private void SaveSettings()
    {
        var config = ConfigService.Load();

        if (_selectedCLI != null)
        {
            config.AICLISettings.SelectedCLI = _selectedCLI.Id;
        }
        config.AICLISettings.LastCommand = GeneratedCommand;
        config.AICLISettings.LastAutoMode = AutoModeCheck?.IsChecked == true;

        if (_selectedCLI?.Id == "custom")
        {
            config.AICLISettings.CustomCommand = CustomCommandTextBox.Text;
        }

        ConfigService.Save(config);
    }

    /// <summary>
    /// CLI 선택 변경
    /// </summary>
    private void OnCLIChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CLICombo.SelectedItem is AICLIInfo cli)
        {
            _selectedCLI = cli;
            _isManualEdit = false; // CLI 변경 시 자동 생성 모드로 복귀
            CLIDescriptionText.Text = cli.Description;

            // 자동 모드 플래그 표시
            if (!string.IsNullOrEmpty(cli.AutoModeFlag))
            {
                AutoModeFlagText.Text = $"({cli.AutoModeFlag})";
                AutoModeCheck.IsEnabled = true;
            }
            else
            {
                AutoModeFlagText.Text = "(사용 불가)";
                AutoModeCheck.IsEnabled = false;
                AutoModeCheck.IsChecked = false;
            }

            // 커스텀 명령어 패널 표시/숨김
            CustomCommandPanel.Visibility = cli.Id == "custom"
                ? Visibility.Visible
                : Visibility.Collapsed;

            // CLI별 옵션 표시/숨김
            UpdateCLISpecificOptions(cli);

            UpdateCommandPreview();
        }
    }

    /// <summary>
    /// CLI별 옵션 표시 업데이트
    /// </summary>
    private void UpdateCLISpecificOptions(AICLIInfo cli)
    {
        // 모든 CLI 전용 패널 숨김
        ClaudeOptionsPanel.Visibility = Visibility.Collapsed;
        CodexOptionsPanel.Visibility = Visibility.Collapsed;
        GeminiOptionsPanel.Visibility = Visibility.Collapsed;
        AiderOptionsPanel.Visibility = Visibility.Collapsed;

        // 선택된 CLI의 패널만 표시
        switch (cli.Id)
        {
            case "claude":
                ClaudeOptionsPanel.Visibility = Visibility.Visible;
                break;
            case "codex":
                CodexOptionsPanel.Visibility = Visibility.Visible;
                break;
            case "gemini":
                GeminiOptionsPanel.Visibility = Visibility.Visible;
                break;
            case "aider":
                AiderOptionsPanel.Visibility = Visibility.Visible;
                break;
        }

        // 옵션 초기화
        ResetAllOptions();
    }

    /// <summary>
    /// 모든 옵션 초기화
    /// </summary>
    private void ResetAllOptions()
    {
        // Claude 옵션
        PrintCheck.IsChecked = false;
        OutputFormatCombo.SelectedIndex = 0;
        VerboseCheck.IsChecked = false;
        ResumeCheck.IsChecked = false;
        ContinueCheck.IsChecked = false;

        // Codex 옵션
        CodexQuietCheck.IsChecked = false;

        // Gemini 옵션
        GeminiYoloCheck.IsChecked = false;

        // Aider 옵션
        AiderAutoCommitCheck.IsChecked = false;
        AiderNoPrettyCheck.IsChecked = false;
    }

    /// <summary>
    /// 콤보박스 변경 이벤트
    /// </summary>
    private void OnComboChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCommandPreview();
    }

    /// <summary>
    /// 옵션 변경 시 명령어 미리보기 업데이트
    /// </summary>
    private void OnOptionChanged(object sender, RoutedEventArgs e)
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
        if (CommandPreviewTextBox == null || _selectedCLI == null) return;

        // 사용자가 직접 편집 중이면 자동 업데이트 하지 않음
        if (_isManualEdit) return;

        var parts = new List<string>();

        // 기본 명령어
        if (_selectedCLI.Id == "custom")
        {
            var customCmd = CustomCommandTextBox?.Text?.Trim();
            if (!string.IsNullOrEmpty(customCmd))
            {
                parts.Add(customCmd);
            }
            else
            {
                parts.Add("(명령어를 입력하세요)");
            }
        }
        else
        {
            parts.Add(_selectedCLI.Command);
        }

        // 자동 모드
        if (AutoModeCheck?.IsChecked == true && !string.IsNullOrEmpty(_selectedCLI.AutoModeFlag))
        {
            parts.Add(_selectedCLI.AutoModeFlag);
        }

        // CLI별 옵션 추가
        switch (_selectedCLI.Id)
        {
            case "claude":
                AddClaudeOptions(parts);
                break;
            case "codex":
                AddCodexOptions(parts);
                break;
            case "gemini":
                AddGeminiOptions(parts);
                break;
            case "aider":
                AddAiderOptions(parts);
                break;
        }

        // 추가 플래그
        if (!string.IsNullOrWhiteSpace(AdditionalFlagsTextBox?.Text))
        {
            parts.Add(AdditionalFlagsTextBox.Text.Trim());
        }

        GeneratedCommand = string.Join(" ", parts);
        CommandPreviewTextBox.Text = GeneratedCommand;
    }

    /// <summary>
    /// Claude 옵션 추가
    /// </summary>
    private void AddClaudeOptions(List<string> parts)
    {
        if (PrintCheck?.IsChecked == true)
            parts.Add("--print");

        if (OutputFormatCombo?.SelectedItem is ComboBoxItem formatItem)
        {
            var tag = formatItem.Tag?.ToString();
            if (!string.IsNullOrEmpty(tag))
                parts.Add(tag);
        }

        if (VerboseCheck?.IsChecked == true)
            parts.Add("--verbose");

        if (ResumeCheck?.IsChecked == true)
            parts.Add("--resume");

        if (ContinueCheck?.IsChecked == true)
            parts.Add("--continue");
    }

    /// <summary>
    /// Codex 옵션 추가
    /// </summary>
    private void AddCodexOptions(List<string> parts)
    {
        if (CodexQuietCheck?.IsChecked == true)
            parts.Add("--quiet");
    }

    /// <summary>
    /// Gemini 옵션 추가
    /// </summary>
    private void AddGeminiOptions(List<string> parts)
    {
        if (GeminiYoloCheck?.IsChecked == true)
            parts.Add("-y");
    }

    /// <summary>
    /// Aider 옵션 추가
    /// </summary>
    private void AddAiderOptions(List<string> parts)
    {
        if (AiderAutoCommitCheck?.IsChecked == true)
            parts.Add("--auto-commits");

        if (AiderNoPrettyCheck?.IsChecked == true)
            parts.Add("--no-pretty");
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
    /// 확인
    /// </summary>
    private void Run_Click(object sender, RoutedEventArgs e)
    {
        // 최종 명령어를 텍스트박스에서 가져옴 (사용자가 편집했을 수 있으므로)
        GeneratedCommand = CommandPreviewTextBox.Text.Trim();
        SaveSettings();
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// 명령어 직접 편집 시
    /// </summary>
    private void CommandPreviewTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // 프로그래밍 방식 변경 vs 사용자 입력 구분
        if (CommandPreviewTextBox.IsFocused)
        {
            _isManualEdit = true;
            GeneratedCommand = CommandPreviewTextBox.Text;
        }
    }
}
