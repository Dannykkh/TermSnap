using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TermSnap.Models;
using TermSnap.Services;
using TermSnap.ViewModels;

namespace TermSnap.Views;

/// <summary>
/// ServerSessionView.xaml에 대한 상호 작용 논리
/// AI 선택 UI (XAML 정의) + 자동 스크롤 + 파일 트리 패널
/// </summary>
public partial class ServerSessionView : UserControl
{
    private readonly AppConfig _config;
    private SftpService? _sftpService;
    private bool _isFileTreeInitialized = false;

    // 추천 기능용 필드
    private DispatcherTimer? _suggestionDebounceTimer;
    private string? _currentSuggestionCommand;
    private CancellationTokenSource? _suggestionCts;

    public ServerSessionView()
    {
        InitializeComponent();

        // 설정 로드
        _config = ConfigService.Load();

        // 추천 디바운스 타이머 설정 (300ms)
        _suggestionDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _suggestionDebounceTimer.Tick += SuggestionDebounceTimer_Tick;

        // Loaded 이벤트에서 초기화
        this.Loaded += ServerSessionView_Loaded;

        // DataContext 변경 시 자동 스크롤 설정
        this.DataContextChanged += OnDataContextChanged;
    }

    private void ServerSessionView_Loaded(object sender, RoutedEventArgs e)
    {
        SetupAutoScroll();
        LoadSavedProfiles();
        // 초기 로드 시에만 UI 상태 복원
        if (!_isFileTreeInitialized)
        {
            RestoreUIState();
        }
    }

    /// <summary>
    /// ViewModel의 UI 상태를 복원 (체크박스만 복원, 실제 표시는 토글 시)
    /// </summary>
    private void RestoreUIState()
    {
        if (DataContext is ServerSessionViewModel vm)
        {
            // 체크박스는 IsFileTreeVisible에 바인딩되어 있으므로 자동으로 복원됨

            // 파일 트리가 이미 초기화되어 있고 표시 상태였다면 Visibility만 복원
            if (vm.IsFileTreeVisible && _isFileTreeInitialized)
            {
                FileTreePanelControl.Visibility = Visibility.Visible;
            }
            else
            {
                FileTreePanelControl.Visibility = Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// 저장된 프로필 목록 로드
    /// </summary>
    private void LoadSavedProfiles()
    {
        // 설정 다시 로드 (다른 곳에서 변경되었을 수 있음)
        var config = ConfigService.Load();
        var profiles = config.ServerProfiles;

        // ItemsSource 강제 갱신
        ProfileList.ItemsSource = null;

        if (profiles.Count == 0)
        {
            NoProfileMessage.Visibility = Visibility.Visible;
            ProfileList.Visibility = Visibility.Collapsed;
        }
        else
        {
            NoProfileMessage.Visibility = Visibility.Collapsed;
            ProfileList.Visibility = Visibility.Visible;
            ProfileList.ItemsSource = profiles.ToList(); // 새 리스트로 복사하여 바인딩
        }
    }

    /// <summary>
    /// 새 서버 추가 버튼 클릭
    /// </summary>
    private void AddNewServer_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProfileEditorDialog();
        dialog.Owner = Window.GetWindow(this);

        if (dialog.ShowDialog() == true && dialog.ResultProfile != null)
        {
            // 프로필 저장
            _config.ServerProfiles.Add(dialog.ResultProfile);
            ConfigService.Save(_config);

            // 목록 갱신
            LoadSavedProfiles();
        }
    }

    /// <summary>
    /// 프로필 편집 버튼 클릭
    /// </summary>
    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is ServerConfig profile)
        {
            var index = _config.ServerProfiles.IndexOf(profile);
            if (index < 0) return;

            var dialog = new ProfileEditorDialog(profile);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true && dialog.ResultProfile != null)
            {
                // 프로필 업데이트
                _config.ServerProfiles[index] = dialog.ResultProfile;
                ConfigService.Save(_config);

                // 목록 갱신
                LoadSavedProfiles();
            }
        }
    }

    /// <summary>
    /// 프로필 연결 버튼 클릭
    /// </summary>
    private async void ConnectProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is ServerConfig profile)
        {
            await ConnectToServerAsync(profile);
        }
    }

    /// <summary>
    /// 설정 버튼 클릭
    /// </summary>
    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_config);
        settingsWindow.Owner = Window.GetWindow(this);

        if (settingsWindow.ShowDialog() == true)
        {
            // 설정 변경 후 프로필 목록 갱신
            LoadSavedProfiles();
        }
    }

    /// <summary>
    /// 서버에 연결
    /// </summary>
    private async System.Threading.Tasks.Task ConnectToServerAsync(ServerConfig profile)
    {
        if (DataContext is ServerSessionViewModel vm)
        {
            try
            {
                await vm.ConnectAsync(profile);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"서버 연결 실패: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// DataContext 변경 시 자동 스크롤 재설정
    /// </summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 이전 ViewModel의 이벤트 해제
        if (e.OldValue is ServerSessionViewModel oldVm)
        {
            oldVm.CommandBlocks.CollectionChanged -= OnCommandBlocksChanged;
            oldVm.Messages.CollectionChanged -= OnMessagesChanged;
            oldVm.CommandDetailRequested -= OnCommandDetailRequested;
            oldVm.Activated -= OnViewModelActivated;
            oldVm.Deactivated -= OnViewModelDeactivated;
        }

        // 새 ViewModel의 이벤트 등록
        if (e.NewValue is ServerSessionViewModel newVm)
        {
            newVm.CommandDetailRequested += OnCommandDetailRequested;
            newVm.Activated += OnViewModelActivated;
            newVm.Deactivated += OnViewModelDeactivated;
        }

        SetupAutoScroll();

        // UI 상태 복원은 하지 않음 (탭 전환 시 성능 문제)
    }

    /// <summary>
    /// ViewModel 활성화 시 파일 워처 활성화 및 UI 상태 복원
    /// </summary>
    private void OnViewModelActivated(object? sender, EventArgs e)
    {
        // UI 상태 복원 (Visibility)
        RestoreUIState();

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
    /// 명령어 상세보기 요청 처리
    /// </summary>
    private void OnCommandDetailRequested(object? sender, FrequentCommand command)
    {
        ShowCommandDetail(command);
    }

    /// <summary>
    /// 자동 스크롤 설정
    /// </summary>
    private void SetupAutoScroll()
    {
        if (DataContext is ServerSessionViewModel vm)
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
            BlockScrollViewer?.ScrollToEnd();
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
                TerminalScrollViewer?.ScrollToEnd();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }



    /// <summary>
    /// 입력창 키 입력 처리 - 클립보드 이미지 붙여넣기 지원
    /// </summary>
    private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+V 감지
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // 클립보드에 이미지가 있는 경우 처리
            if (ClipboardService.HasImage())
            {
                e.Handled = true;
                HandleClipboardImage();
            }
            // 텍스트만 있는 경우는 기본 동작 (e.Handled = false)
        }
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
                MessageBox.Show("클립보드에서 이미지를 가져오는데 실패했습니다.", "오류", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ViewModel에 이미지 경로 전달
            if (DataContext is ServerSessionViewModel vm)
            {
                // 현재 입력에 이미지 경로 추가 (또는 별도 처리)
                var currentInput = vm.UserInput ?? "";
                
                // 이미지 경로를 인라인으로 추가 (사용자가 편집 가능)
                vm.UserInput = string.IsNullOrEmpty(currentInput) 
                    ? $"[이미지: {imagePath}]" 
                    : $"{currentInput} [이미지: {imagePath}]";

                // 사용자에게 알림
                vm.AddMessage($"📷 클립보드 이미지가 저장되었습니다: {imagePath}", MessageType.Info);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"이미지 붙여넣기 중 오류가 발생했습니다: {ex.Message}", "오류", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 설정에서 선택된 AI 제공자 및 모델로 IAIProvider 생성
    /// </summary>
    public IAIProvider? GetSelectedAIProvider()
    {
        // 설정에서 선택한 제공자/모델 사용
        var modelConfig = _config.GetModelConfig(_config.SelectedProvider, _config.SelectedModelId);
        if (modelConfig == null || !modelConfig.IsConfigured)
        {
            return null;
        }

        return AIProviderFactory.CreateProviderFromConfig(modelConfig);
    }

    /// <summary>
    /// 명령어 검색 버튼 클릭
    /// </summary>
    private void SearchCommands_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ServerSessionViewModel vm)
        {
            var managerWindow = new CommandManagerWindow(
                vm.FrequentCommands,
                onSave: cmd => vm.SaveFrequentCommand(cmd),
                onDelete: cmd => vm.DeleteFrequentCommand(cmd));
            managerWindow.Owner = Window.GetWindow(this);

            if (managerWindow.ShowDialog() == true && managerWindow.SelectedCommand != null)
            {
                vm.UserInput = managerWindow.SelectedCommand.Command;
            }
        }
    }

    /// <summary>
    /// 자주 사용하는 명령어 클릭 (단일 클릭 - 명령어 사용)
    /// </summary>
    private void FrequentCommand_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is FrequentCommand command)
        {
            if (DataContext is ServerSessionViewModel vm)
            {
                vm.UserInput = command.Command;
            }
        }
    }

    /// <summary>
    /// 자주 사용하는 명령어 상세보기 (더블 클릭)
    /// </summary>
    public void ShowCommandDetail(FrequentCommand command)
    {
        if (DataContext is ServerSessionViewModel vm)
        {
            var managerWindow = new CommandManagerWindow(
                command,
                vm.FrequentCommands,
                onSave: cmd => vm.SaveFrequentCommand(cmd),
                onDelete: cmd => vm.DeleteFrequentCommand(cmd));
            managerWindow.Owner = Window.GetWindow(this);

            if (managerWindow.ShowDialog() == true && managerWindow.SelectedCommand != null)
            {
                vm.UserInput = managerWindow.SelectedCommand.Command;
            }
        }
    }

    /// <summary>
    /// 자주 사용하는 명령어 편집 버튼 클릭
    /// </summary>
    private void EditFrequentCommand_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // 부모 클릭 이벤트 전파 방지

        if (sender is Button button && button.Tag is FrequentCommand command)
        {
            ShowCommandDetail(command);
        }
    }

    #region 입력 추천 기능

    /// <summary>
    /// 입력창 텍스트 변경 시 추천 검색 시작
    /// </summary>
    private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // 추천 토글이 꺼져 있으면 무시
        if (SuggestionToggle.IsChecked != true)
        {
            HideSuggestion();
            return;
        }

        // 디바운스 타이머 재시작
        _suggestionDebounceTimer?.Stop();
        _suggestionDebounceTimer?.Start();
    }

    /// <summary>
    /// 디바운스 타이머 완료 - 실제 검색 수행
    /// </summary>
    private async void SuggestionDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _suggestionDebounceTimer?.Stop();

        var input = InputTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(input) || input.Length < 2)
        {
            HideSuggestion();
            return;
        }

        // 이전 검색 취소
        _suggestionCts?.Cancel();
        _suggestionCts = new CancellationTokenSource();

        try
        {
            await SearchSuggestionAsync(input, _suggestionCts.Token);
        }
        catch (OperationCanceledException)
        {
            // 취소됨 - 무시
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"추천 검색 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// RAG 서비스를 사용하여 추천 검색
    /// </summary>
    private async Task SearchSuggestionAsync(string input, CancellationToken ct)
    {
        if (DataContext is not ServerSessionViewModel vm) return;

        var ragService = RAGService.Instance;
        var serverProfile = vm.ServerProfile?.ProfileName;

        // RAG 검색 수행
        var result = await ragService.SearchOrGenerateCommand(input, serverProfile);

        ct.ThrowIfCancellationRequested();

        // UI 업데이트
        Dispatcher.Invoke(() =>
        {
            if (result.IsFromCache && result.CachedCommand != null)
            {
                ShowSuggestion(
                    result.SearchMethod ?? "캐시",
                    input,
                    result.CachedCommand,
                    result.Similarity);
            }
            else
            {
                HideSuggestion();
            }
        });
    }

    /// <summary>
    /// 추천 표시
    /// </summary>
    private void ShowSuggestion(string source, string description, string command, double similarity)
    {
        _currentSuggestionCommand = command;

        // 아이콘 및 소스 텍스트 설정
        string sourceText;
        MaterialDesignThemes.Wpf.PackIconKind iconKind;

        if (source.Contains("임베딩") || source.Contains("Embedding"))
        {
            sourceText = $"임베딩 검색 (유사도 {similarity:P0})";
            iconKind = MaterialDesignThemes.Wpf.PackIconKind.VectorCombine;
        }
        else if (source.Contains("FTS") || source.Contains("텍스트"))
        {
            sourceText = "텍스트 검색";
            iconKind = MaterialDesignThemes.Wpf.PackIconKind.TextSearch;
        }
        else
        {
            sourceText = $"캐시 (유사도 {similarity:P0})";
            iconKind = MaterialDesignThemes.Wpf.PackIconKind.DatabaseSearch;
        }

        SuggestionIcon.Kind = iconKind;
        SuggestionSource.Text = sourceText;
        SuggestionDescription.Text = description;
        SuggestionCommand.Text = command;
        SuggestionPopup.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 추천 숨김
    /// </summary>
    private void HideSuggestion()
    {
        _currentSuggestionCommand = null;
        SuggestionPopup.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 추천 항목 클릭 - 명령어 사용
    /// </summary>
    private void SuggestionItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentSuggestionCommand) && DataContext is ServerSessionViewModel vm)
        {
            vm.UserInput = _currentSuggestionCommand;
            HideSuggestion();
            InputTextBox.Focus();
            InputTextBox.CaretIndex = InputTextBox.Text?.Length ?? 0;
        }
    }

    /// <summary>
    /// 추천 토글 변경 시 팝업 숨김
    /// </summary>
    private void SuggestionToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (SuggestionToggle.IsChecked != true)
        {
            HideSuggestion();
        }
    }

    #endregion

    #region 파일 트리 패널

    /// <summary>
    /// 파일 트리 토글 버튼 클릭
    /// </summary>
    private async void FileTreeToggle_Click(object sender, RoutedEventArgs e)
    {
        // IsChecked는 IsFileTreeVisible에 바인딩되어 있으므로 자동 업데이트됨
        if (FileTreeToggle.IsChecked == true)
        {
            // 파일 트리 표시
            await ShowFileTreeAsync();
        }
        else
        {
            // 파일 트리 숨김
            HideFileTree();
        }
    }

    /// <summary>
    /// 파일 트리 표시 및 초기화
    /// </summary>
    private async System.Threading.Tasks.Task ShowFileTreeAsync()
    {
        if (DataContext is not ServerSessionViewModel vm || vm.ServerProfile == null)
        {
            MessageBox.Show("서버에 연결되어 있지 않습니다.", "알림", 
                MessageBoxButton.OK, MessageBoxImage.Information);
            FileTreeToggle.IsChecked = false;
            return;
        }

        if (!vm.IsConnected)
        {
            MessageBox.Show("먼저 서버에 연결하세요.", "알림", 
                MessageBoxButton.OK, MessageBoxImage.Information);
            FileTreeToggle.IsChecked = false;
            return;
        }

        try
        {
            // SFTP 서비스가 없으면 생성
            if (_sftpService == null || !_sftpService.IsConnected)
            {
                _sftpService = new SftpService(vm.ServerProfile);
                await _sftpService.ConnectAsync();
            }

            // 파일 트리 패널 초기화
            if (!_isFileTreeInitialized)
            {
                FileTreePanelControl.CloseRequested += (s, args) =>
                {
                    // IsChecked는 IsFileTreeVisible에 바인딩되어 있으므로 ViewModel만 업데이트
                    if (DataContext is ServerSessionViewModel vmClose)
                    {
                        vmClose.IsFileTreeVisible = false;  // 이렇게 하면 토글 버튼도 자동 업데이트됨
                    }
                    HideFileTree();
                };

                FileTreePanelControl.OpenInTerminalRequested += (s, path) =>
                {
                    if (path != null && DataContext is ServerSessionViewModel sessionVm)
                    {
                        sessionVm.UserInput = $"cd {path}";
                    }
                };

                FileTreePanelControl.FileDoubleClicked += async (s, item) =>
                {
                    // 파일 더블클릭 시 편집기 열기
                    if (_sftpService != null && !item.IsDirectory)
                    {
                        try
                        {
                            var editorWindow = new FileEditorWindow(_sftpService, item.FullPath);
                            editorWindow.Owner = Window.GetWindow(this);
                            editorWindow.Show();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"파일을 열 수 없습니다: {ex.Message}", "오류",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                };

                // 파일 탐색기에서 디렉토리 변경 시 처리
                FileTreePanelControl.DirectoryChanged += async (s, path) =>
                {
                    if (!string.IsNullOrEmpty(path) && DataContext is ServerSessionViewModel sessionVm)
                    {
                        // ViewModel에 경로 저장 (각 탭마다 독립적)
                        sessionVm.FileTreeCurrentPath = path;

                        // 현재 디렉토리와 다를 때만 cd 실행
                        if (sessionVm.CurrentDirectory != path)
                        {
                            sessionVm.UserInput = $"cd {path}";
                            // 자동 실행
                            await sessionVm.ExecuteCurrentInputAsync();
                        }
                    }
                };

                _isFileTreeInitialized = true;
            }

            // SFTP 클라이언트 가져오기
            var sftpClient = _sftpService.GetSftpClient();
            if (sftpClient != null)
            {
                await FileTreePanelControl.InitializeSshAsync(sftpClient, _sftpService);
                FileTreePanelControl.Visibility = Visibility.Visible;

                // ViewModel 상태 업데이트
                if (DataContext is ServerSessionViewModel vmUpdate)
                {
                    vmUpdate.IsFileTreeVisible = true;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일 트리를 열 수 없습니다: {ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
            FileTreeToggle.IsChecked = false;
        }
    }

    /// <summary>
    /// 파일 트리 숨김
    /// </summary>
    private void HideFileTree()
    {
        FileTreePanelControl.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 파일 워처 활성화 (탭 활성화 시)
    /// </summary>
    public void ActivateFileWatcher()
    {
        FileTreePanelControl.EnableFileWatcher();
    }

    /// <summary>
    /// 파일 워처 비활성화 (탭 비활성화 시)
    /// </summary>
    public void DeactivateFileWatcher()
    {
        FileTreePanelControl.DisableFileWatcher();
    }

    #endregion
}
