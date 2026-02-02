using System;
using System.Windows;
using System.Windows.Controls;
using TermSnap.Views;

namespace TermSnap.Services;

/// <summary>
/// 패널 타입
/// </summary>
public enum PanelType
{
    None,
    FileTree,
    FileViewer,
    AITools,
    SubProcess
}

/// <summary>
/// 패널 관리자 - 모든 사이드 패널의 표시/숨김/초기화 통합 관리
/// </summary>
public class PanelManager : IDisposable
{
    private readonly FrameworkElement _owner;
    private string? _workingDirectory;
    private bool _disposed = false;

    // 패널 Border 참조
    private Border? _fileTreeBorder;
    private Border? _fileViewerBorder;
    private Border? _aiToolsBorder;
    private Border? _subProcessBorder;

    // 패널 컨트롤 참조
    private AIToolsPanel? _aiToolsPanel;
    private SubProcessPanel? _subProcessPanel;

    // 초기화 상태
    private bool _isAIToolsInitialized = false;
    private bool _isSubProcessInitialized = false;

    // 현재 열린 오른쪽 패널 (하나만 열림)
    private PanelType _currentRightPanel = PanelType.None;

    /// <summary>
    /// 패널 열림/닫힘 이벤트
    /// </summary>
    public event EventHandler<PanelType>? PanelOpened;
    public event EventHandler<PanelType>? PanelClosed;

    /// <summary>
    /// 명령어 실행 요청 이벤트 (AITools -> Terminal)
    /// </summary>
    public event EventHandler<string>? CommandRequested;

    /// <summary>
    /// 현재 열린 오른쪽 패널
    /// </summary>
    public PanelType CurrentRightPanel => _currentRightPanel;

    /// <summary>
    /// AI Tools 패널이 열려있는지
    /// </summary>
    public bool IsAIToolsVisible => _currentRightPanel == PanelType.AITools;

    /// <summary>
    /// AIToolsPanel의 MemoryService 인스턴스 (탭별 독립)
    /// </summary>
    public MemoryService? MemoryService => _aiToolsPanel?.MemoryService;

    public PanelManager(FrameworkElement owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// 패널 Border 등록
    /// </summary>
    public void RegisterPanels(
        Border? fileTreeBorder,
        Border? fileViewerBorder,
        Border? aiToolsBorder,
        Border? subProcessBorder)
    {
        _fileTreeBorder = fileTreeBorder;
        _fileViewerBorder = fileViewerBorder;
        _aiToolsBorder = aiToolsBorder;
        _subProcessBorder = subProcessBorder;

        // AITools 패널 초기화
        if (_aiToolsBorder?.Child is AIToolsPanel aiTools)
        {
            _aiToolsPanel = aiTools;
            _aiToolsPanel.CloseRequested += (s, e) => HidePanel(PanelType.AITools);
            _aiToolsPanel.CommandRequested += (s, cmd) => CommandRequested?.Invoke(this, cmd);
        }

        // SubProcess 패널 초기화
        if (_subProcessBorder?.Child is SubProcessPanel subProcess)
        {
            _subProcessPanel = subProcess;
        }
    }

    /// <summary>
    /// 작업 디렉토리 설정
    /// </summary>
    public void SetWorkingDirectory(string path)
    {
        _workingDirectory = path;
        _aiToolsPanel?.SetWorkingDirectory(path);
    }

    /// <summary>
    /// 패널 토글
    /// </summary>
    public void TogglePanel(PanelType panelType)
    {
        if (_currentRightPanel == panelType)
        {
            HidePanel(panelType);
        }
        else
        {
            ShowPanel(panelType);
        }
    }

    /// <summary>
    /// 패널 표시
    /// </summary>
    public void ShowPanel(PanelType panelType)
    {
        // 기존 오른쪽 패널 숨김 (FileTree, FileViewer 제외)
        if (_currentRightPanel != PanelType.None &&
            _currentRightPanel != PanelType.FileTree &&
            _currentRightPanel != PanelType.FileViewer)
        {
            HidePanelInternal(_currentRightPanel);
        }

        // 새 패널 표시
        switch (panelType)
        {
            case PanelType.AITools:
                ShowAIToolsPanel();
                break;
            case PanelType.SubProcess:
                ShowSubProcessPanel();
                break;
            case PanelType.FileTree:
                ShowFileTreePanel();
                break;
            case PanelType.FileViewer:
                ShowFileViewerPanel();
                break;
        }

        if (panelType != PanelType.FileTree && panelType != PanelType.FileViewer)
        {
            _currentRightPanel = panelType;
        }

        PanelOpened?.Invoke(this, panelType);
    }

    /// <summary>
    /// 패널 숨김
    /// </summary>
    public void HidePanel(PanelType panelType)
    {
        HidePanelInternal(panelType);

        if (_currentRightPanel == panelType)
        {
            _currentRightPanel = PanelType.None;
        }

        PanelClosed?.Invoke(this, panelType);
    }

    /// <summary>
    /// 모든 오른쪽 패널 숨김
    /// </summary>
    public void HideAllRightPanels()
    {
        HidePanelInternal(PanelType.AITools);
        HidePanelInternal(PanelType.SubProcess);
        _currentRightPanel = PanelType.None;
    }

    #region Private Panel Methods

    private void ShowAIToolsPanel()
    {
        if (_aiToolsBorder == null) return;

        if (!_isAIToolsInitialized)
        {
            InitializeAIToolsPanel();
        }

        if (!string.IsNullOrEmpty(_workingDirectory))
        {
            _aiToolsPanel?.SetWorkingDirectory(_workingDirectory);
        }

        _aiToolsBorder.Visibility = Visibility.Visible;
    }

    private void InitializeAIToolsPanel()
    {
        if (_isAIToolsInitialized) return;
        _isAIToolsInitialized = true;
    }

    private void ShowSubProcessPanel()
    {
        if (_subProcessBorder == null) return;

        if (!_isSubProcessInitialized)
        {
            InitializeSubProcessPanel();
        }

        _subProcessBorder.Visibility = Visibility.Visible;
    }

    private void InitializeSubProcessPanel()
    {
        if (_isSubProcessInitialized) return;
        _isSubProcessInitialized = true;
    }

    private void ShowFileTreePanel()
    {
        if (_fileTreeBorder != null)
        {
            _fileTreeBorder.Visibility = Visibility.Visible;
        }
    }

    private void ShowFileViewerPanel()
    {
        if (_fileViewerBorder != null)
        {
            _fileViewerBorder.Visibility = Visibility.Visible;
        }
    }

    private void HidePanelInternal(PanelType panelType)
    {
        switch (panelType)
        {
            case PanelType.AITools:
                if (_aiToolsBorder != null)
                    _aiToolsBorder.Visibility = Visibility.Collapsed;
                break;
            case PanelType.SubProcess:
                if (_subProcessBorder != null)
                    _subProcessBorder.Visibility = Visibility.Collapsed;
                break;
            case PanelType.FileTree:
                if (_fileTreeBorder != null)
                    _fileTreeBorder.Visibility = Visibility.Collapsed;
                break;
            case PanelType.FileViewer:
                if (_fileViewerBorder != null)
                    _fileViewerBorder.Visibility = Visibility.Collapsed;
                break;
        }
    }

    #endregion

    #region AI Tools Specific

    /// <summary>
    /// AI Tools 패널 진행 상황 업데이트 (Ralph Loop용)
    /// </summary>
    public void UpdateRalphProgress(int progress, int iteration, int maxIterations, string currentTask)
    {
        _aiToolsPanel?.UpdateRalphProgress(progress, iteration, maxIterations, currentTask);
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _aiToolsPanel = null;
        _subProcessPanel = null;
    }
}
