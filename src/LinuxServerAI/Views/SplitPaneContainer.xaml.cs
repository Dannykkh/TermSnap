using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Nebula.ViewModels;

namespace Nebula.Views;

/// <summary>
/// 화면 분할 컨테이너 - 수평/수직 분할 지원
/// </summary>
public partial class SplitPaneContainer : UserControl, INotifyPropertyChanged
{
    private ISessionViewModel? _primarySession;
    private ISessionViewModel? _secondarySession;
    private SplitOrientation _orientation = SplitOrientation.None;

    public SplitPaneContainer()
    {
        InitializeComponent();
        DataContext = this;
    }

    /// <summary>
    /// 분할 방향
    /// </summary>
    public enum SplitOrientation
    {
        None,       // 단일 패널
        Horizontal, // 좌/우 분할
        Vertical    // 상/하 분할
    }

    /// <summary>
    /// 주 세션 (왼쪽/위)
    /// </summary>
    public ISessionViewModel? PrimarySession
    {
        get => _primarySession;
        set
        {
            _primarySession = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 보조 세션 (오른쪽/아래)
    /// </summary>
    public ISessionViewModel? SecondarySession
    {
        get => _secondarySession;
        set
        {
            _secondarySession = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 현재 분할 방향
    /// </summary>
    public SplitOrientation Orientation
    {
        get => _orientation;
        set
        {
            _orientation = value;
            UpdateLayout();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 분할 여부
    /// </summary>
    public bool IsSplit => Orientation != SplitOrientation.None && SecondarySession != null;

    /// <summary>
    /// 수평 분할 (좌/우)
    /// </summary>
    public void SplitHorizontal(ISessionViewModel secondarySession)
    {
        SecondarySession = secondarySession;
        Orientation = SplitOrientation.Horizontal;
    }

    /// <summary>
    /// 수직 분할 (상/하)
    /// </summary>
    public void SplitVertical(ISessionViewModel secondarySession)
    {
        SecondarySession = secondarySession;
        Orientation = SplitOrientation.Vertical;
    }

    /// <summary>
    /// 분할 해제
    /// </summary>
    public void Unsplit()
    {
        SecondarySession?.Dispose();
        SecondarySession = null;
        Orientation = SplitOrientation.None;
    }

    /// <summary>
    /// 레이아웃 업데이트
    /// </summary>
    private new void UpdateLayout()
    {
        SinglePane.Visibility = Visibility.Collapsed;
        HorizontalSplit.Visibility = Visibility.Collapsed;
        VerticalSplit.Visibility = Visibility.Collapsed;

        switch (Orientation)
        {
            case SplitOrientation.None:
                SinglePane.Visibility = Visibility.Visible;
                break;
            case SplitOrientation.Horizontal:
                HorizontalSplit.Visibility = Visibility.Visible;
                break;
            case SplitOrientation.Vertical:
                VerticalSplit.Visibility = Visibility.Visible;
                break;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
