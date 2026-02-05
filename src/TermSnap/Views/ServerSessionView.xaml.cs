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
    private bool _isMemoryPanelInitialized = false;
    private bool _isMemoryPanelVisible = false;

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
                // FileTreePanelControl (MainWindow에서 관리).Visibility = Visibility.Visible;
            }
            else
            {
                // FileTreePanelControl (MainWindow에서 관리).Visibility = Visibility.Collapsed;
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
            // 최신 config 다시 로드
            var currentConfig = ConfigService.Load();

            // 프로필 저장
            currentConfig.ServerProfiles.Add(dialog.ResultProfile);
            ConfigService.Save(currentConfig);

            // 목록 갱신
            LoadSavedProfiles();
        }
    }

    /// <summary>
    /// 프로필 편집 버튼 클릭
    /// </summary>
    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("EditProfile_Click 호출됨");

            if (sender is not Button button)
            {
                System.Diagnostics.Debug.WriteLine("sender가 Button이 아님");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"Button DataContext: {button.DataContext?.GetType().Name}");

            if (button.DataContext is not ServerConfig profile)
            {
                System.Diagnostics.Debug.WriteLine("DataContext가 ServerConfig가 아님");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"프로필: {profile.ProfileName}");

            // 최신 config 다시 로드
            var currentConfig = ConfigService.Load();

            var index = currentConfig.ServerProfiles.FindIndex(p => p.ProfileName == profile.ProfileName);
            if (index < 0)
            {
                System.Diagnostics.Debug.WriteLine($"프로필을 찾을 수 없음: {profile.ProfileName}");
                MessageBox.Show($"프로필을 찾을 수 없습니다: {profile.ProfileName}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            System.Diagnostics.Debug.WriteLine($"프로필 인덱스: {index}, 대화상자 열기");

            var dialog = new ProfileEditorDialog(currentConfig.ServerProfiles[index]);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true && dialog.ResultProfile != null)
            {
                // 프로필 업데이트
                currentConfig.ServerProfiles[index] = dialog.ResultProfile;
                ConfigService.Save(currentConfig);

                // 목록 갱신
                LoadSavedProfiles();

                System.Diagnostics.Debug.WriteLine("프로필 업데이트 완료");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EditProfile_Click 예외: {ex.Message}");
            MessageBox.Show($"프로필 편집 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("ServerSession.ConnectionFailed"), ex.Message),
                    LocalizationService.Instance.GetString("Common.Error"),
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

        // 스크롤 위치 복원
        RestoreScrollPosition();

        // 파일 워처 활성화
        ActivateFileWatcher();

        // 입력창에 포커스 설정 (탭 전환 후 바로 입력 가능하도록)
        Dispatcher.BeginInvoke(() =>
        {
            if (InputTextBox != null && InputTextBox.IsEnabled)
            {
                InputTextBox.Focus();
                System.Diagnostics.Debug.WriteLine("[ServerSessionView] InputTextBox 포커스 설정됨");
            }
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// ViewModel 비활성화 시 파일 워처 비활성화 및 스크롤 위치 저장
    /// </summary>
    private void OnViewModelDeactivated(object? sender, EventArgs e)
    {
        // 스크롤 위치 저장
        SaveScrollPosition();

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
    /// 스크롤 위치 저장 (탭 전환 시)
    /// </summary>
    private void SaveScrollPosition()
    {
        if (DataContext is ServerSessionViewModel vm)
        {
            if (BlockScrollViewer != null)
            {
                vm.SavedScrollVerticalOffset = BlockScrollViewer.VerticalOffset;
                System.Diagnostics.Debug.WriteLine($"[ServerSessionView] Block 스크롤 위치 저장: {vm.SavedScrollVerticalOffset}");
            }

            if (TerminalScrollViewer != null)
            {
                vm.SavedTerminalScrollVerticalOffset = TerminalScrollViewer.VerticalOffset;
                System.Diagnostics.Debug.WriteLine($"[ServerSessionView] Terminal 스크롤 위치 저장: {vm.SavedTerminalScrollVerticalOffset}");
            }
        }
    }

    /// <summary>
    /// 스크롤 위치 복원 (탭 전환 시)
    /// </summary>
    private void RestoreScrollPosition()
    {
        if (DataContext is ServerSessionViewModel vm)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (BlockScrollViewer != null && vm.SavedScrollVerticalOffset > 0)
                {
                    BlockScrollViewer.ScrollToVerticalOffset(vm.SavedScrollVerticalOffset);
                    System.Diagnostics.Debug.WriteLine($"[ServerSessionView] Block 스크롤 위치 복원: {vm.SavedScrollVerticalOffset}");
                }

                if (TerminalScrollViewer != null && vm.SavedTerminalScrollVerticalOffset > 0)
                {
                    TerminalScrollViewer.ScrollToVerticalOffset(vm.SavedTerminalScrollVerticalOffset);
                    System.Diagnostics.Debug.WriteLine($"[ServerSessionView] Terminal 스크롤 위치 복원: {vm.SavedTerminalScrollVerticalOffset}");
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// 입력창 키 입력 처리 - 클립보드 이미지 붙여넣기 지원 + 히스토리 네비게이션 + 자동완성
    /// </summary>
    private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 자동완성 팝업이 열려있을 때 키 처리
        if (AutoCompletePopup.IsOpen && AutoCompleteControl.HasSuggestions)
        {
            switch (e.Key)
            {
                case Key.Up:
                    AutoCompleteControl.MoveSelectionUp();
                    e.Handled = true;
                    return;

                case Key.Down:
                    AutoCompleteControl.MoveSelectionDown();
                    e.Handled = true;
                    return;

                case Key.Tab:
                    AutoCompleteControl.ConfirmSelection();
                    e.Handled = true;
                    return;

                case Key.Escape:
                    HideAutoComplete();
                    e.Handled = true;
                    return;

                case Key.Enter:
                    // Enter는 자동완성 확정이 아닌 명령어 전송으로 처리
                    HideAutoComplete();
                    return;
            }
        }

        // Escape: 자동완성 팝업 닫기
        if (e.Key == Key.Escape)
        {
            if (AutoCompletePopup.IsOpen)
            {
                HideAutoComplete();
                e.Handled = true;
                return;
            }
        }

        // Up 키: 이전 명령어 (자동완성 팝업이 닫혀있을 때)
        if (e.Key == Key.Up && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (DataContext is ServerSessionViewModel vm)
            {
                var previousCommand = vm.GetPreviousCommand(vm.UserInput ?? "");
                if (previousCommand != null)
                {
                    vm.UserInput = previousCommand;

                    // 커서를 맨 끝으로 이동
                    if (sender is TextBox textBox)
                    {
                        textBox.SelectionStart = textBox.Text.Length;
                        textBox.SelectionLength = 0;
                    }

                    e.Handled = true;
                }
            }
        }
        // Down 키: 다음 명령어
        else if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (DataContext is ServerSessionViewModel vm)
            {
                var nextCommand = vm.GetNextCommand();
                if (nextCommand != null)
                {
                    vm.UserInput = nextCommand;

                    // 커서를 맨 끝으로 이동
                    if (sender is TextBox textBox)
                    {
                        textBox.SelectionStart = textBox.Text.Length;
                        textBox.SelectionLength = 0;
                    }

                    e.Handled = true;
                }
            }
        }
        // Ctrl+V 감지
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
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

            // ViewModel에 이미지 경로 전달
            if (DataContext is ServerSessionViewModel vm)
            {
                // 현재 입력에 이미지 경로 추가 (또는 별도 처리)
                var currentInput = vm.UserInput ?? "";
                
                // 이미지 경로를 인라인으로 추가 (사용자가 편집 가능)
                var imageText = string.Format(LocalizationService.Instance.GetString("ServerSession.ImageAttachment"), imagePath);
                vm.UserInput = string.IsNullOrEmpty(currentInput)
                    ? imageText
                    : $"{currentInput} {imageText}";

                // 사용자에게 알림
                vm.AddMessage(
                    string.Format(LocalizationService.Instance.GetString("ServerSession.ImageSaved"), imagePath),
                    MessageType.Info);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("ServerSession.ImagePasteException"), ex.Message),
                LocalizationService.Instance.GetString("Common.Error"),
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
        var input = InputTextBox.Text?.Trim();

        // 입력이 비어있으면 모든 추천 숨김
        if (string.IsNullOrEmpty(input) || input.Length < 2)
        {
            HideSuggestion();
            HideAutoComplete();
            return;
        }

        // 추천 토글이 꺼져 있으면 기존 추천만 숨김
        if (SuggestionToggle.IsChecked != true)
        {
            HideSuggestion();
        }
        else
        {
            // 디바운스 타이머 재시작 (기존 추천 기능)
            _suggestionDebounceTimer?.Stop();
            _suggestionDebounceTimer?.Start();
        }

        // 자동완성 팝업 표시 (항상 활성화)
        ShowAutoComplete(input);
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
                    result.SearchMethod ?? LocalizationService.Instance.GetString("ServerSession.Cache"),
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
            sourceText = string.Format(LocalizationService.Instance.GetString("ServerSession.EmbeddingSearch"), similarity);
            iconKind = MaterialDesignThemes.Wpf.PackIconKind.VectorCombine;
        }
        else if (source.Contains("FTS") || source.Contains("텍스트"))
        {
            sourceText = LocalizationService.Instance.GetString("ServerSession.TextSearch");
            iconKind = MaterialDesignThemes.Wpf.PackIconKind.TextSearch;
        }
        else
        {
            sourceText = string.Format(LocalizationService.Instance.GetString("ServerSession.CacheWithSimilarity"), similarity);
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
        if (DataContext is not ServerSessionViewModel vm)
            return;

        // IsFileTreeVisible 값에 따라 파일 트리 표시/숨김
        if (vm.IsFileTreeVisible)
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
            MessageBox.Show(
                LocalizationService.Instance.GetString("ServerSession.NotConnected"),
                LocalizationService.Instance.GetString("Common.Notification"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            if (DataContext is ServerSessionViewModel vmTemp)
                vmTemp.IsFileTreeVisible = false;
            return;
        }

        if (!vm.IsConnected)
        {
            MessageBox.Show(
                LocalizationService.Instance.GetString("ServerSession.ConnectFirst"),
                LocalizationService.Instance.GetString("Common.Notification"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            vm.IsFileTreeVisible = false;
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
                // FileTreePanelControl (MainWindow에서 관리).CloseRequested += (s, args) =>
                {
                    // IsChecked는 IsFileTreeVisible에 바인딩되어 있으므로 ViewModel만 업데이트
                    if (DataContext is ServerSessionViewModel vmClose)
                    {
                        vmClose.IsFileTreeVisible = false;  // 이렇게 하면 토글 버튼도 자동 업데이트됨
                    }
                    HideFileTree();
                };

                // FileTreePanel은 MainWindow에서 관리
                // FileTreePanelControl.OpenInTerminalRequested += (s, path) =>
                // {
                //     if (path != null && DataContext is ServerSessionViewModel sessionVm)
                //     {
                //         sessionVm.UserInput = $"cd {path}";
                //     }
                // };

                // FileTreePanel은 MainWindow에서 관리
                // FileTreePanelControl.FileDoubleClicked += async (s, item) =>
                // {
                //     // 파일 더블클릭 시 편집기 열기
                //     if (_sftpService != null && !item.IsDirectory)
                //     {
                //         try
                //         {
                //             var editorWindow = new FileEditorWindow(_sftpService, item.FullPath);
                //             editorWindow.Owner = Window.GetWindow(this);
                //             editorWindow.Show();
                //         }
                //         catch (Exception ex)
                //         {
                //             MessageBox.Show(
                //                 string.Format(LocalizationService.Instance.GetString("ServerSession.CannotOpenFile"), ex.Message),
                //                 LocalizationService.Instance.GetString("Common.Error"),
                //                 MessageBoxButton.OK, MessageBoxImage.Error);
                //         }
                //     }
                // };

                // FileTreePanel은 MainWindow에서 관리
                // 파일 탐색기에서 디렉토리 변경 시 처리
                // FileTreePanelControl.DirectoryChanged += async (s, path) =>
                // {
                //     if (!string.IsNullOrEmpty(path) && DataContext is ServerSessionViewModel sessionVm)
                //     {
                //         // ViewModel에 경로 저장 (각 탭마다 독립적)
                //         sessionVm.FileTreeCurrentPath = path;
                //
                //         // 현재 디렉토리와 다를 때만 cd 실행
                //         if (sessionVm.CurrentDirectory != path)
                //         {
                //             sessionVm.UserInput = $"cd {path}";
                //             // 자동 실행
                //             await sessionVm.ExecuteCurrentInputAsync();
                //         }
                //     }
                // };

                _isFileTreeInitialized = true;
            }

            // SFTP 클라이언트 가져오기
            var sftpClient = _sftpService.GetSftpClient();
            if (sftpClient != null)
            {
                // FileTreePanel은 MainWindow에서 관리
                // await FileTreePanelControl.InitializeSshAsync(sftpClient, _sftpService);
                // FileTreePanelControl.Visibility = Visibility.Visible;

                // ViewModel 상태 업데이트
                if (DataContext is ServerSessionViewModel vmUpdate)
                {
                    vmUpdate.IsFileTreeVisible = true;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("ServerSession.CannotOpenFileTree"), ex.Message),
                LocalizationService.Instance.GetString("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            if (DataContext is ServerSessionViewModel vmTemp)
                vmTemp.IsFileTreeVisible = false;
        }
    }

    /// <summary>
    /// 파일 트리 숨김
    /// </summary>
    private void HideFileTree()
    {
        // FileTreePanelControl (MainWindow에서 관리).Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 파일 워처 활성화 (탭 활성화 시)
    /// </summary>
    public void ActivateFileWatcher()
    {
        // FileTreePanelControl (MainWindow에서 관리).EnableFileWatcher();
    }

    /// <summary>
    /// 파일 워처 비활성화 (탭 비활성화 시)
    /// </summary>
    public void DeactivateFileWatcher()
    {
        // FileTreePanelControl (MainWindow에서 관리).DisableFileWatcher();
    }

    /// <summary>
    /// 검색/필터 초기화
    /// </summary>
    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ServerSessionViewModel vm)
        {
            vm.SearchText = string.Empty;
            vm.StatusFilter = null;
            StatusFilterComboBox.SelectedIndex = 0; // "전체" 선택
        }
    }

    /// <summary>
    /// 상태 필터 변경
    /// </summary>
    private void StatusFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DataContext is not ServerSessionViewModel vm)
            return;

        if (StatusFilterComboBox.SelectedItem is ComboBoxItem item)
        {
            var tag = item.Tag as string;
            if (string.IsNullOrEmpty(tag))
            {
                vm.StatusFilter = null;
            }
            else if (Enum.TryParse<BlockStatus>(tag, out var status))
            {
                vm.StatusFilter = status;
            }
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

            if (DataContext is not ServerSessionViewModel vm)
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
            System.Diagnostics.Debug.WriteLine($"[InputTextBox_Drop] 오류: {ex.Message}");
        }
    }

    #endregion

    #region 자동완성 팝업

    private bool _isAutoCompleteEnabled = true;

    /// <summary>
    /// 자동완성 제안 확정 (Tab 또는 더블클릭)
    /// </summary>
    private void AutoComplete_SuggestionConfirmed(object? sender, AutoCompleteSuggestion suggestion)
    {
        if (DataContext is ServerSessionViewModel vm)
        {
            // 사용자 입력을 선택한 제안으로 대체
            vm.UserInput = suggestion.UserInput;
            AutoCompletePopup.IsOpen = false;
            InputTextBox.Focus();
            InputTextBox.CaretIndex = InputTextBox.Text?.Length ?? 0;
        }
    }

    /// <summary>
    /// 자동완성 닫기 요청
    /// </summary>
    private void AutoComplete_CloseRequested(object? sender, EventArgs e)
    {
        AutoCompletePopup.IsOpen = false;
        InputTextBox.Focus();
    }

    /// <summary>
    /// 자동완성 팝업 표시
    /// </summary>
    private async void ShowAutoComplete(string query)
    {
        if (!_isAutoCompleteEnabled) return;
        if (AutoCompletePopup == null || AutoCompleteControl == null) return;

        try
        {
            AutoCompletePopup.IsOpen = true;
            await AutoCompleteControl.SearchAsync(query);

            // 결과가 없으면 팝업 닫기
            if (!AutoCompleteControl.HasSuggestions)
            {
                AutoCompletePopup.IsOpen = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AutoComplete] 검색 오류: {ex.Message}");
            AutoCompletePopup.IsOpen = false;
        }
    }

    /// <summary>
    /// 자동완성 팝업 숨기기
    /// </summary>
    private void HideAutoComplete()
    {
        if (AutoCompletePopup != null)
            AutoCompletePopup.IsOpen = false;
        AutoCompleteControl?.ClearSuggestions();
    }

    #endregion

    #region AI 장기기억 패널 관리

    /// <summary>
    /// 메모리 토글 버튼 클릭
    /// </summary>
    private void MemoryToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isMemoryPanelVisible)
        {
            HideMemoryPanel();
        }
        else
        {
            ShowMemoryPanel();
        }
    }

    /// <summary>
    /// 메모리 패널 초기화
    /// </summary>
    private void InitializeMemoryPanel()
    {
        if (_isMemoryPanelInitialized) return;

        // 작업 디렉토리 설정 (로컬 경로 사용 - 서버별로 관리)
        if (DataContext is ServerSessionViewModel vm && !string.IsNullOrEmpty(vm.ServerProfile?.Host))
        {
            // 서버별 메모리 파일 경로 설정
            var serverMemoryPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TermSnap",
                "ServerMemory",
                vm.ServerProfile.Host);

            if (!System.IO.Directory.Exists(serverMemoryPath))
            {
                System.IO.Directory.CreateDirectory(serverMemoryPath);
            }

            MemoryPanelControl.SetWorkingDirectory(serverMemoryPath);
        }

        // 패널 닫기 요청
        MemoryPanelControl.CloseRequested += (s, e) =>
        {
            HideMemoryPanel();
        };

        _isMemoryPanelInitialized = true;
    }

    /// <summary>
    /// 메모리 패널 표시
    /// </summary>
    private void ShowMemoryPanel()
    {
        InitializeMemoryPanel();
        MemoryBorder.Visibility = Visibility.Visible;
        _isMemoryPanelVisible = true;
    }

    /// <summary>
    /// 메모리 패널 숨김
    /// </summary>
    private void HideMemoryPanel()
    {
        MemoryBorder.Visibility = Visibility.Collapsed;
        _isMemoryPanelVisible = false;
    }

    #endregion
}
