using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using TermSnap.Models;

namespace TermSnap.Views;

/// <summary>
/// 명령어 확인 및 편집 대화상자
/// AI JSON 응답의 추가 정보(경고, 대체 명령어, 신뢰도, 위험도 등)를 표시
/// </summary>
public partial class CommandConfirmDialog : Window
{
    public string EditedCommand { get; private set; }

    /// <summary>
    /// 기본 생성자 (기존 호환성 유지)
    /// </summary>
    public CommandConfirmDialog(string command, string? explanation = null)
        : this(command, explanation, null, null, 1.0, false, false)
    {
    }

    /// <summary>
    /// AI 응답 객체를 사용하는 생성자
    /// </summary>
    public CommandConfirmDialog(AICommandResponse aiResponse)
        : this(
            aiResponse.Command,
            aiResponse.Explanation,
            aiResponse.Warning,
            aiResponse.Alternatives,
            aiResponse.Confidence,
            aiResponse.RequiresSudo,
            aiResponse.IsDangerous,
            aiResponse.Category)
    {
    }

    /// <summary>
    /// 모든 AI 메타데이터를 받는 생성자
    /// </summary>
    public CommandConfirmDialog(
        string command,
        string? explanation,
        string? warning,
        List<string>? alternatives,
        double confidence,
        bool requiresSudo,
        bool isDangerous,
        string? category = null)
    {
        InitializeComponent();

        CommandTextBox.Text = command;
        EditedCommand = command;

        // 설명 설정
        if (!string.IsNullOrWhiteSpace(explanation))
        {
            ExplanationTextBlock.Text = explanation;
        }
        else
        {
            ExplanationTextBlock.Text = "명령어 설명을 가져올 수 없습니다.";
        }

        // 신뢰도 배지
        SetConfidenceBadge(confidence);

        // 경고 메시지
        if (!string.IsNullOrWhiteSpace(warning))
        {
            WarningText.Text = warning;
            WarningPanel.Visibility = Visibility.Visible;
        }

        // 대체 명령어
        if (alternatives != null && alternatives.Count > 0)
        {
            AlternativesList.ItemsSource = alternatives;
            AlternativesPanel.Visibility = Visibility.Visible;
        }

        // sudo 필요 배지
        if (requiresSudo)
        {
            SudoBadge.Visibility = Visibility.Visible;
        }

        // 위험 명령어 배지
        if (isDangerous)
        {
            DangerBadge.Visibility = Visibility.Visible;
        }

        // 카테고리 배지
        if (!string.IsNullOrWhiteSpace(category))
        {
            CategoryText.Text = category;
            CategoryIcon.Kind = GetCategoryIcon(category);
            CategoryBadge.Visibility = Visibility.Visible;
        }

        // 포커스를 명령어 텍스트박스에
        CommandTextBox.Focus();
        CommandTextBox.SelectAll();
    }

    /// <summary>
    /// 신뢰도에 따른 배지 색상 설정
    /// </summary>
    private void SetConfidenceBadge(double confidence)
    {
        ConfidenceText.Text = $"신뢰도 {confidence * 100:0}%";

        string colorHex = confidence switch
        {
            >= 0.9 => "#4CAF50", // 녹색 (높음)
            >= 0.7 => "#FF9800", // 주황색 (중간)
            _ => "#F44336"       // 빨간색 (낮음)
        };

        ConfidenceBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
    }

    /// <summary>
    /// 카테고리에 따른 아이콘 반환
    /// </summary>
    private static PackIconKind GetCategoryIcon(string category)
    {
        return category.ToLower() switch
        {
            "파일" => PackIconKind.FileOutline,
            "네트워크" => PackIconKind.Web,
            "프로세스" => PackIconKind.Memory,
            "시스템" => PackIconKind.Cog,
            "패키지" => PackIconKind.Package,
            _ => PackIconKind.Console
        };
    }

    /// <summary>
    /// 대체 명령어 클릭 시 명령어 교체
    /// </summary>
    private void AlternativeCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Content is string alternativeCommand)
        {
            CommandTextBox.Text = alternativeCommand;
            CommandTextBox.Focus();
            CommandTextBox.SelectAll();
        }
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
