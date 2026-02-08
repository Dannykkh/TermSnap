using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TermSnap.Core.Sessions;
using TermSnap.Services;
using TermSnap.ViewModels;

namespace TermSnap.Views;

/// <summary>
/// ProjectSessionView - 서브탭별 View 캐싱 및 전환
/// </summary>
public partial class ProjectSessionView : UserControl
{
    private ProjectSessionViewModel? _viewModel;
    private bool _isSubTabSelectorInitialized = false;

    // 서브탭별 View 캐싱 (탭 전환 시 View 재사용)
    private readonly Dictionary<ISessionViewModel, UIElement> _subViewCache = new();

    public ProjectSessionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SetupSubTabSelector();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 이전 ViewModel 구독 해제
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel.SubSessions.CollectionChanged -= SubSessions_CollectionChanged;
        }

        _viewModel = DataContext as ProjectSessionViewModel;

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            _viewModel.SubSessions.CollectionChanged += SubSessions_CollectionChanged;

            // 초기 View 설정
            UpdateSubSessionView();
            UpdateEmptyState();
            UpdateSelectedHighlight();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectSessionViewModel.SelectedSubSession))
        {
            UpdateSubSessionView();
            UpdateSelectedHighlight();
        }
        else if (e.PropertyName == nameof(ProjectSessionViewModel.IsShowingSubTabSelector))
        {
            UpdateSubTabSelectorVisibility();
        }
    }

    private void SubSessions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 제거된 서브세션의 View 캐시 정리
        if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is ISessionViewModel sessionVm && _subViewCache.Remove(sessionVm, out var cachedView))
                {
                    if (cachedView is IDisposable disposable)
                        disposable.Dispose();
                }
            }
        }

        UpdateEmptyState();
    }

    /// <summary>
    /// 선택된 서브세션의 View를 ContentControl에 할당
    /// </summary>
    private void UpdateSubSessionView()
    {
        if (_viewModel?.SelectedSubSession == null)
        {
            SubSessionContentControl.Content = null;
            return;
        }

        var view = GetOrCreateSubView(_viewModel.SelectedSubSession);
        if (SubSessionContentControl.Content != view)
        {
            SubSessionContentControl.Content = view;
        }
    }

    /// <summary>
    /// 서브세션의 View를 캐시에서 가져오거나 새로 생성
    /// </summary>
    private UIElement GetOrCreateSubView(ISessionViewModel session)
    {
        if (_subViewCache.TryGetValue(session, out var cachedView))
            return cachedView;

        UIElement newView = session switch
        {
            LocalTerminalViewModel localVm => new LocalTerminalView { DataContext = localVm },
            ServerSessionViewModel serverVm => new ServerSessionView { DataContext = serverVm },
            _ => throw new NotSupportedException($"서브탭 지원 안 함: {session.GetType().Name}")
        };

        _subViewCache[session] = newView;
        return newView;
    }

    /// <summary>
    /// 서브탭이 없을 때 빈 상태 표시
    /// </summary>
    private void UpdateEmptyState()
    {
        if (_viewModel == null) return;

        EmptyStatePanel.Visibility = _viewModel.SubSessions.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        SubSessionContentControl.Visibility = _viewModel.SubSessions.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// 선택된 서브탭 하이라이트 업데이트
    /// </summary>
    private void UpdateSelectedHighlight()
    {
        // ItemsControl의 각 아이템 컨테이너에서 선택 상태를 시각적으로 표시
        // Border의 배경색으로 선택 상태를 나타냄
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_viewModel == null) return;

            // ItemsControl에서 각 아이템의 Border를 찾아서 선택 상태 업데이트
            var itemsControl = FindVisualChild<ItemsControl>(this);
            if (itemsControl == null) return;

            for (int i = 0; i < itemsControl.Items.Count; i++)
            {
                var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i);
                if (container == null) continue;

                var border = FindVisualChild<Border>(container);
                if (border != null)
                {
                    bool isSelected = itemsControl.Items[i] == _viewModel.SelectedSubSession;
                    border.Background = isSelected
                        ? (Brush)FindResource("CardBrush")
                        : Brushes.Transparent;
                    border.BorderThickness = isSelected
                        ? new Thickness(0, 0, 0, 2)
                        : new Thickness(0);
                    border.BorderBrush = isSelected
                        ? (Brush)FindResource("PrimaryBrush")
                        : Brushes.Transparent;
                }
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 서브탭 클릭 - 선택 전환
    /// </summary>
    private void SubTab_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ISessionViewModel session && _viewModel != null)
        {
            _viewModel.SelectedSubSession = session;
        }
    }

    /// <summary>
    /// 서브탭 닫기 클릭
    /// </summary>
    private void SubTabClose_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ISessionViewModel session && _viewModel != null)
        {
            _viewModel.CloseSubTab(session);
            e.Handled = true;
        }
    }

    /// <summary>
    /// 서브탭 선택기 초기화
    /// </summary>
    private void SetupSubTabSelector()
    {
        if (_isSubTabSelectorInitialized) return;

        // 서브탭 모드로 설정 (폴더 선택 숨김)
        SubTabSelectorPanel.SetSubTabMode(true);

        // 서브탭 시작 이벤트
        SubTabSelectorPanel.SubTabStartRequested += async (s, args) =>
        {
            if (_viewModel == null) return;

            // 쉘 타입 결정
            var shellType = args.Shell?.ShellType ?? LocalSession.LocalShellType.PowerShell;

            // 서브탭 선택기 숨김
            _viewModel.IsShowingSubTabSelector = false;

            if (args.CLIOptions != null)
            {
                // AI CLI 포함 서브탭 생성
                await AddSubTabWithAICLI(shellType, args.Shell, args.CLIOptions);
            }
            else
            {
                // 일반 터미널 서브탭 생성
                _viewModel.AddTerminalSubTab(shellType);
            }
        };

        // 서브탭 취소 이벤트
        SubTabSelectorPanel.SubTabCancelled += (s, args) =>
        {
            _viewModel?.CancelSubTabSelector();
        };

        _isSubTabSelectorInitialized = true;
    }

    /// <summary>
    /// AI CLI 포함 서브탭 생성
    /// </summary>
    private async Task AddSubTabWithAICLI(LocalSession.LocalShellType shellType,
        ShellDetectionService.DetectedShell? shell, ClaudeRunOptions cliOptions)
    {
        if (_viewModel == null) return;

        // 쉘 이름으로 서브탭 생성
        var localVm = new LocalTerminalViewModel(shellType);

        // 쉘 설정
        if (shell != null)
            localVm.SetShell(shell);

        // 프로그램 이름 설정
        var programName = cliOptions.Command.Split(' ')[0];
        localVm.TabHeader = programName;
        localVm.SetAICLIProgramName(programName);

        _viewModel.SubSessions.Add(localVm);
        _viewModel.SelectedSubSession = localVm;

        // 프로젝트 폴더에서 시작
        var folder = _viewModel.ProjectPath;
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            await localVm.OpenFolderAsync(folder);

            // 터미널 준비 대기
            await Task.Delay(500);

            // AI CLI 명령어 실행
            localVm.UserInput = cliOptions.Command;
            await localVm.ExecuteCurrentInputAsync();
        }
    }

    /// <summary>
    /// 서브탭 선택기 표시/숨김 토글
    /// </summary>
    private void UpdateSubTabSelectorVisibility()
    {
        if (_viewModel == null) return;

        if (_viewModel.IsShowingSubTabSelector)
        {
            // 선택기 표시, 콘텐츠 숨김
            SubTabSelectorPanel.Visibility = Visibility.Visible;
            SubSessionContentControl.Visibility = Visibility.Collapsed;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            // 선택기 숨김, 콘텐츠 복원
            SubTabSelectorPanel.Visibility = Visibility.Collapsed;
            UpdateSubSessionView();
            UpdateEmptyState();
        }
    }

    /// <summary>
    /// VisualTree에서 특정 타입의 자식 요소 찾기
    /// </summary>
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
                return result;

            var found = FindVisualChild<T>(child);
            if (found != null)
                return found;
        }
        return null;
    }
}
