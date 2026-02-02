using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TermSnap.Controls.Terminal;
using TermSnap.Models;
using TermSnap.Services;
using TermSnap.ViewModels;
using MaterialDesignThemes.Wpf;

namespace TermSnap.Views;

/// <summary>
/// 로컬 터미널 세션 뷰 (PowerShell/CMD/WSL/GitBash)
/// 자동 스크롤 지원 + 파일 트리 패널
/// </summary>
public partial class LocalTerminalView : UserControl
{
    private bool _isFileTreeInitialized = false;
    private bool _isWelcomePanelInitialized = false;
    private bool _isTerminalInitialized = false;
    private bool _isSubProcessPanelInitialized = false;
    private SubProcessManager? _subProcessManager;
    private bool _isSubProcessPanelVisible = false;

    // 인터랙티브 모드 초기 로드 완료 감지용 타이머
    private System.Windows.Threading.DispatcherTimer? _interactiveLoadTimer;
    private bool _interactiveResizePending = false;

    // IME 상태 모니터링용 타이머
    private System.Windows.Threading.DispatcherTimer? _imeMonitorTimer;
    private bool _lastImeState = false;

    public LocalTerminalView()
    {
        InitializeComponent();

        // DataContext 변경 시 자동 스크롤 설정
        this.DataContextChanged += OnDataContextChanged;
        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
        this.SizeChanged += OnViewSizeChanged;

        // IME 상태 변경 이벤트 구독 (한영 전환 시 버튼 자동 업데이트)
        InputLanguageManager.Current.InputLanguageChanged += OnInputLanguageChanged;
    }

    /// <summary>
    /// 뷰 크기 변경 시 터미널 컨트롤 강제 갱신
    /// </summary>
    private void OnViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 인터랙티브 모드일 때만 터미널 갱신
        if (DataContext is LocalTerminalViewModel vm && vm.IsInteractiveMode)
        {
            // 크기 변경이 완료된 후 터미널 갱신
            Dispatcher.BeginInvoke(() =>
            {
                TerminalCtrl?.InvalidateVisual();
                TerminalCtrl?.ResizeToFitImmediate();
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        // 파일 트리도 갱신
        if (DataContext is LocalTerminalViewModel { IsFileTreeVisible: true })
        {
            Dispatcher.BeginInvoke(() =>
            {
                // FileTreePanel은 MainWindow에서 관리
            }, System.Windows.Threading.DispatcherPriority.Render);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetupAutoScroll();
        SetupWelcomePanel();
        SetupTerminalControl();
        InitializePanelManager();

        // 초기 로드 시에만 UI 상태 복원 (탭 생성 시)
        // 탭 전환 시에는 복원하지 않음
        if (!_isFileTreeInitialized && !_isFileViewerInitialized)
        {
            RestoreUIState();
        }

        // 한영 버튼 초기 상태 설정 및 모니터링 시작
        UpdateImeButtonText();
        StartImeMonitoring();

        // GSD 상태 체크
        CheckGsdStatusOnDirectoryChange();
    }

    /// <summary>
    /// ViewModel의 UI 상태를 복원
    /// </summary>
    private void RestoreUIState()
    {
        // Visibility는 IsFileTreeVisible/IsFileViewerVisible 바인딩으로 자동 복원됨
        // 추가적인 UI 복원 로직이 필요하면 여기에 추가
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // IME 상태 변경 이벤트 구독 해제
        InputLanguageManager.Current.InputLanguageChanged -= OnInputLanguageChanged;

        // IME 모니터링 타이머 정리
        StopImeMonitoring();

        // 출력 배치 타이머 정리
        if (_outputBatchTimer != null)
        {
            _outputBatchTimer.Stop();
            _outputBatchTimer = null;
        }

        // 터미널 컨트롤 정리
        TerminalCtrl?.Dispose();

        // 서브 프로세스 관리자 정리
        _subProcessManager?.Stop();
        _subProcessManager?.Dispose();
        _subProcessManager = null;
    }

    /// <summary>
    /// VT100 터미널 컨트롤 초기화
    /// </summary>
    private void SetupTerminalControl()
    {
        if (_isTerminalInitialized) return;

        // 터미널 입력 이벤트 연결
        TerminalCtrl.InputReceived += OnTerminalInputReceived;

        // 터미널 크기 변경 이벤트 연결
        TerminalCtrl.TerminalSizeChanged += OnTerminalSizeChanged;

        // Ctrl+Click 링크 클릭 이벤트 연결
        TerminalCtrl.LinkClicked += OnTerminalLinkClicked;

        // 터미널 버퍼 변경 시 스크롤바 업데이트
        TerminalCtrl.Buffer.BufferChanged += OnTerminalBufferChanged;

        // 터미널이 실제로 렌더링된 후 크기 동기화
        TerminalCtrl.SizeChanged += (s, e) =>
        {
            if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    TerminalCtrl.ResizeToFitImmediate();
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        };

        _isTerminalInitialized = true;
    }

    /// <summary>
    /// 터미널 버퍼 변경 시 스크롤바 업데이트
    /// </summary>
    private void OnTerminalBufferChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateTerminalScrollBar();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// 스크롤바 상태 업데이트
    /// </summary>
    private void UpdateTerminalScrollBar()
    {
        if (TerminalCtrl?.Buffer == null) return;

        var buffer = TerminalCtrl.Buffer;
        int scrollbackCount = buffer.ScrollbackCount;
        int scrollOffset = buffer.ScrollOffset;

        // 스크롤백이 없으면 스크롤바 숨김
        if (scrollbackCount <= 0)
        {
            TerminalScrollBar.Visibility = Visibility.Collapsed;
            return;
        }

        TerminalScrollBar.Visibility = Visibility.Visible;
        TerminalScrollBar.Maximum = scrollbackCount;
        TerminalScrollBar.ViewportSize = buffer.Rows;

        // 스크롤바 값은 반전 (위로 스크롤 = 큰 값)
        _isUpdatingScrollBar = true;
        TerminalScrollBar.Value = scrollbackCount - scrollOffset;
        _isUpdatingScrollBar = false;
    }

    private bool _isUpdatingScrollBar = false;

    /// <summary>
    /// 스크롤바 값 변경 시 터미널 스크롤
    /// </summary>
    private void TerminalScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingScrollBar || TerminalCtrl?.Buffer == null) return;

        var buffer = TerminalCtrl.Buffer;
        int scrollbackCount = buffer.ScrollbackCount;

        // 스크롤바 값을 스크롤 오프셋으로 변환 (반전)
        int newOffset = scrollbackCount - (int)e.NewValue;
        newOffset = Math.Max(0, Math.Min(newOffset, scrollbackCount));

        if (buffer.ScrollOffset != newOffset)
        {
            buffer.ScrollOffset = newOffset;
            buffer.MarkAllLinesDirty();
        }
    }

    /// <summary>
    /// 터미널 크기 변경 시 ConPTY에 알림
    /// </summary>
    private void OnTerminalSizeChanged(int columns, int rows)
    {
        if (DataContext is LocalTerminalViewModel vm)
        {
            vm.ResizeTerminal(columns, rows);
        }
    }

    /// <summary>
    /// 터미널 컨트롤에서 입력 수신 시
    /// </summary>
    private async void OnTerminalInputReceived(string input)
    {
        if (DataContext is not LocalTerminalViewModel vm) return;

        if (vm.IsInteractiveMode)
        {
            // 입력 전에 터미널 스타일 리셋 (배경색 아티팩트 방지)
            TerminalCtrl.ResetStyleBeforeInput();

            // 인터랙티브 모드: 직접 프로세스에 전달
            await vm.SendSpecialKeyAsync(input);
        }
        else
        {
            // 일반 모드: InputTextBox에 텍스트 추가 (Ctrl+V 등)
            Dispatcher.Invoke(() =>
            {
                if (InputTextBox != null && InputTextBox.IsVisible)
                {
                    var caretIndex = InputTextBox.CaretIndex;
                    var currentText = InputTextBox.Text ?? "";
                    InputTextBox.Text = currentText.Insert(caretIndex, input);
                    InputTextBox.CaretIndex = caretIndex + input.Length;
                    InputTextBox.Focus();
                }
            });
        }
    }

    /// <summary>
    /// Ctrl+Click 링크 클릭 시
    /// </summary>
    private void OnTerminalLinkClicked(LinkClickedEventArgs args)
    {
        try
        {
            switch (args.LinkType)
            {
                case LinkType.Url:
                    // 기본 브라우저로 URL 열기
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = args.Value,
                        UseShellExecute = true
                    });
                    break;

                case LinkType.FilePath:
                    HandleFilePathClick(args.Value);
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"링크 열기 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 파일 경로 클릭 처리
    /// </summary>
    private void HandleFilePathClick(string path)
    {
        if (DataContext is not LocalTerminalViewModel vm) return;

        // 상대 경로를 절대 경로로 변환
        string fullPath = path;

        if (!Path.IsPathRooted(path))
        {
            // 현재 작업 디렉토리 기준 절대 경로 계산
            string? workingDir = vm.CurrentDirectory;
            if (!string.IsNullOrEmpty(workingDir))
            {
                // ~/로 시작하면 홈 디렉토리로 변환
                if (path.StartsWith("~/"))
                {
                    var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    fullPath = Path.Combine(homeDir, path.Substring(2));
                }
                // ./로 시작하면 현재 디렉토리 기준
                else if (path.StartsWith("./"))
                {
                    fullPath = Path.Combine(workingDir, path.Substring(2));
                }
                else
                {
                    fullPath = Path.Combine(workingDir, path);
                }
            }
        }

        // 경로 정규화
        fullPath = Path.GetFullPath(fullPath);

        // 파일 존재 확인
        if (File.Exists(fullPath))
        {
            // 파일 뷰어 패널에서 열기
            ShowFileInViewer(fullPath);
        }
        else if (Directory.Exists(fullPath))
        {
            // 디렉토리는 파일 탐색기에서 열기
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = fullPath,
                UseShellExecute = true
            });
        }
        else
        {
            // 파일이 없으면 알림
            Debug.WriteLine($"파일을 찾을 수 없음: {fullPath}");
        }
    }

    /// <summary>
    /// 파일 뷰어 패널에서 파일 표시
    /// </summary>
    private async void ShowFileInViewer(string filePath)
    {
        // 기존 OpenFileInViewerAsync 메서드 사용
        await OpenFileInViewerAsync(filePath);
    }

    /// <summary>
    /// 터미널 컨트롤에 출력 쓰기
    /// </summary>
    public void WriteToTerminal(string text)
    {
        Dispatcher.BeginInvoke(() =>
        {
            TerminalCtrl?.Write(text);
        });
    }

    /// <summary>
    /// 웰컴 패널 초기화 및 이벤트 연결
    /// </summary>
    private void SetupWelcomePanel()
    {
        if (_isWelcomePanelInitialized) return;

        // 쉘 선택 시
        WelcomePanelControl.ShellSelected += (s, shell) =>
        {
            if (DataContext is LocalTerminalViewModel vm)
            {
                vm.SetShell(shell);
            }
        };

        // 폴더 선택 시
        WelcomePanelControl.FolderSelected += async (s, path) =>
        {
            if (DataContext is LocalTerminalViewModel vm)
            {
                // 폴더 열기 전에 선택된 쉘 적용
                var selectedShell = WelcomePanelControl.SelectedShell;
                if (selectedShell != null)
                {
                    vm.SetShell(selectedShell);
                }

                await vm.OpenFolderAsync(path);

                // 터미널 크기를 즉시 올바르게 설정 (출력 전에!)
                await Dispatcher.InvokeAsync(() =>
                {
                    TerminalCtrl?.ResizeToFitImmediate();
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                // 파일 트리 자동 표시 (ViewModel만 업데이트하면 토글 버튼도 자동 업데이트됨)
                vm.IsFileTreeVisible = true;
                await ShowFileTreeAsync(path);

                // GSD 상태 체크
                UpdateGsdStatus(path);

                // AI CLI 옵션이 있으면 실행
                var aiOptions = WelcomePanelControl.GetAICLIOptions();
                if (aiOptions != null)
                {
                    aiOptions.WorkingFolder = path;

                    // Claude Code인 경우 장기기억 훅 설정
                    var programName = aiOptions.Command.Split(' ')[0];
                    if (programName.Contains("claude", StringComparison.OrdinalIgnoreCase))
                    {
                        // .claude/settings.local.json 훅 설정
                        if (ClaudeHookService.EnsureMemoryHooks(path))
                        {
                            Debug.WriteLine($"[FolderSelected] Claude 메모리 훅 설정 완료: {path}");
                        }

                        // CLAUDE.md, MEMORY.md 파일 설정
                        if (ClaudeHookService.EnsureMemoryReference(path))
                        {
                            Debug.WriteLine($"[FolderSelected] CLAUDE.md/MEMORY.md 설정 완료: {path}");
                        }
                    }

                    // 터미널이 완전히 준비될 때까지 대기 (PowerShell 초기화 시간 포함)
                    await Task.Delay(2000);
                    var modeText = aiOptions.AutoMode ? "자동 모드" : "일반 모드";
                    vm.AddMessage($"🤖 AI CLI 시작 ({modeText}): {programName}", Models.MessageType.Info);

                    // 프로그램 이름 설정 (경과 시간 표시용)
                    vm.SetAICLIProgramName(programName);

                    // UI 작업을 Dispatcher에서 실행하여 동기화 보장
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            Debug.WriteLine("[FolderSelected] AI CLI 명령어 입력창에 표시");

                            // 1. 입력창에 명령어 표시 (사용자가 볼 수 있도록)
                            vm.UserInput = aiOptions.Command;
                            InputTextBox.Focus();
                            InputTextBox.CaretIndex = InputTextBox.Text.Length;

                            // 2. 잠시 대기 (사용자가 명령어를 확인할 수 있도록)
                            await Task.Delay(1500);

                            Debug.WriteLine("[FolderSelected] AI CLI 명령어 실행 시작");

                            // 3. ExecuteCurrentInputAsync를 호출하여 인터랙티브 모드 감지 로직 실행
                            // (이렇게 하면 IsInteractiveMode가 자동으로 true로 설정됨)
                            await vm.ExecuteCurrentInputAsync();

                            Debug.WriteLine("[FolderSelected] AI CLI 명령어 실행 완료");

                            // 4. 단계별 크기 로직이 자동으로 적절한 크기를 설정함
                            // Claude Code는 터미널 크기를 감지하면 자동으로 웰컴 박스를 그림
                            await Task.Delay(500); // Claude Code 초기화 대기
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[FolderSelected] AI CLI 실행 실패: {ex.Message}");
                            vm.AddMessage($"⚠️ AI CLI 실행 중 오류: {ex.Message}", Models.MessageType.Error);
                        }
                    });
                }
            }
        };

        // 저장소 복제 요청 시
        WelcomePanelControl.CloneRepositoryRequested += async (s, command) =>
        {
            if (DataContext is LocalTerminalViewModel vm)
            {
                // git clone 명령어 실행
                vm.UserInput = command;
                // 연결되어 있지 않으면 연결 후 실행
                if (!vm.IsConnected)
                {
                    await vm.ConnectAsync();
                }
            }
        };

        // 새 프로젝트 생성 시
        WelcomePanelControl.NewProjectRequested += (s, path) =>
        {
            if (DataContext is LocalTerminalViewModel vm)
            {
                vm.AddMessage($"📁 새 프로젝트 폴더가 생성되었습니다: {path}", Models.MessageType.Success);
            }
        };

        // Claude Code 실행 요청
        WelcomePanelControl.ClaudeRunRequested += async (s, options) =>
        {
            try
            {
                if (DataContext is LocalTerminalViewModel vm)
                {
                    Debug.WriteLine($"[ClaudeRunRequested] Command: {options.Command}, Connected: {vm.IsConnected}");

                    // 먼저 터미널 연결
                    if (!vm.IsConnected)
                    {
                        // 선택된 쉘 사용
                        var shell = WelcomePanelControl.SelectedShell
                            ?? Services.ShellDetectionService.Instance.GetDefaultShell();

                        if (shell != null)
                        {
                            vm.SetShell(shell);
                        }

                        // 작업 폴더가 지정되어 있으면 해당 폴더로 연결
                        if (!string.IsNullOrEmpty(options.WorkingFolder) && System.IO.Directory.Exists(options.WorkingFolder))
                        {
                            await vm.OpenFolderAsync(options.WorkingFolder);
                        }
                        else
                        {
                            await vm.ConnectAsync();
                            vm.ShowWelcome = false;
                        }

                        // 터미널이 완전히 준비될 때까지 대기 (PowerShell 초기화 시간 포함)
                        await Task.Delay(2000);
                    }

                    // AI CLI 명령어 실행
                    if (vm.IsConnected)
                    {
                        var programName = options.Command.Split(' ')[0];
                        var modeText = options.AutoMode ? "자동 모드" : "일반 모드";
                        vm.AddMessage($"🤖 AI CLI 시작 ({modeText}): {programName}", Models.MessageType.Info);

                        // 프로그램 이름 설정 (경과 시간 표시용)
                        vm.SetAICLIProgramName(programName);

                        // UI 작업을 Dispatcher에서 실행하여 동기화 보장
                        await Dispatcher.InvokeAsync(async () =>
                        {
                            try
                            {
                                Debug.WriteLine("[ClaudeRunRequested] 명령어 입력창에 표시");

                                // 1. 입력창에 명령어 표시 (사용자가 볼 수 있도록)
                                vm.UserInput = options.Command;
                                InputTextBox.Focus();
                                InputTextBox.CaretIndex = InputTextBox.Text.Length;

                                // 2. 잠시 대기 (사용자가 명령어를 확인할 수 있도록)
                                await Task.Delay(1500);

                                Debug.WriteLine("[ClaudeRunRequested] 명령어 실행 시작");

                                // 3. ExecuteCurrentInputAsync를 호출하여 인터랙티브 모드 감지 로직 실행
                                await vm.ExecuteCurrentInputAsync();

                                Debug.WriteLine("[ClaudeRunRequested] 명령어 실행 완료");

                                // 초기 프롬프트가 있으면 추가 대기 후 전송
                                if (!string.IsNullOrWhiteSpace(options.InitialPrompt))
                                {
                                    Debug.WriteLine($"[ClaudeRunRequested] 초기 프롬프트 대기 중: {options.InitialPrompt}");

                                    // AI CLI 시작 대기 (Claude 로딩 시간 고려)
                                    await Task.Delay(5000);

                                    // 인터랙티브 모드에서 프롬프트 전송
                                    vm.AddMessage($"📝 초기 프롬프트 전송: {options.InitialPrompt}", Models.MessageType.Info);

                                    // 인터랙티브 입력창에 프롬프트 설정하고 전송
                                    await Dispatcher.InvokeAsync(async () =>
                                    {
                                        InteractiveInputTextBox.Text = options.InitialPrompt;
                                        await Task.Delay(500);
                                        await SendInteractiveInputAsync();
                                    });

                                    Debug.WriteLine("[ClaudeRunRequested] 초기 프롬프트 전송 완료");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[ClaudeRunRequested] UI 작업 실패: {ex.Message}");
                                vm.AddMessage($"⚠️ 명령어 실행 중 오류: {ex.Message}", Models.MessageType.Error);
                            }
                        });
                    }
                    else
                    {
                        Debug.WriteLine("[ClaudeRunRequested] 터미널이 연결되지 않음");
                        vm.AddMessage("⚠️ 터미널이 연결되지 않았습니다", Models.MessageType.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ClaudeRunRequested] 예외 발생: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("LocalTerminal.AICLIError"), ex.Message),
                    LocalizationService.Instance.GetString("Common.Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        _isWelcomePanelInitialized = true;
    }

    /// <summary>
    /// DataContext 변경 시 자동 스크롤 재설정
    /// </summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 이전 ViewModel의 이벤트 해제
        if (e.OldValue is LocalTerminalViewModel oldVm)
        {
            oldVm.CommandBlocks.CollectionChanged -= OnCommandBlocksChanged;
            oldVm.Messages.CollectionChanged -= OnMessagesChanged;
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            oldVm.RawOutputReceived -= OnRawOutputReceived;
            oldVm.Activated -= OnViewModelActivated;
            oldVm.Deactivated -= OnViewModelDeactivated;
        }

        // 새 ViewModel의 이벤트 등록
        if (e.NewValue is LocalTerminalViewModel newVm)
        {
            newVm.Activated += OnViewModelActivated;
            newVm.Deactivated += OnViewModelDeactivated;
            newVm.RawOutputReceived += OnRawOutputReceived;

            // CommandBlocks와 Messages 이벤트 등록
            newVm.CommandBlocks.CollectionChanged += OnCommandBlocksChanged;
            newVm.Messages.CollectionChanged += OnMessagesChanged;
            newVm.PropertyChanged += OnViewModelPropertyChanged;

            // 인터랙티브 모드일 때 버퍼 복원 (View가 새로 생성된 경우)
            if (newVm.IsInteractiveMode)
            {
                RestoreInteractiveBuffer(newVm);
            }

            // GSD 상태 체크
            CheckGsdStatusOnDirectoryChange();
        }

        SetupAutoScroll();

        // UI 상태 복원은 하지 않음 (탭 전환 시 성능 문제)
        // 대신 파일 트리/뷰어는 사용자가 명시적으로 토글할 때만 표시
    }

    /// <summary>
    /// ViewModel 활성화 시 파일 워처 활성화 및 UI 상태 복원
    /// </summary>
    private async void OnViewModelActivated(object? sender, EventArgs e)
    {
        Debug.WriteLine("[OnViewModelActivated] 탭 활성화됨");

        if (DataContext is not LocalTerminalViewModel vm) return;

        // UI 상태 복원 (Visibility)
        RestoreUIState();

        // 인터랙티브 모드일 때 터미널 컨트롤 강제 갱신
        // 버퍼 복원은 View가 새로 생성될 때만 (OnDataContextChanged에서 처리)
        // View 캐싱이 작동하면 TerminalControl은 이미 내용을 가지고 있음
        if (vm.IsInteractiveMode)
        {
            Debug.WriteLine("[OnViewModelActivated] 인터랙티브 모드 - 터미널 갱신");
            Dispatcher.BeginInvoke(() =>
            {
                // 화면 갱신만 (버퍼 복원 안 함)
                TerminalCtrl?.InvalidateVisual();
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        // 파일 트리 경로 복원 및 강제 새로고침 (탭마다 독립적)
        if (vm.IsFileTreeVisible && _isFileTreeInitialized)
        {
            Debug.WriteLine($"[OnViewModelActivated] 파일 트리 갱신 중... Path: {vm.FileTreeCurrentPath}");
            try
            {
                // UI 스레드에서 약간의 지연 후 갱신 (렌더링 완료 대기)
                await Dispatcher.InvokeAsync(async () =>
                {
                    // 파일 트리가 초기화되어 있으면 명시적으로 UI 갱신
                    if (!string.IsNullOrEmpty(vm.FileTreeCurrentPath))
                    {
                        // 경로가 저장되어 있으면 해당 경로로 이동
                        // FileTreePanel은 MainWindow에서 관리
                        // await FileTreePanelControl.NavigateToAsync(vm.FileTreeCurrentPath);
                    }
                    else
                    {
                        // 경로가 없으면 현재 표시된 경로를 새로고침
                        // FileTreePanel은 MainWindow에서 관리
                        // await FileTreePanelControl.RefreshAsync();
                    }

                    // 파일 트리 UI 강제 갱신
                    // FileTreePanel은 MainWindow에서 관리
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                Debug.WriteLine("[OnViewModelActivated] 파일 트리 갱신 완료");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OnViewModelActivated] 파일 트리 갱신 실패: {ex.Message}");
            }
        }

        // 블록 UI나 터미널 뷰도 강제 갱신
        Dispatcher.BeginInvoke(() =>
        {
            BlockScrollViewer?.InvalidateVisual();
            TerminalScrollViewer?.InvalidateVisual();
        }, System.Windows.Threading.DispatcherPriority.Render);

        // 파일 워처 활성화
        ActivateFileWatcher();
    }

    /// <summary>
    /// ViewModel 비활성화 시 파일 워처 비활성화
    /// </summary>
    private void OnViewModelDeactivated(object? sender, EventArgs e)
    {
        DeactivateFileWatcher();
    }

    /// <summary>
    /// 자동 스크롤 설정
    /// </summary>
    private void SetupAutoScroll()
    {
        if (DataContext is LocalTerminalViewModel vm)
        {
            // CommandBlocks (Block UI) 변경 감지
            vm.CommandBlocks.CollectionChanged -= OnCommandBlocksChanged;
            vm.CommandBlocks.CollectionChanged += OnCommandBlocksChanged;

            // 기존 블록들의 PropertyChanged 이벤트 등록
            foreach (var block in vm.CommandBlocks)
            {
                block.PropertyChanged -= OnBlockPropertyChanged;
                block.PropertyChanged += OnBlockPropertyChanged;
            }

            // Messages (기존 채팅 UI) 변경 감지
            vm.Messages.CollectionChanged -= OnMessagesChanged;
            vm.Messages.CollectionChanged += OnMessagesChanged;

            // ViewModel PropertyChanged 감지 (인터랙티브 모드 등)
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;

            // 인터랙티브 모드 원시 출력 → 터미널 컨트롤
            vm.RawOutputReceived -= OnRawOutputReceived;
            vm.RawOutputReceived += OnRawOutputReceived;
        }
    }

    // 출력 배치 처리 (UI 쓰레드 부하 감소)
    private readonly System.Text.StringBuilder _outputBuffer = new();
    private System.Windows.Threading.DispatcherTimer? _outputBatchTimer;
    private readonly object _outputLock = new();

    /// <summary>
    /// 인터랙티브 모드에서 원시 출력 수신 시 터미널 컨트롤에 전달 (배치 처리)
    /// </summary>
    private void OnRawOutputReceived(string rawData)
    {
        lock (_outputLock)
        {
            _outputBuffer.Append(rawData);

            // 타이머가 없으면 생성 (16ms = 60fps)
            if (_outputBatchTimer == null)
            {
                _outputBatchTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal)
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                _outputBatchTimer.Tick += OnOutputBatchTimerTick;
            }

            // 타이머 시작 (이미 실행 중이면 무시)
            if (!_outputBatchTimer.IsEnabled)
            {
                _outputBatchTimer.Start();
            }
        }

        // 인터랙티브 모드 로드 완료 감지 (출력이 들어올 때마다 타이머 리셋)
        if (_interactiveResizePending)
        {
            ResetInteractiveLoadTimer();
        }

        // AI Tools 패널 출력 전달은 통합 패널 내부에서 처리
    }

    /// <summary>
    /// 배치 타이머: 버퍼의 모든 출력을 한 번에 처리
    /// </summary>
    private void OnOutputBatchTimerTick(object? sender, EventArgs e)
    {
        string buffered;
        lock (_outputLock)
        {
            // 버퍼가 비어있으면 타이머 중지
            if (_outputBuffer.Length == 0)
            {
                _outputBatchTimer?.Stop();
                return;
            }

            buffered = _outputBuffer.ToString();
            _outputBuffer.Clear();
        }

        // 터미널 컨트롤에 한 번에 전달 (메인 UI 쓰레드)
        TerminalCtrl?.Write(buffered);
    }

    /// <summary>
    /// ViewModel 속성 변경 감지
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (DataContext is not LocalTerminalViewModel vm) return;

        if (e.PropertyName == nameof(LocalTerminalViewModel.IsInteractiveMode))
        {
            if (vm.IsInteractiveMode)
            {
                // 인터랙티브 모드 진입 시 터미널 컨트롤에 포커스
                Dispatcher.BeginInvoke(() =>
                {
                    TerminalCtrl?.Focus();
                }, System.Windows.Threading.DispatcherPriority.Input);

                // 서브 프로세스 관리자 시작
                StartSubProcessManager(vm.ProcessId);

                // 인터랙티브 프로그램 로고 출력 완료 후 터미널 크기 동기화
                // 출력이 멈추면 500ms 후 리사이즈 (타이머 기반)
                _interactiveResizePending = true;
                StartInteractiveLoadTimer();
            }
            else
            {
                // 인터랙티브 모드 종료 시 서브 프로세스 패널 숨김
                _interactiveResizePending = false;
                StopInteractiveLoadTimer();
                HideSubProcessPanel();
                StopSubProcessManager();
            }
        }
        else if (e.PropertyName == nameof(LocalTerminalViewModel.IsConnected))
        {
            if (vm.IsConnected)
            {
                // 세션 연결 후 터미널 크기 즉시 동기화
                Dispatcher.BeginInvoke(() =>
                {
                    TerminalCtrl?.ResizeToFitImmediate();
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }
        else if (e.PropertyName == nameof(LocalTerminalViewModel.CurrentDirectory) ||
                 e.PropertyName == nameof(LocalTerminalViewModel.WorkingFolder))
        {
            // 작업 디렉토리 변경 시 GSD 상태 체크
            CheckGsdStatusOnDirectoryChange();
        }
        else if (e.PropertyName == nameof(LocalTerminalViewModel.AICLIProgramName))
        {
            // AI CLI 프로그램 변경 시 아이콘 업데이트
            UpdateAIModelIcon(vm.AICLIProgramName);
        }
    }

    /// <summary>
    /// AI 모델 아이콘 업데이트 (프로그램 이름에 따라)
    /// </summary>
    private void UpdateAIModelIcon(string? programName)
    {
        // 모든 아이콘 숨기기
        ClaudeIcon.Visibility = Visibility.Collapsed;
        GeminiIcon.Visibility = Visibility.Collapsed;
        OpenAIIcon.Visibility = Visibility.Collapsed;
        AiderIcon.Visibility = Visibility.Collapsed;
        DefaultTerminalIcon.Visibility = Visibility.Collapsed;

        if (string.IsNullOrEmpty(programName))
        {
            DefaultTerminalIcon.Visibility = Visibility.Visible;
            return;
        }

        var lowerName = programName.ToLowerInvariant();

        // 프로그램 이름에 따라 아이콘 표시
        if (lowerName.Contains("claude"))
        {
            ClaudeIcon.Visibility = Visibility.Visible;
        }
        else if (lowerName.Contains("gemini"))
        {
            GeminiIcon.Visibility = Visibility.Visible;
        }
        else if (lowerName.Contains("codex") || lowerName.Contains("openai") || lowerName.Contains("gpt"))
        {
            OpenAIIcon.Visibility = Visibility.Visible;
        }
        else if (lowerName.Contains("aider"))
        {
            AiderIcon.Visibility = Visibility.Visible;
        }
        else
        {
            DefaultTerminalIcon.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// CommandBlocks 변경 시 자동 스크롤
    /// </summary>
    private void OnCommandBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            // 새 블록의 PropertyChanged 이벤트 등록
            foreach (var item in e.NewItems)
            {
                if (item is CommandBlock block)
                {
                    block.PropertyChanged -= OnBlockPropertyChanged;
                    block.PropertyChanged += OnBlockPropertyChanged;
                }
            }

            ScrollToBottom();
        }
    }

    /// <summary>
    /// CommandBlock 속성 변경 시 자동 스크롤 (Output 업데이트 감지)
    /// </summary>
    private void OnBlockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommandBlock.Output) || 
            e.PropertyName == nameof(CommandBlock.Error) ||
            e.PropertyName == nameof(CommandBlock.Status))
        {
            ScrollToBottom();
        }
    }

    /// <summary>
    /// 스크롤을 맨 아래로 이동
    /// </summary>
    private void ScrollToBottom()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (BlockScrollViewer != null && BlockScrollViewer.Items.Count > 0)
            {
                BlockScrollViewer.ScrollIntoView(BlockScrollViewer.Items[BlockScrollViewer.Items.Count - 1]);
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// Messages 변경 시 자동 스크롤
    /// </summary>
    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (TerminalScrollViewer != null && TerminalScrollViewer.Items.Count > 0)
                {
                    TerminalScrollViewer.ScrollIntoView(TerminalScrollViewer.Items[TerminalScrollViewer.Items.Count - 1]);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// 입력창 키 입력 처리 - 히스토리 탐색 및 클립보드 이미지 붙여넣기 지원
    /// </summary>
    private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not LocalTerminalViewModel vm) return;

        // Ctrl+K: CommandPalette 열기
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            ShowCommandPalette();
            return;
        }

        // 한영 전환키 처리 (HangulMode, 우측 Alt)
        if (e.Key == Key.HangulMode || e.Key == Key.HanjaMode ||
            (e.Key == Key.RightAlt && e.SystemKey == Key.None))
        {
            e.Handled = true;
            ToggleIme(InputTextBox);
            return;
        }

        // 기타 IME 관련 키는 기본 동작 허용
        if (e.Key == Key.ImeProcessed || e.Key == Key.JunjaMode ||
            e.Key == Key.KanaMode || e.Key == Key.KanjiMode)
        {
            return;
        }

        // 화살표 위: 이전 히스토리
        if (e.Key == Key.Up)
        {
            var prevCommand = vm.NavigateHistoryUp();
            if (prevCommand != null)
            {
                vm.UserInput = prevCommand;
                InputTextBox.CaretIndex = InputTextBox.Text.Length;
            }
            e.Handled = true;
            return;
        }

        // 화살표 아래: 다음 히스토리
        if (e.Key == Key.Down)
        {
            var nextCommand = vm.NavigateHistoryDown();
            if (nextCommand != null)
            {
                vm.UserInput = nextCommand;
                InputTextBox.CaretIndex = InputTextBox.Text.Length;
            }
            e.Handled = true;
            return;
        }

        // Ctrl+V 감지
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // 텍스트가 있으면 텍스트 우선 (기본 동작)
            // 이미지만 있을 때만 이미지 처리
            if (!Clipboard.ContainsText() && ClipboardService.HasImage())
            {
                e.Handled = true;
                HandleClipboardImage();
            }
            // 텍스트가 있는 경우는 기본 동작 (e.Handled = false)
        }
    }

    /// <summary>
    /// 입력창 텍스트 변경 시 - 슬래시 명령어 감지
    /// </summary>
    private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is not LocalTerminalViewModel vm) return;

        // `/`로 시작하면 CommandPalette 표시
        if (!string.IsNullOrEmpty(vm.UserInput) && vm.UserInput.StartsWith("/"))
        {
            ShowCommandPalette();
        }
    }

    /// <summary>
    /// CommandPalette 표시
    /// </summary>
    private void ShowCommandPalette()
    {
        if (DataContext is not LocalTerminalViewModel vm) return;

        try
        {
            var config = ConfigService.Load();
            var palette = new CommandPalette(config);
            palette.Owner = Window.GetWindow(this);

            if (palette.ShowDialog() == true)
            {
                // 명령어가 선택되었으면 입력창에 설정
                if (!string.IsNullOrEmpty(palette.SelectedCommand))
                {
                    vm.UserInput = palette.SelectedCommand;
                    InputTextBox.Focus();
                    InputTextBox.CaretIndex = InputTextBox.Text.Length;
                }
                // 액션이 선택되었으면 실행
                else if (!string.IsNullOrEmpty(palette.SelectedAction))
                {
                    // 액션 실행 (필요시 구현)
                    System.Diagnostics.Debug.WriteLine($"Action selected: {palette.SelectedAction}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CommandPalette 표시 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 인터랙티브 모드에서 키 입력 처리
    /// </summary>
    private async Task HandleInteractiveKeyAsync(LocalTerminalViewModel vm, KeyEventArgs e)
    {
        string? keyToSend = null;

        // Ctrl 조합 키
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            keyToSend = e.Key switch
            {
                Key.C => "\x03",  // Ctrl+C (ETX) - 프로세스에 전송 (종료는 버튼으로)
                Key.D => "\x04",  // Ctrl+D (EOT)
                Key.Z => "\x1a",  // Ctrl+Z (SUB)
                Key.L => "\x0c",  // Ctrl+L (clear)
                Key.A => "\x01",  // Ctrl+A (home)
                Key.E => "\x05",  // Ctrl+E (end)
                Key.U => "\x15",  // Ctrl+U (kill line)
                Key.K => "\x0b",  // Ctrl+K (kill to end)
                Key.W => "\x17",  // Ctrl+W (delete word)
                _ => null
            };
            // Ctrl+C는 프로세스에 전송만 하고 인터랙티브 모드는 유지
            // 사용자가 "종료" 버튼을 눌러야 인터랙티브 모드 종료
        }
        // 특수 키
        else if (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift)
        {
            keyToSend = e.Key switch
            {
                // 화살표 키 (ANSI escape sequences)
                Key.Up => "\x1b[A",
                Key.Down => "\x1b[B",
                Key.Right => "\x1b[C",
                Key.Left => "\x1b[D",

                // 편집 키
                Key.Enter => "\r",
                Key.Tab => "\t",
                Key.Escape => "\x1b",
                Key.Back => "\x7f",  // DEL (backspace)
                Key.Delete => "\x1b[3~",
                Key.Home => "\x1b[H",
                Key.End => "\x1b[F",
                Key.PageUp => "\x1b[5~",
                Key.PageDown => "\x1b[6~",
                Key.Insert => "\x1b[2~",

                // F 키
                Key.F1 => "\x1bOP",
                Key.F2 => "\x1bOQ",
                Key.F3 => "\x1bOR",
                Key.F4 => "\x1bOS",
                Key.F5 => "\x1b[15~",
                Key.F6 => "\x1b[17~",
                Key.F7 => "\x1b[18~",
                Key.F8 => "\x1b[19~",
                Key.F9 => "\x1b[20~",
                Key.F10 => "\x1b[21~",
                Key.F11 => "\x1b[23~",
                Key.F12 => "\x1b[24~",

                _ => null
            };
        }

        // 일반 문자 입력
        if (keyToSend == null && e.Key != Key.LeftShift && e.Key != Key.RightShift
            && e.Key != Key.LeftCtrl && e.Key != Key.RightCtrl
            && e.Key != Key.LeftAlt && e.Key != Key.RightAlt
            && e.Key != Key.System && e.Key != Key.CapsLock
            && e.Key != Key.NumLock && e.Key != Key.Scroll)
        {
            // 키를 문자로 변환
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var chr = KeyToChar(key, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            if (chr.HasValue)
            {
                keyToSend = chr.Value.ToString();
            }
        }

        // 키 전송
        if (!string.IsNullOrEmpty(keyToSend))
        {
            await vm.SendSpecialKeyAsync(keyToSend);
        }
    }

    /// <summary>
    /// 키를 문자로 변환
    /// </summary>
    private static char? KeyToChar(Key key, bool shift)
    {
        // 숫자 키
        if (key >= Key.D0 && key <= Key.D9)
        {
            if (shift)
            {
                return key switch
                {
                    Key.D1 => '!', Key.D2 => '@', Key.D3 => '#', Key.D4 => '$', Key.D5 => '%',
                    Key.D6 => '^', Key.D7 => '&', Key.D8 => '*', Key.D9 => '(', Key.D0 => ')',
                    _ => null
                };
            }
            return (char)('0' + (key - Key.D0));
        }

        // 넘패드 숫자
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return (char)('0' + (key - Key.NumPad0));
        }

        // 알파벳 키
        if (key >= Key.A && key <= Key.Z)
        {
            var c = (char)('a' + (key - Key.A));
            return shift ? char.ToUpper(c) : c;
        }

        // 특수 문자
        return key switch
        {
            Key.Space => ' ',
            Key.OemMinus => shift ? '_' : '-',
            Key.OemPlus => shift ? '+' : '=',
            Key.OemOpenBrackets => shift ? '{' : '[',
            Key.OemCloseBrackets => shift ? '}' : ']',
            Key.OemPipe => shift ? '|' : '\\',
            Key.OemSemicolon => shift ? ':' : ';',
            Key.OemQuotes => shift ? '"' : '\'',
            Key.OemComma => shift ? '<' : ',',
            Key.OemPeriod => shift ? '>' : '.',
            Key.OemQuestion => shift ? '?' : '/',
            Key.OemTilde => shift ? '~' : '`',
            Key.Multiply => '*',
            Key.Add => '+',
            Key.Subtract => '-',
            Key.Divide => '/',
            Key.Decimal => '.',
            _ => null
        };
    }

    /// <summary>
    /// 클립보드 이미지 처리
    /// </summary>
    private void HandleClipboardImage()
    {
        try
        {
            var imagePath = ClipboardService.SaveClipboardImage();
            if (string.IsNullOrEmpty(imagePath))
            {
                MessageBox.Show(
                    LocalizationService.Instance.GetString("ServerSession.ImagePasteError"),
                    LocalizationService.Instance.GetString("Common.Error"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (DataContext is LocalTerminalViewModel vm)
            {
                // 인터랙티브 모드: 파일 경로만 입력창에 추가
                if (vm.IsInteractiveMode)
                {
                    var currentText = InteractiveInputTextBox.Text ?? "";
                    var caretIndex = InteractiveInputTextBox.CaretIndex;

                    // 현재 커서 위치에 파일 경로 삽입
                    var newText = currentText.Insert(caretIndex, imagePath);
                    InteractiveInputTextBox.Text = newText;
                    InteractiveInputTextBox.CaretIndex = caretIndex + imagePath.Length;
                    InteractiveInputTextBox.Focus();
                }
                else
                {
                    // 일반 모드: 기존 입력에 파일 경로 추가
                    var currentInput = vm.UserInput ?? "";
                    vm.UserInput = string.IsNullOrEmpty(currentInput)
                        ? imagePath
                        : $"{currentInput} {imagePath}";

                    // 입력창에 포커스
                    InputTextBox.Focus();
                    InputTextBox.CaretIndex = InputTextBox.Text.Length;
                }

                // 사용자에게 알림
                vm.AddMessage(
                    string.Format(LocalizationService.Instance.GetString("LocalTerminal.ImageSaved"), imagePath),
                    Models.MessageType.Info);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("LocalTerminal.ImagePasteException"), ex.Message),
                LocalizationService.Instance.GetString("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 출력 영역에서 키 입력 처리 (인터랙티브 모드)
    /// </summary>
    private void OutputArea_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not LocalTerminalViewModel vm) return;

        // 인터랙티브 모드에서만 키 입력 처리
        if (!vm.IsInteractiveMode) return;

        e.Handled = true;
        _ = HandleInteractiveKeyAsync(vm, e);
    }

    /// <summary>
    /// 인터랙티브 모드 종료 버튼 클릭
    /// </summary>
    private void ExitInteractiveMode_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is LocalTerminalViewModel vm)
        {
            _ = vm.ExitInteractiveModeAsync();
        }
    }

    /// <summary>
    /// 인터랙티브 모드 텍스트 입력창 키 처리 (PreviewKeyDown)
    /// </summary>
    private async void InteractiveInputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not LocalTerminalViewModel vm) return;

        // 한영 전환키 처리 (HangulMode, 우측 Alt)
        if (e.Key == Key.HangulMode || e.Key == Key.HanjaMode ||
            (e.Key == Key.RightAlt && e.SystemKey == Key.None))
        {
            e.Handled = true;
            ToggleIme(InteractiveInputTextBox);
            return;
        }

        // 기타 IME 관련 키는 기본 동작 허용
        if (e.Key == Key.ImeProcessed || e.Key == Key.JunjaMode ||
            e.Key == Key.KanaMode || e.Key == Key.KanjiMode)
        {
            return;
        }

        // Ctrl+C: 선택된 텍스트가 없으면 프로세스에 Ctrl+C 전송 (종료 신호)
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (InteractiveInputTextBox.SelectedText.Length == 0)
            {
                // 선택된 텍스트가 없으면 Ctrl+C를 프로세스에 전송
                e.Handled = true;
                await vm.SendSpecialKeyAsync("\x03");
                return;
            }
            // 선택된 텍스트가 있으면 CommandBinding에서 처리됨 (복사)
            return;
        }

        // Ctrl+V: 텍스트 우선, 이미지만 있으면 이미지 처리
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!Clipboard.ContainsText() && ClipboardService.HasImage())
            {
                e.Handled = true;
                HandleClipboardImage();
                return;
            }
            // 텍스트가 있으면 CommandBinding에서 처리됨
            return;
        }

        // Ctrl+X, Ctrl+A: CommandBinding에서 처리됨 (기본 동작)
        if ((e.Key == Key.X || e.Key == Key.A) && Keyboard.Modifiers == ModifierKeys.Control)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            System.Diagnostics.Debug.WriteLine($"[InteractiveInput] Enter key pressed, Modifiers: {Keyboard.Modifiers}");

            // Shift+Enter 또는 Ctrl+Enter = 줄바꿈 수동 삽입
            if (Keyboard.Modifiers == ModifierKeys.Shift || Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true; // 기본 동작 차단
                System.Diagnostics.Debug.WriteLine($"[InteractiveInput] {Keyboard.Modifiers}+Enter - inserting newline manually");

                // 현재 커서 위치에 줄바꿈 삽입
                int caretIndex = InteractiveInputTextBox.CaretIndex;
                InteractiveInputTextBox.Text = InteractiveInputTextBox.Text.Insert(caretIndex, Environment.NewLine);
                InteractiveInputTextBox.CaretIndex = caretIndex + Environment.NewLine.Length;
                return;
            }

            // Enter (수식키 없음) = 전송
            e.Handled = true; // 기본 동작 차단
            System.Diagnostics.Debug.WriteLine($"[InteractiveInput] Plain Enter - calling SendInteractiveInputAsync, InputText Length: {InteractiveInputTextBox.Text?.Length ?? 0}");
            await SendInteractiveInputAsync();
            System.Diagnostics.Debug.WriteLine("[InteractiveInput] SendInteractiveInputAsync completed");
        }
        else if (e.Key == Key.Up)
        {
            // 화살표 키는 프로세스에 전송
            e.Handled = true;
            await vm.SendSpecialKeyAsync("\x1b[A");
        }
        else if (e.Key == Key.Down)
        {
            e.Handled = true;
            await vm.SendSpecialKeyAsync("\x1b[B");
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            await vm.SendSpecialKeyAsync("\x1b");
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            // 기타 Ctrl+조합키는 프로세스에 전송
            string? keyToSend = e.Key switch
            {
                Key.D => "\x04",  // Ctrl+D
                Key.Z => "\x1a",  // Ctrl+Z
                Key.L => "\x0c",  // Ctrl+L
                _ => null
            };
            if (keyToSend != null)
            {
                e.Handled = true;
                await vm.SendSpecialKeyAsync(keyToSend);
            }
        }
    }

    /// <summary>
    /// 인터랙티브 입력 전송 버튼 클릭
    /// </summary>
    private async void SendInteractiveInput_Click(object sender, RoutedEventArgs e)
    {
        await SendInteractiveInputAsync();
    }

    /// <summary>
    /// 인터랙티브 모드에서 텍스트 입력 전송
    /// </summary>
    private async Task SendInteractiveInputAsync()
    {
        if (DataContext is not LocalTerminalViewModel vm) return;

        var text = InteractiveInputTextBox.Text ?? "";

        System.Diagnostics.Debug.WriteLine($"[SendInteractiveInput] Text: '{text}'");

        // 입력창 먼저 비우기 (UX 개선)
        InteractiveInputTextBox.Text = "";

        // 입력 전에 터미널 스타일 리셋 (배경색 아티팩트 방지)
        // 프로그램이 종료될 때 스타일 리셋을 안 보내는 경우 대비
        TerminalCtrl.ResetStyleBeforeInput();

        // 빈 입력이면 엔터만 전송
        if (string.IsNullOrEmpty(text))
        {
            System.Diagnostics.Debug.WriteLine("[SendInteractiveInput] Sending Enter only (CR)");
            await vm.SendSpecialKeyAsync("\r");
        }
        else
        {
            // 줄바꿈은 유지 (\r\n은 그대로)
            System.Diagnostics.Debug.WriteLine($"[SendInteractiveInput] Sending: '{text}\\r' (text + CR)");

            // 텍스트와 CR(\r)를 합쳐서 한 번에 전송
            await vm.SendSpecialKeyAsync(text + "\r");

            // 백그라운드에서 메모리 자동 추출 (UI 블로킹 없이)
            _ = ExtractMemoryFromUserInputAsync(text);
        }

        InteractiveInputTextBox.Focus();
    }

    /// <summary>
    /// 사용자 입력에서 메모리 자동 추출 (백그라운드)
    /// </summary>
    private async Task ExtractMemoryFromUserInputAsync(string userInput)
    {
        try
        {
            // 너무 짧은 입력은 무시
            if (string.IsNullOrWhiteSpace(userInput) || userInput.Length < 5)
                return;

            // 명령어 같은 입력은 무시 (한 단어, 특수문자로 시작)
            if (!userInput.Contains(' ') || userInput.StartsWith("/") || userInput.StartsWith("!"))
                return;

            // 탭별 MemoryService 인스턴스 사용
            var memoryService = _panelManager?.MemoryService;
            if (memoryService == null)
                return;

            var memories = await memoryService.ExtractMemoriesFromConversation(userInput);

            if (memories.Count > 0)
            {
                Debug.WriteLine($"[Memory] 자동 추출됨: {memories.Count}개");
                foreach (var m in memories)
                {
                    Debug.WriteLine($"  - [{m.Type}] {m.Content}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Memory] 자동 추출 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 인터랙티브 모드 진입 시 출력 영역에 포커스
    /// </summary>
    public void FocusOutputArea()
    {
        if (DataContext is LocalTerminalViewModel vm && vm.UseBlockUI)
        {
            BlockScrollViewer.Focus();
        }
        else
        {
            TerminalScrollViewer.Focus();
        }
    }

    #region 파일 트리 패널

    /// <summary>
    /// 파일 트리 토글 버튼 클릭 (MainWindow에서 처리)
    /// </summary>
    private void FileTreeToggle_Click(object sender, RoutedEventArgs e)
    {
        // FileTreePanel은 MainWindow에서 관리하므로 여기서는 아무것도 하지 않음
    }

    /// <summary>
    /// 파일 트리 표시 및 초기화 - MainWindow에서 처리
    /// </summary>
    private async System.Threading.Tasks.Task ShowFileTreeAsync(string? path = null)
    {
        // FileTreePanel은 MainWindow에서 관리
        await System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>
    /// 파일 트리 숨김 - MainWindow에서 처리
    /// </summary>
    private void HideFileTree()
    {
        // FileTreePanel은 MainWindow에서 관리
    }

    /// <summary>
    /// 파일 워처 활성화 (탭 활성화 시) - MainWindow에서 처리
    /// </summary>
    public void ActivateFileWatcher()
    {
        // FileTreePanel은 MainWindow에서 관리
    }

    /// <summary>
    /// 파일 워처 비활성화 (탭 비활성화 시) - MainWindow에서 처리
    /// </summary>
    public void DeactivateFileWatcher()
    {
        // FileTreePanel은 MainWindow에서 관리
    }

    #endregion

    #region 스니펫 패널

    /// <summary>
    /// 스니펫 클릭 - 명령어 입력창에 삽입
    /// </summary>
    private void Snippet_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is CommandSnippet snippet)
        {
            if (DataContext is LocalTerminalViewModel vm)
            {
                // 파라미터가 있으면 파라미터 다이얼로그 표시
                if (snippet.HasParameters)
                {
                    ShowParameterDialog(snippet);
                }
                else
                {
                    // 명령어를 입력창에 삽입
                    vm.UserInput = snippet.Command;
                    vm.UseSnippet(snippet);
                    InputTextBox.Focus();
                    InputTextBox.CaretIndex = InputTextBox.Text.Length;
                }
            }
        }
    }

    /// <summary>
    /// 파라미터 다이얼로그 표시
    /// </summary>
    private void ShowParameterDialog(CommandSnippet snippet)
    {
        // 간단한 InputBox로 파라미터 입력 받기 (추후 개선 가능)
        var parameters = snippet.ExtractParameters();
        var values = new Dictionary<string, string>();

        foreach (var param in parameters)
        {
            var dialog = new MaterialDesignThemes.Wpf.DialogHost();
            var result = Microsoft.VisualBasic.Interaction.InputBox(
                $"{param.Description}\n기본값: {param.DefaultValue}",
                $"파라미터: {param.Name}",
                param.DefaultValue);

            if (string.IsNullOrEmpty(result) && string.IsNullOrEmpty(param.DefaultValue))
            {
                // 취소됨
                return;
            }

            values[param.Name] = string.IsNullOrEmpty(result) ? param.DefaultValue : result;
        }

        if (DataContext is LocalTerminalViewModel vm)
        {
            var resolvedCommand = snippet.ResolveCommand(values);
            vm.UserInput = resolvedCommand;
            vm.UseSnippet(snippet);
            InputTextBox.Focus();
            InputTextBox.CaretIndex = InputTextBox.Text.Length;
        }
    }

    /// <summary>
    /// 스니펫 추가 버튼 클릭
    /// </summary>
    private void AddSnippet_Click(object sender, RoutedEventArgs e)
    {
        ShowSnippetEditDialog(null);
    }

    /// <summary>
    /// 스니펫 편집 버튼 클릭
    /// </summary>
    private void EditSnippet_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is CommandSnippet snippet)
        {
            ShowSnippetEditDialog(snippet);
        }
    }

    /// <summary>
    /// 스니펫 삭제 버튼 클릭
    /// </summary>
    private void DeleteSnippet_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is CommandSnippet snippet)
        {
            var result = MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("LocalTerminal.DeleteSnippetConfirm"), snippet.Name),
                LocalizationService.Instance.GetString("LocalTerminal.DeleteSnippetTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes && DataContext is LocalTerminalViewModel vm)
            {
                vm.RemoveSnippet(snippet);
            }
        }
    }

    /// <summary>
    /// 스니펫 편집 다이얼로그 표시
    /// </summary>
    private void ShowSnippetEditDialog(CommandSnippet? existingSnippet)
    {
        if (DataContext is not LocalTerminalViewModel vm) return;

        var isNew = existingSnippet == null;

        // 기존 카테고리 목록 가져오기
        var existingCategories = vm.LocalSnippets
            .Select(s => s.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToList();

        // 새 다이얼로그 표시
        var dialog = new SnippetEditDialog(existingSnippet, existingCategories)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true && dialog.Snippet != null)
        {
            if (isNew)
            {
                vm.AddSnippet(dialog.Snippet);
            }
            else
            {
                vm.SaveLocalSnippets();
            }
        }
    }

    /// <summary>
    /// 스니펫 패널 닫기 버튼 클릭
    /// </summary>
    private void CloseSnippetPanel_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is LocalTerminalViewModel vm)
        {
            vm.ShowSnippetPanel = false;
        }
    }

    #endregion

    #region 파일 뷰어 패널

    private bool _isFileViewerInitialized = false;
    private bool _isFileViewerOverlay = false;

    /// <summary>
    /// 파일 뷰어 패널 초기화
    /// </summary>
    private void InitializeFileViewer()
    {
        if (_isFileViewerInitialized) return;

        // 닫기 요청 이벤트 처리
        FileViewerPanelControl.CloseRequested += () =>
        {
            HideFileViewer();
            if (DataContext is LocalTerminalViewModel vm)
            {
                vm.IsFileViewerVisible = false;
            }
        };

        _isFileViewerInitialized = true;
    }

    /// <summary>
    /// 파일 뷰어에서 파일 열기
    /// </summary>
    public async Task OpenFileInViewerAsync(string filePath)
    {
        InitializeFileViewer();

        // 인터랙티브 모드 확인
        var isInteractive = DataContext is LocalTerminalViewModel vm && vm.IsInteractiveMode;

        if (isInteractive)
        {
            // 인터랙티브 모드: 오버레이로 표시 (Column 1에 겹침)
            SetFileViewerOverlayMode(true);
        }
        else
        {
            // 일반 모드: 분할 표시 (Column 4)
            SetFileViewerOverlayMode(false);
        }

        FileViewerPanelControl.Visibility = Visibility.Visible;

        // ViewModel 상태 업데이트
        if (DataContext is LocalTerminalViewModel vmState)
        {
            vmState.IsFileViewerVisible = true;
        }

        // 파일 열기
        await FileViewerPanelControl.OpenFileAsync(filePath);
    }

    /// <summary>
    /// 파일 뷰어 오버레이 모드 설정
    /// </summary>
    private void SetFileViewerOverlayMode(bool overlay)
    {
        _isFileViewerOverlay = overlay;

        // 오버레이 모드는 XAML에서 이미 설정됨 (Grid.Column="1", Panel.ZIndex="100")
        // 이 메서드는 향후 비-오버레이 모드 지원 시 사용
    }

    /// <summary>
    /// 파일 뷰어 숨김
    /// </summary>
    private void HideFileViewer()
    {
        // FileViewerPanelControl은 Binding으로 자동 숨김됨

        // 원래 위치로 복원
        if (_isFileViewerOverlay)
        {
            SetFileViewerOverlayMode(false);
        }
    }

    #endregion

    #region 한영 전환

    // Win32 API for keyboard simulation and IME state
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport("imm32.dll")]
    private static extern bool ImmGetOpenStatus(IntPtr hIMC);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    private const byte VK_HANGUL = 0x15;  // 한영 전환 키
    private const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>
    /// 클립보드에 텍스트 설정 (재시도 로직 포함)
    /// </summary>
    private static bool TrySetClipboardText(string text, int maxRetries = 3)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // 클립보드가 다른 프로세스에 의해 잠겨있음, 잠시 대기 후 재시도
                System.Threading.Thread.Sleep(50);
            }
        }
        return false;
    }

    /// <summary>
    /// 클립보드에서 텍스트 가져오기 (재시도 로직 포함)
    /// </summary>
    private static string? TryGetClipboardText(int maxRetries = 3)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    return Clipboard.GetText();
                }
                return null;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // 클립보드가 다른 프로세스에 의해 잠겨있음, 잠시 대기 후 재시도
                System.Threading.Thread.Sleep(50);
            }
        }
        return null;
    }

    /// <summary>
    /// 인터랙티브 입력창 Loaded 시 CommandBindings 설정
    /// </summary>
    private void InteractiveInputTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;

        // 기존 Copy/Paste/Cut CommandBindings 제거 (기본 동작 비활성화)
        textBox.CommandBindings.Clear();

        // Copy 커맨드: 선택된 텍스트가 있으면 복사
        textBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy,
            (s, args) =>
            {
                if (textBox.SelectedText.Length > 0)
                {
                    TrySetClipboardText(textBox.SelectedText);
                    args.Handled = true;
                }
            }));

        // Paste 커맨드: 텍스트 붙여넣기
        textBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste,
            (s, args) =>
            {
                var clipboardText = TryGetClipboardText();
                if (!string.IsNullOrEmpty(clipboardText))
                {
                    var caretIndex = textBox.CaretIndex;
                    var currentText = textBox.Text ?? "";
                    var newText = currentText.Insert(caretIndex, clipboardText);
                    textBox.Text = newText;
                    textBox.CaretIndex = caretIndex + clipboardText.Length;
                }
                args.Handled = true;
            }));

        // Cut 커맨드: 선택된 텍스트 잘라내기
        textBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut,
            (s, args) =>
            {
                if (textBox.SelectedText.Length > 0)
                {
                    if (TrySetClipboardText(textBox.SelectedText))
                    {
                        var selectionStart = textBox.SelectionStart;
                        var selectionLength = textBox.SelectionLength;
                        var currentText = textBox.Text ?? "";
                        var newText = currentText.Remove(selectionStart, selectionLength);
                        textBox.Text = newText;
                        textBox.CaretIndex = selectionStart;
                    }
                    args.Handled = true;
                }
            }));
    }

    /// <summary>
    /// 인터랙티브 입력창 포커스 시 IME 상태 업데이트
    /// </summary>
    private void InteractiveInputTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        StartImeMonitoring();
        UpdateImeButtonText();
    }

    /// <summary>
    /// 인터랙티브 입력창 포커스 해제 시 선택 영역 초기화
    /// </summary>
    private void InteractiveInputTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // 포커스를 잃을 때 선택 영역 초기화 (커서 백그라운드 제거)
        if (sender is TextBox textBox)
        {
            textBox.SelectionStart = textBox.Text.Length;
            textBox.SelectionLength = 0;
        }

        StopImeMonitoring();
    }

    /// <summary>
    /// 인터랙티브 입력창 텍스트 변경 시 IME 상태 업데이트
    /// </summary>
    private void InteractiveInputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateImeButtonText();
    }

    /// <summary>
    /// IME 모니터링 시작 (타이머 기반 - IME 내부 상태는 이벤트로 감지 불가)
    /// </summary>
    private void StartImeMonitoring()
    {
        // 이미 실행 중이면 무시
        if (_imeMonitorTimer != null && _imeMonitorTimer.IsEnabled)
            return;

        _imeMonitorTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _imeMonitorTimer.Tick += (s, e) =>
        {
            bool currentState = GetCurrentImeState();
            if (currentState != _lastImeState)
            {
                _lastImeState = currentState;
                UpdateImeButtonText();
                Debug.WriteLine($"[ImeMonitor] 상태 변경 감지: {(currentState ? "한글" : "영문")}");
            }
        };
        _imeMonitorTimer.Start();
        Debug.WriteLine("[ImeMonitor] 모니터링 시작");
    }

    /// <summary>
    /// 현재 IME 상태 가져오기 (true: 한글, false: 영문)
    /// </summary>
    private bool GetCurrentImeState()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(Window.GetWindow(this)).Handle;
            if (hwnd != IntPtr.Zero)
            {
                IntPtr hIMC = ImmGetContext(hwnd);
                if (hIMC != IntPtr.Zero)
                {
                    bool isKorean = ImmGetOpenStatus(hIMC);
                    ImmReleaseContext(hwnd, hIMC);
                    return isKorean;
                }
            }
        }
        catch { }

        // 폴백: InputLanguageManager 사용
        var language = InputLanguageManager.Current.CurrentInputLanguage;
        return language.Name.StartsWith("ko") || language.TwoLetterISOLanguageName == "ko";
    }

    /// <summary>
    /// IME 모니터링 중지
    /// </summary>
    private void StopImeMonitoring()
    {
        if (_imeMonitorTimer != null)
        {
            _imeMonitorTimer.Stop();
            _imeMonitorTimer = null;
            Debug.WriteLine("[ImeMonitor] 모니터링 중지");
        }
    }

    /// <summary>
    /// 한영 전환 토글 (키보드 입력 시)
    /// </summary>
    private void ToggleIme(System.Windows.Controls.TextBox textBox)
    {
        try
        {
            // 입력창에 포커스
            textBox.Focus();

            // Win32 API로 한영 전환 키(VK_HANGUL) 전송
            keybd_event(VK_HANGUL, 0, 0, UIntPtr.Zero);  // Key Down
            keybd_event(VK_HANGUL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);  // Key Up

            Debug.WriteLine("[ToggleIme] 한영 전환 키 전송");

            // IME 버튼 텍스트 업데이트 (InputLanguageChanged 이벤트로 자동 업데이트됨)
            // 이벤트가 지연될 수 있으므로 한 번 명시적으로 호출
            Task.Run(async () =>
            {
                await Task.Delay(100);
                await Dispatcher.InvokeAsync(() => UpdateImeButtonText());
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ToggleIme] 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// IME 언어 변경 이벤트 핸들러 (시스템에서 한영 전환 시 자동 호출)
    /// </summary>
    private void OnInputLanguageChanged(object sender, InputLanguageEventArgs e)
    {
        Debug.WriteLine($"[OnInputLanguageChanged] 언어 변경 감지: {e.NewLanguage.DisplayName}");

        // UI 스레드에서 버튼 업데이트
        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateImeButtonText();
        }), System.Windows.Threading.DispatcherPriority.Normal);
    }

    /// <summary>
    /// IME 버튼 텍스트 업데이트
    /// </summary>
    private void UpdateImeButtonText()
    {
        try
        {
            if (ImeToggleButton == null)
                return;

            // IMM32 API로 직접 IME 상태 확인
            bool isKorean = false;

            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(Window.GetWindow(this)).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    IntPtr hIMC = ImmGetContext(hwnd);
                    if (hIMC != IntPtr.Zero)
                    {
                        isKorean = ImmGetOpenStatus(hIMC);
                        ImmReleaseContext(hwnd, hIMC);
                    }
                }
            }
            catch
            {
                // IMM32 실패 시 InputLanguageManager로 확인
                var language = InputLanguageManager.Current.CurrentInputLanguage;
                isKorean = language.Name.StartsWith("ko") || language.TwoLetterISOLanguageName == "ko";
            }

            // 버튼 텍스트 업데이트
            ImeToggleButton.Content = isKorean ? "한" : "A";
            ImeToggleButton.ToolTip = isKorean ? "한영 전환 (클릭하여 영문으로)" : "한영 전환 (클릭하여 한글로)";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateImeButtonText] 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 한영 전환 버튼 클릭
    /// </summary>
    private void ImeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 입력창에 포커스
            InteractiveInputTextBox.Focus();

            // Win32 API로 한영 전환 키(VK_HANGUL) 전송
            keybd_event(VK_HANGUL, 0, 0, UIntPtr.Zero);  // Key Down
            keybd_event(VK_HANGUL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);  // Key Up

            Debug.WriteLine("[ImeToggleButton_Click] 한영 전환 키 전송");

            // IME 버튼 텍스트 업데이트 (InputLanguageChanged 이벤트로 자동 업데이트됨)
            // 이벤트가 지연될 수 있으므로 한 번 명시적으로 호출
            Task.Run(async () =>
            {
                await Task.Delay(100);
                await Dispatcher.InvokeAsync(() => UpdateImeButtonText());
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ImeToggleButton_Click] 오류: {ex.Message}");
        }
    }

    #endregion

    #region Claude Code 환영 박스 트리거

    /// <summary>
    /// Claude Code CLI 환영 박스 표시를 위한 터미널 리사이즈 트리거
    /// </summary>
    private async void TriggerTerminalWelcomeBox(LocalTerminalViewModel vm)
    {
        try
        {
            // 현재 터미널 버퍼 크기 가져오기
            int currentCols = TerminalCtrl?.Buffer?.Columns ?? 130;
            int currentRows = TerminalCtrl?.Buffer?.Rows ?? 40;

            Debug.WriteLine($"[TriggerWelcomeBox] 현재 크기: {currentCols}x{currentRows}");

            // 크기를 1칸 늘렸다가 다시 원래대로 (리사이즈 이벤트 트리거)
            // Claude Code CLI는 리사이즈 이벤트를 받으면 화면을 다시 그림
            vm.ResizeTerminal(currentCols, currentRows + 1);
            await Task.Delay(150);
            vm.ResizeTerminal(currentCols, currentRows);

            Debug.WriteLine($"[TriggerWelcomeBox] 리사이즈 완료");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TriggerWelcomeBox] 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 인터랙티브 모드 버퍼 복원 (View가 새로 생성될 때만 호출)
    /// </summary>
    private void RestoreInteractiveBuffer(LocalTerminalViewModel vm)
    {
        try
        {
            // TerminalControl이 이미 초기화되어 있고 내용이 있으면 복원 안 함
            // (View 캐싱으로 재사용되는 경우)
            if (TerminalCtrl?.Buffer != null && TerminalCtrl.Buffer.ScrollbackCount > 0)
            {
                Debug.WriteLine("[RestoreInteractiveBuffer] TerminalControl에 이미 내용 있음 - 복원 건너뜀");
                return;
            }

            var buffer = vm.GetInteractiveBuffer();

            if (string.IsNullOrEmpty(buffer))
            {
                Debug.WriteLine("[RestoreInteractiveBuffer] 복원할 버퍼 없음");
                return;
            }

            Debug.WriteLine($"[RestoreInteractiveBuffer] 버퍼 복원 시작: {buffer.Length}자");

            // TerminalControl에 버퍼 내용 복원
            Dispatcher.BeginInvoke(() =>
            {
                if (TerminalCtrl != null)
                {
                    // 버퍼 내용 출력
                    TerminalCtrl.Write(buffer);

                    Debug.WriteLine("[RestoreInteractiveBuffer] 버퍼 복원 완료");
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RestoreInteractiveBuffer] 오류: {ex.Message}");
        }
    }

    #endregion

    #region 드래그앤드롭 - 파일 경로 입력

    /// <summary>
    /// 드래그 엔터 이벤트
    /// </summary>
    private void InputTextBox_DragEnter(object sender, DragEventArgs e)
    {
        // 파일이 드롭되는 경우만 허용
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    /// <summary>
    /// 드래그 오버 이벤트
    /// </summary>
    private void InputTextBox_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    /// <summary>
    /// 드롭 이벤트 - 파일 경로를 입력창에 추가
    /// </summary>
    private void InputTextBox_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        try
        {
            // 드롭된 파일 목록 가져오기
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0)
                return;

            if (DataContext is not LocalTerminalViewModel vm)
                return;

            // 파일 경로를 공백으로 구분하여 입력창에 추가
            var paths = string.Join(" ", files.Select(f =>
            {
                // 공백이 포함된 경로는 따옴표로 감싸기
                if (f.Contains(' '))
                    return $"\"{f}\"";
                return f;
            }));

            // 기존 입력 뒤에 공백과 함께 추가
            if (!string.IsNullOrEmpty(vm.UserInput))
            {
                vm.UserInput += " " + paths;
            }
            else
            {
                vm.UserInput = paths;
            }

            // 입력창에 포커스 및 커서를 끝으로 이동
            InputTextBox.Focus();
            InputTextBox.CaretIndex = InputTextBox.Text.Length;

            e.Handled = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InputTextBox_Drop] 오류: {ex.Message}");
        }
    }

    #endregion

    #region 드래그앤드롭 - 인터랙티브 터미널

    private void TerminalCtrl_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void TerminalCtrl_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void TerminalCtrl_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        try
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0)
                return;

            if (DataContext is not LocalTerminalViewModel vm)
                return;

            // 공백이 포함된 경로는 따옴표로 감싸기
            var paths = string.Join(" ", files.Select(f =>
            {
                if (f.Contains(' '))
                    return $"\"{f}\"";
                return f;
            }));

            // 인터랙티브 모드인 경우 SendSpecialKeyAsync로 전송
            if (vm.IsInteractiveMode)
            {
                await vm.SendSpecialKeyAsync(paths);
            }

            // 포커스를 터미널로 이동
            if (TerminalCtrl != null)
            {
                TerminalCtrl.Focus();
            }

            e.Handled = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TerminalCtrl_Drop] 오류: {ex.Message}");
        }
    }

    #endregion

    #region AI Tools 패널 관리

    private PanelManager? _panelManager;

    /// <summary>
    /// AI Tools 패널 토글 (Memory, RalphLoop, GSD 버튼 모두 이 패널로)
    /// </summary>
    private void AIToolsToggle_Click(object sender, RoutedEventArgs e)
    {
        _panelManager?.TogglePanel(PanelType.AITools);
    }

    /// <summary>
    /// PanelManager 초기화
    /// </summary>
    private void InitializePanelManager()
    {
        if (_panelManager != null) return;

        _panelManager = new PanelManager(this);
        _panelManager.RegisterPanels(
            fileTreeBorder: null,  // FileTree는 별도 관리
            fileViewerBorder: null,  // FileViewer는 별도 관리
            aiToolsBorder: AIToolsBorder,
            subProcessBorder: SubProcessBorder
        );

        // 명령어 실행 요청 이벤트
        _panelManager.CommandRequested += async (s, command) =>
        {
            await SendPromptToTerminal(command);
        };

        // 작업 디렉토리 설정
        if (DataContext is LocalTerminalViewModel vm && !string.IsNullOrEmpty(vm.CurrentDirectory))
        {
            _panelManager.SetWorkingDirectory(vm.CurrentDirectory);
        }
    }

    /// <summary>
    /// AI Tools 패널 표시
    /// </summary>
    private void ShowAIToolsPanel()
    {
        InitializePanelManager();

        // 패널 열 때마다 현재 작업 디렉토리 업데이트
        if (DataContext is LocalTerminalViewModel vm && !string.IsNullOrEmpty(vm.CurrentDirectory))
        {
            _panelManager?.SetWorkingDirectory(vm.CurrentDirectory);
            System.Diagnostics.Debug.WriteLine($"[AIToolsPanel] 작업 디렉토리 설정: {vm.CurrentDirectory}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[AIToolsPanel] 경고: 작업 디렉토리가 없습니다");
        }

        _panelManager?.ShowPanel(PanelType.AITools);
    }

    /// <summary>
    /// AI Tools 패널 숨김
    /// </summary>
    private void HideAIToolsPanel()
    {
        _panelManager?.HidePanel(PanelType.AITools);
    }

    /// <summary>
    /// 터미널에 프롬프트 전송
    /// </summary>
    private async Task SendPromptToTerminal(string prompt)
    {
        if (DataContext is not LocalTerminalViewModel vm) return;

        try
        {
            await vm.SendRawInputAsync(prompt + "\n");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AITools] 프롬프트 전송 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// AI 컨텍스트 리셋 (AI CLI 재시작)
    /// </summary>
    private async Task ResetAIContext()
    {
        if (DataContext is not LocalTerminalViewModel vm) return;

        try
        {
            await vm.SendCtrlCAsync();
            await Task.Delay(500);
            // AI CLI 명령어는 설정에서 가져와야 함
            await vm.SendRawInputAsync("claude\n");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AITools] 컨텍스트 리셋 실패: {ex.Message}");
        }
    }

    #endregion

    #region 서브 프로세스 관리

    /// <summary>
    /// 서브 프로세스 관리자 시작 (P/Invoke 사용, 백그라운드 실행)
    /// </summary>
    private void StartSubProcessManager(int parentProcessId)
    {
        if (parentProcessId <= 0) return;

        try
        {
            // 기존 관리자 정리
            StopSubProcessManager();

            _subProcessManager = new SubProcessManager(parentProcessId);

            // 패널 초기화
            InitializeSubProcessPanel();

            // 관리자 시작 (백그라운드 스레드에서 실행됨)
            _subProcessManager.Start();

            Debug.WriteLine($"[SubProcessManager] 시작됨, 부모 PID: {parentProcessId}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SubProcessManager] 시작 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 서브 프로세스 관리자 중지
    /// </summary>
    private void StopSubProcessManager()
    {
        if (_subProcessManager != null)
        {
            _subProcessManager.Stop();
            _subProcessManager.Dispose();
            _subProcessManager = null;

            Debug.WriteLine("[SubProcessManager] 중지됨");
        }
    }

    /// <summary>
    /// 서브 프로세스 패널 초기화
    /// </summary>
    private void InitializeSubProcessPanel()
    {
        if (_isSubProcessPanelInitialized || _subProcessManager == null) return;

        SubProcessPanelControl.SetManager(_subProcessManager);

        // 패널 닫기 요청
        SubProcessPanelControl.CloseRequested += (s, e) =>
        {
            HideSubProcessPanel();
        };

        // 로그 보기 요청
        SubProcessPanelControl.ViewLogRequested += (s, info) =>
        {
            ShowSubProcessLog(info);
        };

        _isSubProcessPanelInitialized = true;
    }

    /// <summary>
    /// 서브 프로세스 토글 버튼 클릭
    /// </summary>
    private void SubProcessToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isSubProcessPanelVisible)
        {
            HideSubProcessPanel();
        }
        else
        {
            ShowSubProcessPanel();
        }
    }

    /// <summary>
    /// 서브 프로세스 패널 표시
    /// </summary>
    private void ShowSubProcessPanel()
    {
        if (_subProcessManager == null) return;

        InitializeSubProcessPanel();
        SubProcessBorder.Visibility = Visibility.Visible;
        _isSubProcessPanelVisible = true;
    }

    /// <summary>
    /// 서브 프로세스 패널 숨김
    /// </summary>
    private void HideSubProcessPanel()
    {
        SubProcessBorder.Visibility = Visibility.Collapsed;
        _isSubProcessPanelVisible = false;
    }

    /// <summary>
    /// 서브 프로세스 로그 보기
    /// </summary>
    private void ShowSubProcessLog(SubProcessInfo info)
    {
        try
        {
            var log = info.OutputBuffer.ToString();

            if (string.IsNullOrWhiteSpace(log))
            {
                log = LocalizationService.Instance.GetString("SubProcess.NoLog");
            }

            // 간단한 로그 다이얼로그 표시
            var dialog = new Window
            {
                Title = string.Format(LocalizationService.Instance.GetString("SubProcess.LogTitle"), info.ProcessName),
                Width = 700,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = System.Windows.Media.Brushes.Black
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 로그 텍스트
            var textBox = new TextBox
            {
                Text = log,
                IsReadOnly = true,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 12,
                Background = System.Windows.Media.Brushes.Black,
                Foreground = System.Windows.Media.Brushes.LightGray,
                BorderThickness = new Thickness(0),
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(10)
            };
            Grid.SetRow(textBox, 0);
            grid.Children.Add(textBox);

            // 닫기 버튼
            var closeButton = new Button
            {
                Content = LocalizationService.Instance.GetString("Common.Close"),
                Width = 100,
                Height = 30,
                Margin = new Thickness(0, 10, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            closeButton.Click += (s, e) => dialog.Close();
            Grid.SetRow(closeButton, 1);
            grid.Children.Add(closeButton);

            dialog.Content = grid;
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShowSubProcessLog] 오류: {ex.Message}");
        }
    }

    #endregion

    #region GSD 워크플로우

    /// <summary>
    /// GSD 초기화 버튼 클릭
    /// </summary>
    private async void GsdInit_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LocalTerminalViewModel vm)
            return;

        var workingDir = vm.WorkingFolder ?? vm.CurrentDirectory;
        if (string.IsNullOrEmpty(workingDir))
        {
            MessageBox.Show(
                "먼저 작업 폴더를 선택해주세요.",
                "GSD 초기화",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // 이미 .planning 폴더가 있는지 확인
        if (GsdWorkflowService.HasPlanningFolder(workingDir))
        {
            MessageBox.Show(
                LocalizationService.Instance.GetString("Gsd.InitDialog.AlreadyExists"),
                LocalizationService.Instance.GetString("Gsd.InitDialog.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            UpdateGsdStatus(workingDir);
            return;
        }

        // 프로젝트 이름 입력 받기
        var projectName = Path.GetFileName(workingDir);
        var dialog = new TextInputDialog(
            LocalizationService.Instance.GetString("Gsd.InitDialog.Title"),
            LocalizationService.Instance.GetString("Gsd.InitDialog.ProjectName"),
            projectName);

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
        {
            var success = await GsdWorkflowService.InitializeAsync(workingDir, dialog.InputText);

            if (success)
            {
                vm.AddMessage(LocalizationService.Instance.GetString("Gsd.InitDialog.Success"), MessageType.Success);
                UpdateGsdStatus(workingDir);
            }
            else
            {
                vm.AddMessage("GSD 초기화 실패", MessageType.Error);
            }
        }
    }

    /// <summary>
    /// Claude + GSD 실행 버튼 클릭
    /// </summary>
    private async void RunClaudeGsd_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LocalTerminalViewModel vm)
            return;

        var workingDir = vm.WorkingFolder ?? vm.CurrentDirectory;
        if (string.IsNullOrEmpty(workingDir))
        {
            MessageBox.Show(
                "먼저 작업 폴더를 선택해주세요.",
                "Claude 실행",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // 세션이 연결되어 있는지 확인
        if (!vm.IsConnected)
        {
            MessageBox.Show(
                "터미널 세션이 연결되어 있지 않습니다.\n먼저 폴더를 선택하여 세션을 시작해주세요.",
                "Claude 실행",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // .planning 폴더가 없으면 자동 생성
        if (!GsdWorkflowService.HasPlanningFolder(workingDir))
        {
            var projectName = Path.GetFileName(workingDir);
            var result = MessageBox.Show(
                $".planning/ 폴더가 없습니다.\n'{projectName}' 프로젝트로 GSD를 초기화할까요?",
                "GSD 초기화",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await GsdWorkflowService.InitializeAsync(workingDir, projectName ?? "project");
                vm.AddMessage(".planning/ 폴더가 생성되었습니다.", MessageType.Success);
                UpdateGsdStatus(workingDir);
            }
        }

        // Claude 실행
        vm.UserInput = "claude";
        await vm.ExecuteCurrentInputAsync();
    }

    /// <summary>
    /// .planning 폴더 열기 버튼 클릭
    /// </summary>
    private void OpenPlanning_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LocalTerminalViewModel vm)
            return;

        var workingDir = vm.WorkingFolder ?? vm.CurrentDirectory;
        if (string.IsNullOrEmpty(workingDir))
            return;

        var planningPath = Path.Combine(workingDir, ".planning");
        if (Directory.Exists(planningPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = planningPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OpenPlanning] 오류: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// GSD 상태 업데이트
    /// </summary>
    private void UpdateGsdStatus(string workingDir)
    {
        if (string.IsNullOrEmpty(workingDir))
        {
            GsdStatusBorder.Visibility = Visibility.Collapsed;
            OpenPlanningButton.Visibility = Visibility.Collapsed;
            return;
        }

        var hasPlanning = GsdWorkflowService.HasPlanningFolder(workingDir);
        GsdStatusBorder.Visibility = hasPlanning ? Visibility.Visible : Visibility.Collapsed;
        OpenPlanningButton.Visibility = hasPlanning ? Visibility.Visible : Visibility.Collapsed;

        if (hasPlanning)
        {
            var config = GsdWorkflowService.LoadConfig(workingDir);
            if (config != null)
            {
                GsdStatusText.Text = $"GSD: Phase {config.CurrentPhase}";
            }
            else
            {
                GsdStatusText.Text = "GSD";
            }
        }
    }

    /// <summary>
    /// 작업 폴더 변경 시 GSD 상태 체크
    /// </summary>
    private void CheckGsdStatusOnDirectoryChange()
    {
        if (DataContext is LocalTerminalViewModel vm)
        {
            var workingDir = vm.WorkingFolder ?? vm.CurrentDirectory;
            UpdateGsdStatus(workingDir);
        }
    }

    #endregion

    #region 인터랙티브 모드 로드 완료 감지

    /// <summary>
    /// 인터랙티브 모드 로드 타이머 시작
    /// </summary>
    private void StartInteractiveLoadTimer()
    {
        if (_interactiveLoadTimer == null)
        {
            _interactiveLoadTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _interactiveLoadTimer.Tick += OnInteractiveLoadTimerTick;
        }

        _interactiveLoadTimer.Stop();
        _interactiveLoadTimer.Start();
    }

    /// <summary>
    /// 인터랙티브 모드 로드 타이머 정지
    /// </summary>
    private void StopInteractiveLoadTimer()
    {
        _interactiveLoadTimer?.Stop();
    }

    /// <summary>
    /// 인터랙티브 모드 로드 타이머 리셋 (출력이 들어올 때마다 호출)
    /// </summary>
    private void ResetInteractiveLoadTimer()
    {
        Dispatcher.BeginInvoke(() =>
        {
            _interactiveLoadTimer?.Stop();
            _interactiveLoadTimer?.Start();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// 인터랙티브 모드 로드 완료 (출력이 500ms 동안 멈추면 리사이즈)
    /// </summary>
    private void OnInteractiveLoadTimerTick(object? sender, EventArgs e)
    {
        _interactiveLoadTimer?.Stop();

        if (_interactiveResizePending)
        {
            _interactiveResizePending = false;
            Debug.WriteLine("[InteractiveLoad] 로드 완료 감지, 터미널 리사이즈 실행");
            TerminalCtrl?.ResizeToFitImmediate();
        }
    }

    #endregion
}
