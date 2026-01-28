using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TermSnap.Models;
using TermSnap.Services;

namespace TermSnap.Views;

/// <summary>
/// Ralph Loop 패널 코드비하인드
/// </summary>
public partial class RalphLoopPanel : UserControl
{
    private RalphLoopConfig _config = new();
    private RalphLoopService? _service;
    private DispatcherTimer? _elapsedTimer;

    /// <summary>
    /// 패널 닫기 요청
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// 프롬프트 전송 요청 (터미널에 입력)
    /// </summary>
    public event Func<string, Task>? SendPromptRequested;

    /// <summary>
    /// 컨텍스트 리셋 요청 (AI CLI 재시작)
    /// </summary>
    public event Func<Task>? ResetContextRequested;

    /// <summary>
    /// 루프 상태 변경
    /// </summary>
    public event Action<RalphLoopState>? StateChanged;

    public RalphLoopPanel()
    {
        InitializeComponent();

        DataContext = _config;

        // 경과 시간 타이머
        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimer.Tick += OnElapsedTimerTick;
    }

    /// <summary>
    /// 작업 디렉토리 설정
    /// </summary>
    public void SetWorkingDirectory(string directory)
    {
        _config.WorkingDirectory = directory;
    }

    /// <summary>
    /// AI 출력 수신 (외부에서 호출)
    /// </summary>
    public void OnOutputReceived(string output)
    {
        _service?.OnOutputReceived(output);
    }

    /// <summary>
    /// 현재 설정 가져오기
    /// </summary>
    public RalphLoopConfig GetConfig() => _config;

    /// <summary>
    /// 실행 중 여부
    /// </summary>
    public bool IsRunning => _config.IsRunning;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_config.IsRunning)
        {
            // 중지
            Stop();
        }
        else
        {
            // 시작
            await StartAsync();
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        Reset();
    }

    private void AiCommandCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AiCommandCombo.SelectedItem is ComboBoxItem item && item.Tag is string command)
        {
            _config.AICommand = command;
        }
    }

    /// <summary>
    /// Ralph Loop 시작
    /// </summary>
    public async Task StartAsync()
    {
        if (string.IsNullOrWhiteSpace(_config.PRD))
        {
            MessageBox.Show(
                "PRD를 입력해주세요.",
                "Ralph Loop",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            // 서비스 생성
            _service?.Dispose();
            _service = new RalphLoopService(_config);

            // 이벤트 연결
            _service.SendPromptRequested += async (prompt) =>
            {
                if (SendPromptRequested != null)
                {
                    await SendPromptRequested.Invoke(prompt);
                }
            };

            _service.ResetContextRequested += async () =>
            {
                if (ResetContextRequested != null)
                {
                    await ResetContextRequested.Invoke();
                }
            };

            _service.StateChanged += (state) =>
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateStatusText(state);
                    StateChanged?.Invoke(state);
                });
            };

            _service.IterationCompleted += (iteration) =>
            {
                Debug.WriteLine($"[RalphLoop] 반복 #{iteration} 완료");
            };

            // 타이머 시작
            _elapsedTimer?.Start();

            // 루프 시작
            await _service.StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ralph Loop 시작 실패: {ex.Message}",
                "오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _elapsedTimer?.Stop();
        }
    }

    /// <summary>
    /// Ralph Loop 중지
    /// </summary>
    public void Stop()
    {
        _service?.Stop();
        _elapsedTimer?.Stop();
    }

    /// <summary>
    /// 설정 리셋
    /// </summary>
    public void Reset()
    {
        Stop();
        _config.Reset();
        _config.PRD = string.Empty;
    }

    /// <summary>
    /// 경과 시간 타이머
    /// </summary>
    private void OnElapsedTimerTick(object? sender, EventArgs e)
    {
        if (_config.StartTime.HasValue)
        {
            var elapsed = DateTime.Now - _config.StartTime.Value;
            ElapsedTimeText.Text = $"경과: {elapsed:hh\\:mm\\:ss}";
        }
    }

    /// <summary>
    /// 상태 텍스트 업데이트
    /// </summary>
    private void UpdateStatusText(RalphLoopState state)
    {
        StatusText.Text = state switch
        {
            RalphLoopState.Running => "실행 중",
            RalphLoopState.WaitingForResponse => "응답 대기",
            RalphLoopState.Paused => "일시 정지",
            RalphLoopState.Completed => "완료",
            RalphLoopState.Error => "오류",
            _ => "대기 중"
        };
    }
}
