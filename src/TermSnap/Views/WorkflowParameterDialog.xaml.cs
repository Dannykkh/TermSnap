using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using TermSnap.Models;

namespace TermSnap.Views
{
    /// <summary>
    /// 워크플로우 파라미터 입력 대화상자
    /// </summary>
    public partial class WorkflowParameterDialog : Window
    {
        private readonly CommandSnippet _snippet;
        private readonly List<WorkflowParameter> _parameters;

        /// <summary>
        /// 최종 해석된 명령어
        /// </summary>
        public string ResolvedCommand { get; private set; } = string.Empty;

        public WorkflowParameterDialog(CommandSnippet snippet)
        {
            InitializeComponent();
            _snippet = snippet ?? throw new ArgumentNullException(nameof(snippet));
            _parameters = _snippet.ExtractParameters();

            LoadSnippetInfo();
            SetupParameterBindings();
        }

        private void LoadSnippetInfo()
        {
            SnippetNameText.Text = _snippet.Name;
            SnippetCommandText.Text = _snippet.Command;
            UpdatePreview();
        }

        private void SetupParameterBindings()
        {
            ParametersItemsControl.ItemsSource = _parameters;

            // 각 파라미터 값 변경 시 미리보기 업데이트
            foreach (var param in _parameters)
            {
                // PropertyChanged 이벤트를 위해 INotifyPropertyChanged 구현 필요
                // 여기서는 간단히 TextChanged 이벤트로 처리
            }
        }

        private void UpdatePreview()
        {
            var values = _parameters.ToDictionary(p => p.Name, p => p.Value ?? p.DefaultValue ?? "");
            ResolvedCommand = _snippet.ResolveCommand(values);
            PreviewCommandText.Text = $"$ {ResolvedCommand}";
        }

        private void ParameterValue_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void ExecuteButton_Click(object sender, RoutedEventArgs e)
        {
            // 필수 파라미터 검증
            var emptyParams = _parameters.Where(p => 
                string.IsNullOrWhiteSpace(p.Value) && string.IsNullOrWhiteSpace(p.DefaultValue)).ToList();

            if (emptyParams.Any())
            {
                MessageBox.Show(
                    $"다음 파라미터를 입력해주세요:\n\n{string.Join("\n", emptyParams.Select(p => $"• {p.Name}"))}",
                    "파라미터 누락",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            UpdatePreview();
            DialogResult = true;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    DialogResult = false;
                    Close();
                    break;

                case Key.Enter:
                    if (Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        ExecuteButton_Click(sender, e);
                    }
                    break;
            }
        }
    }
}
