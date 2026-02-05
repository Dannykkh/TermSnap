using System;
using System.Windows;
using System.Windows.Controls;
using TermSnap.Models;

namespace TermSnap.Views;

/// <summary>
/// 새 기억 추가 다이얼로그
/// </summary>
public partial class AddMemoryDialog : Window
{
    /// <summary>
    /// 입력된 내용
    /// </summary>
    public string MemoryContent { get; private set; } = string.Empty;

    /// <summary>
    /// 선택된 타입
    /// </summary>
    public MemoryType SelectedType { get; private set; } = MemoryType.Architecture;

    /// <summary>
    /// 중요도 (0.0 ~ 1.0)
    /// </summary>
    public double Importance { get; private set; } = 0.5;

    public AddMemoryDialog()
    {
        InitializeComponent();
    }

    private void ImportanceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ImportanceText != null)
        {
            ImportanceText.Text = $"{(int)ImportanceSlider.Value}%";
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var content = ContentBox.Text?.Trim();

        if (string.IsNullOrEmpty(content))
        {
            MessageBox.Show(
                "내용을 입력해주세요.",
                "입력 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ContentBox.Focus();
            return;
        }

        MemoryContent = content;
        Importance = ImportanceSlider.Value / 100.0;

        if (TypeCombo.SelectedItem is ComboBoxItem item && item.Tag is string typeStr)
        {
            if (Enum.TryParse<MemoryType>(typeStr, out var type))
            {
                SelectedType = type;
            }
        }

        DialogResult = true;
        Close();
    }
}
