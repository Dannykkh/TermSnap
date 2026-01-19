using System.Windows;
using Nebula.Models;
using Nebula.Services;
using Microsoft.Win32;

namespace Nebula.Views;

/// <summary>
/// 서버 프로필 편집 대화상자
/// </summary>
public partial class ProfileEditorDialog : Window
{
    private readonly ServerConfig? _existingProfile;
    
    /// <summary>
    /// 편집된 프로필
    /// </summary>
    public ServerConfig? ResultProfile { get; private set; }

    /// <summary>
    /// 새 프로필 생성
    /// </summary>
    public ProfileEditorDialog()
    {
        InitializeComponent();
        _existingProfile = null;
        TitleText.Text = "새 서버 프로필";
    }

    /// <summary>
    /// 기존 프로필 편집
    /// </summary>
    public ProfileEditorDialog(ServerConfig profile) : this()
    {
        _existingProfile = profile;
        TitleText.Text = "서버 프로필 편집";
        LoadProfile(profile);
    }

    private void LoadProfile(ServerConfig profile)
    {
        ProfileNameBox.Text = profile.ProfileName;
        HostBox.Text = profile.Host;
        PortBox.Text = profile.Port.ToString();
        UsernameBox.Text = profile.Username;
        FavoriteCheckBox.IsChecked = profile.IsFavorite;
        NotesBox.Text = profile.Notes;

        if (profile.AuthType == AuthenticationType.Password)
        {
            PasswordAuthRadio.IsChecked = true;
            // 암호화된 비밀번호 복호화
            if (!string.IsNullOrEmpty(profile.EncryptedPassword))
            {
                try
                {
                    PasswordBox.Password = EncryptionService.Decrypt(profile.EncryptedPassword);
                }
                catch
                {
                    PasswordBox.Password = string.Empty;
                }
            }
        }
        else
        {
            KeyAuthRadio.IsChecked = true;
            KeyPathBox.Text = profile.PrivateKeyPath;
            if (!string.IsNullOrEmpty(profile.EncryptedPassphrase))
            {
                try
                {
                    PassphraseBox.Password = EncryptionService.Decrypt(profile.EncryptedPassphrase);
                }
                catch
                {
                    PassphraseBox.Password = string.Empty;
                }
            }
        }

        UpdateAuthUI();
    }

    private void AuthType_Changed(object sender, RoutedEventArgs e)
    {
        UpdateAuthUI();
    }

    private void UpdateAuthUI()
    {
        if (PasswordPanel == null || KeyPanel == null) return;

        if (PasswordAuthRadio.IsChecked == true)
        {
            PasswordPanel.Visibility = Visibility.Visible;
            KeyPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            PasswordPanel.Visibility = Visibility.Collapsed;
            KeyPanel.Visibility = Visibility.Visible;
        }
    }

    private void BrowseKey_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "SSH 키 선택",
            Filter = "SSH 키 파일 (*.pem, *.ppk, *.key)|*.pem;*.ppk;*.key|" +
                     "PuTTY 키 (*.ppk)|*.ppk|" +
                     "OpenSSH/PEM 키 (*.pem)|*.pem|" +
                     "모든 파일|*.*",
            FilterIndex = 1,  // 기본값: SSH 키 파일
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            KeyPathBox.Text = dialog.FileName;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // 유효성 검사
        if (string.IsNullOrWhiteSpace(ProfileNameBox.Text))
        {
            MessageBox.Show("프로필 이름을 입력하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            ProfileNameBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(HostBox.Text))
        {
            MessageBox.Show("호스트를 입력하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            HostBox.Focus();
            return;
        }

        if (!int.TryParse(PortBox.Text, out int port) || port < 1 || port > 65535)
        {
            MessageBox.Show("유효한 포트 번호를 입력하세요 (1-65535).", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            PortBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(UsernameBox.Text))
        {
            MessageBox.Show("사용자 이름을 입력하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            UsernameBox.Focus();
            return;
        }

        if (PasswordAuthRadio.IsChecked == true && string.IsNullOrEmpty(PasswordBox.Password))
        {
            MessageBox.Show("비밀번호를 입력하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            PasswordBox.Focus();
            return;
        }

        if (KeyAuthRadio.IsChecked == true && string.IsNullOrWhiteSpace(KeyPathBox.Text))
        {
            MessageBox.Show("SSH 키 경로를 입력하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            KeyPathBox.Focus();
            return;
        }

        // 프로필 생성
        ResultProfile = new ServerConfig
        {
            ProfileName = ProfileNameBox.Text.Trim(),
            Host = HostBox.Text.Trim(),
            Port = port,
            Username = UsernameBox.Text.Trim(),
            AuthType = PasswordAuthRadio.IsChecked == true ? AuthenticationType.Password : AuthenticationType.PrivateKey,
            IsFavorite = FavoriteCheckBox.IsChecked == true,
            Notes = NotesBox.Text ?? string.Empty,
            LastConnected = _existingProfile?.LastConnected ?? System.DateTime.MinValue
        };

        // 비밀번호/키 암호화
        if (ResultProfile.AuthType == AuthenticationType.Password)
        {
            ResultProfile.EncryptedPassword = EncryptionService.Encrypt(PasswordBox.Password);
        }
        else
        {
            ResultProfile.PrivateKeyPath = KeyPathBox.Text.Trim();
            if (!string.IsNullOrEmpty(PassphraseBox.Password))
            {
                ResultProfile.EncryptedPassphrase = EncryptionService.Encrypt(PassphraseBox.Password);
            }
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
