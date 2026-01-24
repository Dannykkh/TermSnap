using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using TermSnap.Models;
using TermSnap.Services;

namespace TermSnap.Views;

public partial class PortForwardingManagerDialog : Window, INotifyPropertyChanged
{
    private readonly SshService? _sshService;
    private PortForwardingConfig? _selectedPortForwarding;

    public ObservableCollection<PortForwardingConfig> PortForwardings { get; } = new();

    public PortForwardingConfig? SelectedPortForwarding
    {
        get => _selectedPortForwarding;
        set => SetProperty(ref _selectedPortForwarding, value);
    }

    public ICommand AddPortForwardingCommand { get; }
    public ICommand AddFromTemplateCommand { get; }
    public ICommand DeletePortForwardingCommand { get; }
    public ICommand StartPortForwardingCommand { get; }
    public ICommand StopPortForwardingCommand { get; }

    /// <summary>
    /// SSH 연결 상태 (Start/Stop 버튼 표시 여부)
    /// </summary>
    public bool CanControl => _sshService != null;

    public PortForwardingManagerDialog(SshService? sshService = null, ObservableCollection<PortForwardingConfig>? existingConfigs = null)
    {
        InitializeComponent();
        DataContext = this;

        _sshService = sshService;

        // 기존 Port Forwarding 로드
        if (existingConfigs != null)
        {
            foreach (var config in existingConfigs)
            {
                PortForwardings.Add(config);
            }
        }

        // Commands
        AddPortForwardingCommand = new RelayCommand(AddPortForwarding);
        AddFromTemplateCommand = new RelayCommand(AddFromTemplate);
        DeletePortForwardingCommand = new RelayCommand(DeletePortForwarding, () => SelectedPortForwarding != null);
        StartPortForwardingCommand = new RelayCommand(async () => await StartPortForwardingAsync(), () => SelectedPortForwarding != null);
        StopPortForwardingCommand = new RelayCommand(async () => await StopPortForwardingAsync(), () => SelectedPortForwarding != null);
    }

    private void AddPortForwarding()
    {
        var newConfig = new PortForwardingConfig
        {
            Name = $"New Tunnel {PortForwardings.Count + 1}",
            Type = PortForwardingType.Local,
            LocalHost = "localhost",
            LocalPort = 8080,
            RemoteHost = "",
            RemotePort = 80,
            Status = PortForwardingStatus.Stopped
        };

        PortForwardings.Add(newConfig);
        SelectedPortForwarding = newConfig;
    }

    private void AddFromTemplate()
    {
        var templates = PortForwardingTemplate.GetDefaultTemplates();

        var templateDialog = new TemplateSelectionDialog(templates)
        {
            Owner = this
        };

        if (templateDialog.ShowDialog() == true && templateDialog.SelectedTemplate != null)
        {
            var config = templateDialog.SelectedTemplate.CreateConfig();
            PortForwardings.Add(config);
            SelectedPortForwarding = config;
        }
    }

    private void DeletePortForwarding()
    {
        if (SelectedPortForwarding == null) return;

        var result = MessageBox.Show(
            Application.Current.FindResource("PortForwarding.DeleteConfirm") as string ?? "Are you sure you want to delete this port forwarding?",
            Application.Current.FindResource("PortForwarding.Title") as string ?? "Port Forwarding",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );

        if (result == MessageBoxResult.Yes)
        {
            // 실행 중이면 먼저 중지 (SshService가 있을 때만)
            if (_sshService != null && SelectedPortForwarding.Status == PortForwardingStatus.Running)
            {
                _ = StopPortForwardingAsync();
            }

            PortForwardings.Remove(SelectedPortForwarding);
            SelectedPortForwarding = null;
        }
    }

    private async System.Threading.Tasks.Task StartPortForwardingAsync()
    {
        if (SelectedPortForwarding == null || _sshService == null) return;

        // 유효성 검사
        if (!SelectedPortForwarding.Validate(out var errorMessage))
        {
            MessageBox.Show(
                errorMessage,
                Application.Current.FindResource("PortForwarding.Title") as string ?? "Port Forwarding",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        bool success = SelectedPortForwarding.Type switch
        {
            PortForwardingType.Local => await _sshService.StartLocalPortForwardingAsync(SelectedPortForwarding),
            PortForwardingType.Remote => await _sshService.StartRemotePortForwardingAsync(SelectedPortForwarding),
            PortForwardingType.Dynamic => await _sshService.StartDynamicPortForwardingAsync(SelectedPortForwarding),
            _ => false
        };

        if (!success && !string.IsNullOrEmpty(SelectedPortForwarding.ErrorMessage))
        {
            MessageBox.Show(
                SelectedPortForwarding.ErrorMessage,
                Application.Current.FindResource("PortForwarding.Error") as string ?? "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private async System.Threading.Tasks.Task StopPortForwardingAsync()
    {
        if (SelectedPortForwarding == null || _sshService == null) return;

        await _sshService.StopPortForwardingAsync(SelectedPortForwarding);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
    {
        Regex regex = new Regex("[^0-9]+");
        e.Handled = regex.IsMatch(e.Text);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
