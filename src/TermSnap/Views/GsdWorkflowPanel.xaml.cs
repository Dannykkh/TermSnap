using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TermSnap.Models;
using TermSnap.Services;

namespace TermSnap.Views;

/// <summary>
/// GSD Workflow 통합 패널
/// Discuss -> Plan -> Execute (Ralph Loop) -> Verify 워크플로우 관리
/// </summary>
public partial class GsdWorkflowPanel : UserControl
{
    private GsdWorkflowConfig _config = new();
    private RalphLoopService? _executeService;
    private DispatcherTimer? _elapsedTimer;
    private ObservableCollection<GsdTask> _tasks = new();
    private ExecutionMode _currentExecutionMode = ExecutionMode.Interactive;

    // 탭 ID (탭별 싱글톤용)
    private string _tabId = Guid.NewGuid().ToString("N")[..8];

    // 앱 전역 싱글톤에서 탭별 인스턴스 가져오기
    private AgentOrchestrator Orchestrator =>
        AgentServiceManager.Instance.GetOrchestrator(_tabId);

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
    /// 상태 변경 이벤트
    /// </summary>
    public event Action<GsdStep>? StepChanged;

    public GsdWorkflowPanel()
    {
        InitializeComponent();

        DataContext = _config;
        TaskListBox.ItemsSource = _tasks;

        // 경과 시간 타이머
        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimer.Tick += OnElapsedTimerTick;

        // 오케스트레이터 초기화
        InitializeOrchestrator();
    }

    /// <summary>
    /// 에이전트 오케스트레이터 초기화
    /// </summary>
    private void InitializeOrchestrator()
    {
        try
        {
            // 탭별 오케스트레이터는 싱글톤 매니저에서 가져옴 (Orchestrator 프로퍼티 사용)
            // 이벤트 연결
            Orchestrator.ProgressChanged += (progress) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _config.CurrentTask = progress.CurrentTask;
                    StatusText.Text = $"[{progress.CurrentIndex}/{progress.TotalCount}] {progress.Status}";
                });
            };

            Orchestrator.TaskCompleted += (task, response) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (response.Success)
                    {
                        StatusText.Text = $"Task completed: {task.Description.Substring(0, Math.Min(30, task.Description.Length))}...";
                    }
                    else
                    {
                        StatusText.Text = $"Task failed: {response.Error}";
                    }
                });
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GsdWorkflowPanel] Orchestrator init failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 탭 ID (외부에서 설정 가능)
    /// </summary>
    public string TabId
    {
        get => _tabId;
        set => _tabId = value;
    }

    /// <summary>
    /// 패널 종료 시 리소스 해제
    /// </summary>
    public void Cleanup()
    {
        AgentServiceManager.Instance.ReleaseTab(_tabId);
    }

    /// <summary>
    /// 현재 실행 모드
    /// </summary>
    public ExecutionMode CurrentExecutionMode => _currentExecutionMode;

    /// <summary>
    /// 작업 디렉토리 설정
    /// </summary>
    public void SetWorkingDirectory(string directory)
    {
        _config.WorkingDirectory = directory;

        // 기존 GSD 상태 로드
        LoadCurrentState();
    }

    /// <summary>
    /// 현재 GSD 상태 로드
    /// </summary>
    private async void LoadCurrentState()
    {
        if (string.IsNullOrEmpty(_config.WorkingDirectory))
            return;

        if (!GsdWorkflowService.HasPlanningFolder(_config.WorkingDirectory))
        {
            StatusText.Text = LocalizationService.Instance.GetString("Gsd.NotInitialized");
            return;
        }

        // config.json에서 상태 로드
        var gsdConfig = GsdWorkflowService.LoadConfig(_config.WorkingDirectory);
        if (gsdConfig != null)
        {
            _config.CurrentPhase = gsdConfig.CurrentPhase;
            _config.CurrentStep = GsdWorkflowService.StringToStep(gsdConfig.CurrentStep);

            // Phase ComboBox 동기화
            if (gsdConfig.CurrentPhase >= 1 && gsdConfig.CurrentPhase <= 5)
            {
                PhaseCombo.SelectedIndex = gsdConfig.CurrentPhase - 1;
            }

            // Step RadioButton 동기화
            SyncStepRadioButtons(_config.CurrentStep);

            // 현재 단계 문서 로드
            await LoadCurrentStepDocumentAsync();

            StatusText.Text = $"Phase {_config.CurrentPhase} - {_config.CurrentStepText}";
        }
    }

    /// <summary>
    /// Step RadioButton 동기화
    /// </summary>
    private void SyncStepRadioButtons(GsdStep step)
    {
        DiscussRadio.IsChecked = step == GsdStep.Discuss;
        PlanRadio.IsChecked = step == GsdStep.Plan;
        ExecuteRadio.IsChecked = step == GsdStep.Execute;
        VerifyRadio.IsChecked = step == GsdStep.Verify;

        // Tab도 동기화
        StepTabControl.SelectedIndex = (int)step;
    }

    /// <summary>
    /// 현재 단계 문서 로드
    /// </summary>
    private async Task LoadCurrentStepDocumentAsync()
    {
        switch (_config.CurrentStep)
        {
            case GsdStep.Discuss:
                var context = await GsdWorkflowService.ReadPhaseDocumentAsync(
                    _config.WorkingDirectory, _config.CurrentPhase, "CONTEXT");
                ContextTextBox.Text = context ?? GetDefaultContextTemplate();
                break;

            case GsdStep.Plan:
                var plan = await GsdWorkflowService.ReadPhaseDocumentAsync(
                    _config.WorkingDirectory, _config.CurrentPhase, "PLAN");
                PlanTextBox.Text = plan ?? GetDefaultPlanTemplate();
                UpdateTaskList(plan);
                break;

            case GsdStep.Execute:
                // Plan 내용을 PRD로 변환
                var planForPrd = await GsdWorkflowService.ReadPhaseDocumentAsync(
                    _config.WorkingDirectory, _config.CurrentPhase, "PLAN");
                _config.PrdContent = GsdWorkflowService.ConvertPlanToPrd(planForPrd ?? "", _config.CurrentPhase);
                break;

            case GsdStep.Verify:
                var uat = await GsdWorkflowService.ReadPhaseDocumentAsync(
                    _config.WorkingDirectory, _config.CurrentPhase, "UAT");
                UatTextBox.Text = uat ?? GetDefaultUatTemplate();
                break;
        }
    }

    /// <summary>
    /// AI 출력 수신 (외부에서 호출)
    /// </summary>
    public void OnOutputReceived(string output)
    {
        _executeService?.OnOutputReceived(output);
    }

    /// <summary>
    /// 실행 중 여부
    /// </summary>
    public bool IsExecuting => _config.IsExecuting;

    #region 이벤트 핸들러

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PhaseCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 초기화 전에는 무시
        if (StatusText == null) return;

        if (PhaseCombo.SelectedIndex >= 0)
        {
            _config.CurrentPhase = PhaseCombo.SelectedIndex + 1;
            StatusText.Text = $"Phase {_config.CurrentPhase} - {_config.CurrentStepText}";

            // 문서 다시 로드
            _ = LoadCurrentStepDocumentAsync();
        }
    }

    private void StepRadio_Checked(object sender, RoutedEventArgs e)
    {
        // 초기화 전에는 무시
        if (StatusText == null || StepTabControl == null) return;

        if (sender is RadioButton radio && radio.Tag is string stepName)
        {
            var step = GsdWorkflowService.StringToStep(stepName);
            _config.CurrentStep = step;
            StepTabControl.SelectedIndex = (int)step;
            StatusText.Text = $"Phase {_config.CurrentPhase} - {_config.CurrentStepText}";

            // 문서 로드
            _ = LoadCurrentStepDocumentAsync();

            StepChanged?.Invoke(step);
        }
    }

    private void StepTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // RadioButton과 동기화 (탭이 직접 변경된 경우)
    }

    /// <summary>
    /// 실행 모드 변경 핸들러
    /// </summary>
    private void ExecutionModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 초기화 전에는 무시
        if (ModeDescriptionText == null || ExecutionModeCombo == null) return;

        if (ExecutionModeCombo.SelectedItem is ComboBoxItem item && item.Tag is string modeTag)
        {
            _currentExecutionMode = modeTag switch
            {
                "Interactive" => ExecutionMode.Interactive,
                "EcoMode" => ExecutionMode.EcoMode,
                "Pipeline" => ExecutionMode.Pipeline,
                "Swarm" => ExecutionMode.Swarm,
                "Expert" => ExecutionMode.Expert,
                _ => ExecutionMode.Interactive
            };

            // 오케스트레이터에 모드 설정 (탭별 싱글톤)
            Orchestrator.CurrentMode = _currentExecutionMode;

            // 모드 설명 업데이트
            ModeDescriptionText.Text = _currentExecutionMode.GetDescription();

            StatusText.Text = $"Mode: {_currentExecutionMode.ToDisplayString()}";
        }
    }

    #endregion

    #region Discuss 탭

    private async void SaveContext_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_config.WorkingDirectory))
            return;

        // CONTEXT.md가 없으면 생성, 있으면 덮어쓰기
        var success = await GsdWorkflowService.SavePhaseDocumentAsync(
            _config.WorkingDirectory, _config.CurrentPhase, "CONTEXT", ContextTextBox.Text);

        if (success)
        {
            StatusText.Text = LocalizationService.Instance.GetString("Common.Saved");
        }
    }

    private void GoToPlan_Click(object sender, RoutedEventArgs e)
    {
        _config.CurrentStep = GsdStep.Plan;
        SyncStepRadioButtons(GsdStep.Plan);
        _ = LoadCurrentStepDocumentAsync();
        _ = GsdWorkflowService.UpdateConfigAsync(_config.WorkingDirectory, _config.CurrentPhase, GsdStep.Plan);
    }

    private string GetDefaultContextTemplate()
    {
        return $@"# Phase {_config.CurrentPhase} - Context

## Discussion Summary

(요구사항 논의 내용을 여기에 작성하세요)

## Decisions Made

| Topic | Decision | Rationale |
|-------|----------|-----------|
| | | |

## Gray Areas Resolved

-

## Deferred to Later Phases

-

---

*Created: {DateTime.Now:yyyy-MM-dd HH:mm}*
";
    }

    #endregion

    #region Plan 탭

    private async void SavePlan_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_config.WorkingDirectory))
            return;

        var success = await GsdWorkflowService.SavePhaseDocumentAsync(
            _config.WorkingDirectory, _config.CurrentPhase, "PLAN", PlanTextBox.Text);

        if (success)
        {
            StatusText.Text = LocalizationService.Instance.GetString("Common.Saved");
            UpdateTaskList(PlanTextBox.Text);
        }
    }

    private void StartExecute_Click(object sender, RoutedEventArgs e)
    {
        _config.CurrentStep = GsdStep.Execute;
        SyncStepRadioButtons(GsdStep.Execute);

        // Plan 내용을 PRD로 변환
        _config.PrdContent = GsdWorkflowService.ConvertPlanToPrd(PlanTextBox.Text, _config.CurrentPhase);

        _ = GsdWorkflowService.UpdateConfigAsync(_config.WorkingDirectory, _config.CurrentPhase, GsdStep.Execute);
    }

    private void UpdateTaskList(string? planContent)
    {
        _tasks.Clear();

        if (string.IsNullOrEmpty(planContent))
            return;

        var tasks = GsdWorkflowService.ExtractTasksFromPlan(planContent);
        foreach (var task in tasks)
        {
            _tasks.Add(task);
        }
    }

    private string GetDefaultPlanTemplate()
    {
        return $@"# Phase {_config.CurrentPhase} - Plan

## Objective

(이 Phase의 목표를 작성하세요)

## Tasks

- [ ] Task 1
- [ ] Task 2
- [ ] Task 3

## Files to Modify

| File | Action | Description |
|------|--------|-------------|
| | | |

## Dependencies

- (없음)

## Verification

- [ ] 빌드 성공
- [ ] 테스트 통과
- [ ] 기능 동작 확인

## Success Criteria

-

---

*Created: {DateTime.Now:yyyy-MM-dd HH:mm}*
";
    }

    #endregion

    #region Execute 탭 (Ralph Loop)

    private void AiCliCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 초기화 전에는 무시
        if (AiCliCombo == null) return;

        if (AiCliCombo.SelectedItem is ComboBoxItem item && item.Tag is string command)
        {
            _config.AICommand = command;
        }
    }

    private async void ExecuteStartStop_Click(object sender, RoutedEventArgs e)
    {
        if (_config.IsExecuting)
        {
            StopExecute();
        }
        else
        {
            await StartExecuteAsync();
        }
    }

    private async Task StartExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(_config.PrdContent))
        {
            MessageBox.Show(
                LocalizationService.Instance.GetString("GsdWorkflow.NoPrd"),
                LocalizationService.Instance.GetString("GsdWorkflow.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            // RalphLoopConfig로 변환
            var ralphConfig = new RalphLoopConfig
            {
                PRD = _config.PrdContent,
                WorkingDirectory = _config.WorkingDirectory,
                MaxIterations = _config.MaxIterations,
                AICommand = _config.AICommand
            };

            // 서비스 생성
            _executeService?.Dispose();
            _executeService = new RalphLoopService(ralphConfig);

            // 이벤트 연결
            _executeService.SendPromptRequested += async (prompt) =>
            {
                if (SendPromptRequested != null)
                {
                    await SendPromptRequested.Invoke(prompt);
                }
            };

            _executeService.ResetContextRequested += async () =>
            {
                if (ResetContextRequested != null)
                {
                    await ResetContextRequested.Invoke();
                }
            };

            _executeService.StateChanged += (state) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _config.IsExecuting = state == RalphLoopState.Running || state == RalphLoopState.WaitingForResponse;
                    _config.CurrentIteration = ralphConfig.CurrentIteration;
                    _config.CurrentTask = ralphConfig.CurrentTask;

                    ExecuteStatusText.Text = state switch
                    {
                        RalphLoopState.Running => "Executing",
                        RalphLoopState.WaitingForResponse => "Waiting...",
                        RalphLoopState.Completed => "Completed",
                        RalphLoopState.Error => "Error",
                        _ => ""
                    };

                    // UI 상태 업데이트
                    UpdateExecuteUI();

                    // 완료 시 Summary 생성 제안
                    if (state == RalphLoopState.Completed)
                    {
                        StatusText.Text = "Execute completed! Move to Verify step.";
                    }
                });
            };

            // 상태 업데이트
            _config.IsExecuting = true;
            _config.ExecuteStartTime = DateTime.Now;
            _elapsedTimer?.Start();

            // UI 상태 업데이트
            UpdateExecuteUI();

            // 루프 시작
            await _executeService.StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"{LocalizationService.Instance.GetString("GsdWorkflow.ExecuteError")}: {ex.Message}",
                LocalizationService.Instance.GetString("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _config.IsExecuting = false;
            _elapsedTimer?.Stop();
        }
    }

    private void StopExecute()
    {
        _executeService?.Stop();
        _config.IsExecuting = false;
        _elapsedTimer?.Stop();
        UpdateExecuteUI();
        StatusText.Text = "Execute stopped.";
    }

    /// <summary>
    /// Execute UI 상태 업데이트 (버튼 텍스트, 색상, Iteration 표시)
    /// </summary>
    private void UpdateExecuteUI()
    {
        if (_config.IsExecuting)
        {
            ExecuteStartStopButton.Content = "Stop";
            ExecuteStartStopButton.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F44336"));
        }
        else
        {
            ExecuteStartStopButton.Content = "Start";
            ExecuteStartStopButton.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4CAF50"));
        }

        IterationText.Text = $"Iteration: {_config.CurrentIteration}/{_config.MaxIterations}";
    }

    private async void ResetContext_Click(object sender, RoutedEventArgs e)
    {
        if (ResetContextRequested != null)
        {
            await ResetContextRequested.Invoke();
            StatusText.Text = LocalizationService.Instance.GetString("GsdWorkflow.ContextReset");
        }
    }

    private void GoToVerify_Click(object sender, RoutedEventArgs e)
    {
        _config.CurrentStep = GsdStep.Verify;
        SyncStepRadioButtons(GsdStep.Verify);
        _ = LoadCurrentStepDocumentAsync();
        _ = GsdWorkflowService.UpdateConfigAsync(_config.WorkingDirectory, _config.CurrentPhase, GsdStep.Verify);
    }

    #endregion

    #region Verify 탭

    private async void GapsFound_Click(object sender, RoutedEventArgs e)
    {
        // UAT 저장
        await GsdWorkflowService.SavePhaseDocumentAsync(
            _config.WorkingDirectory, _config.CurrentPhase, "UAT", UatTextBox.Text);

        // Execute로 되돌아가기
        _config.CurrentStep = GsdStep.Execute;
        SyncStepRadioButtons(GsdStep.Execute);
        _ = LoadCurrentStepDocumentAsync();
        _ = GsdWorkflowService.UpdateConfigAsync(_config.WorkingDirectory, _config.CurrentPhase, GsdStep.Execute);

        StatusText.Text = LocalizationService.Instance.GetString("GsdWorkflow.BackToExecute");
    }

    private async void Passed_Click(object sender, RoutedEventArgs e)
    {
        // UAT 저장 (Passed 상태로)
        var uatContent = UatTextBox.Text;
        if (!uatContent.Contains("**Status**: passed"))
        {
            uatContent = uatContent.Replace(
                "**Status**: pending | passed | gaps_found | human_needed",
                "**Status**: passed");
        }

        await GsdWorkflowService.SavePhaseDocumentAsync(
            _config.WorkingDirectory, _config.CurrentPhase, "UAT", uatContent);

        StatusText.Text = $"Phase {_config.CurrentPhase} {LocalizationService.Instance.GetString("GsdWorkflow.Passed")}!";
    }

    private async void NextPhase_Click(object sender, RoutedEventArgs e)
    {
        // 다음 Phase로 이동
        _config.CurrentPhase++;
        if (_config.CurrentPhase > 5)
        {
            MessageBox.Show(
                LocalizationService.Instance.GetString("GsdWorkflow.AllPhasesCompleted"),
                LocalizationService.Instance.GetString("GsdWorkflow.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _config.CurrentPhase = 5;
            return;
        }

        _config.CurrentStep = GsdStep.Discuss;
        PhaseCombo.SelectedIndex = _config.CurrentPhase - 1;
        SyncStepRadioButtons(GsdStep.Discuss);

        await GsdWorkflowService.UpdateConfigAsync(_config.WorkingDirectory, _config.CurrentPhase, GsdStep.Discuss);
        await LoadCurrentStepDocumentAsync();

        StatusText.Text = $"Phase {_config.CurrentPhase} - Discuss";
    }

    private string GetDefaultUatTemplate()
    {
        return $@"# Phase {_config.CurrentPhase} - User Acceptance Test

## Verification Status: Pending

## Observable Truths

| Truth | Status | Evidence |
|-------|--------|----------|
| | | |

## Artifacts Check

| Artifact | Exists | Substantial | Connected |
|----------|--------|-------------|-----------|
| | | | |

## Key Links Verified

| From | To | Status |
|------|----|--------|
| | | |

## Gaps Found

(발견된 문제점을 여기에 작성하세요)

## Human Verification Needed

- [ ]

## Result

- **Status**: pending | passed | gaps_found | human_needed
- **Next Action**:

---

*Created: {DateTime.Now:yyyy-MM-dd HH:mm}*
";
    }

    #endregion

    #region 타이머

    private void OnElapsedTimerTick(object? sender, EventArgs e)
    {
        if (_config.ExecuteStartTime.HasValue)
        {
            var elapsed = DateTime.Now - _config.ExecuteStartTime.Value;
            ElapsedTimeText.Text = elapsed.ToString(@"hh\:mm\:ss");
        }
    }

    #endregion
}
