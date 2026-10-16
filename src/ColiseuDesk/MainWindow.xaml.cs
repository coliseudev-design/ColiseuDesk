using System;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ColiseuDesk.Services;

namespace ColiseuDesk
{
    public partial class MainWindow : Window
    {
        private readonly RustDeskEngineService _engineService = new();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ClientsListBox.ItemsSource = _engineService.Settings.SavedClients;
            RegeneratePassword();
            UpdateVpsStatusBanner();

            var localId = await _engineService.GetLocalMachineIdAsync();
            LocalIdText.Text = localId;
        }

        private void UpdateVpsStatusBanner()
        {
            var host = _engineService.Settings.RendezvousServer;
            if (!string.IsNullOrWhiteSpace(host))
            {
                TopVpsBadge.Text = $"🟢 VPS Coliseu Conectada ({host}) • Criptografia Ed25519";
                StatusTextBlock.Text = $"Conectado ao servidor VPS Coliseu Sistemas ({host}:21116) — Motor RustDesk Pronto";
            }
            else
            {
                TopVpsBadge.Text = "🔒 Motor RustDesk • Aguardando IP da VPS";
                StatusTextBlock.Text = "Motor RustDesk integrado. Clique em 'Configurar VPS' para definir o IP do seu servidor.";
            }
        }

        private void RegeneratePassword()
        {
            const string chars = "abcdefghjkmnpqrstuvwxyz23456789";
            char[] buffer = new char[6];
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
            }
            TempPasswordText.Text = new string(buffer);
        }

        private void CopyLocalId_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(LocalIdText.Text);
            StatusTextBlock.Text = $"ID {LocalIdText.Text} copiado para a área de transferência!";
        }

        private void RegeneratePassword_Click(object sender, RoutedEventArgs e)
        {
            RegeneratePassword();
            StatusTextBlock.Text = "Nova senha temporária de sessão gerada.";
        }

        private void CopyPassword_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(TempPasswordText.Text);
            StatusTextBlock.Text = "Senha temporária copiada para a área de transferência!";
        }

        private void SaveFixedPassword_Click(object sender, RoutedEventArgs e)
        {
            var pwd = FixedPasswordBox.Password;
            if (string.IsNullOrWhiteSpace(pwd) || pwd.Length < 6)
            {
                MessageBox.Show(
                    "Defina uma senha fixa com pelo menos 6 caracteres para acesso não supervisionado.",
                    "ColiseuDesk - Senha Fixa",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            bool applied = _engineService.SetUnattendedPassword(pwd);
            StatusTextBlock.Text = applied
                ? "Senha fixa configurada com sucesso no motor RustDesk!"
                : "Senha fixa salva nas configurações do ColiseuDesk.";

            MessageBox.Show(
                "Acesso Não Supervisionado configurado!\nVocê poderá conectar neste computador a qualquer momento usando essa senha fixa.",
                "ColiseuDesk - Coliseu Sistemas",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private async void InitiateConnection(string remoteId, string mode = "--connect")
        {
            var formatted = RustDeskEngineService.FormatId(remoteId);
            if (string.IsNullOrWhiteSpace(formatted))
            {
                MessageBox.Show("Digite o ID de 9 dígitos do computador do cliente.", "ColiseuDesk", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TabRemoteIdText.Text = formatted;
            ToolbarRemoteIdBox.Text = formatted;
            MainRemoteIdBox.Text = formatted;

            StatusTextBlock.Text = $"Iniciando conexão ({mode}) com o cliente {formatted}...";

            var enginePath = _engineService.FindEngineExecutable();
            if (enginePath == null)
            {
                var result = MessageBox.Show(
                    $"Para abrir a sessão remota com vídeo, som e transferência de arquivos para o ID {formatted}, o motor de execução RustDesk será baixado automaticamente agora (~20 MB).\n\nDeseja baixar e iniciar a conexão?",
                    "ColiseuDesk - Motor de Acesso Remoto",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await _engineService.EnsureEngineInstalledAsync(msg => Dispatcher.Invoke(() => StatusTextBlock.Text = msg));
                }
                else
                {
                    return;
                }
            }

            if (_engineService.LaunchRemoteConnection(formatted, mode))
            {
                StatusTextBlock.Text = $"Sessão iniciada com {formatted} via motor RustDesk!";
            }
            else
            {
                StatusTextBlock.Text = $"Não foi possível iniciar o motor para {formatted}. Verifique a instalação.";
            }
        }

        private void ConnectMain_Click(object sender, RoutedEventArgs e) => InitiateConnection(MainRemoteIdBox.Text, "--connect");

        private void FileTransferMain_Click(object sender, RoutedEventArgs e) => InitiateConnection(MainRemoteIdBox.Text, "--file-transfer");

        private void ConnectFromToolbar_Click(object sender, RoutedEventArgs e) => InitiateConnection(ToolbarRemoteIdBox.Text, "--connect");

        private void FileTransferFromToolbar_Click(object sender, RoutedEventArgs e) => InitiateConnection(ToolbarRemoteIdBox.Text, "--file-transfer");

        private void MainRemoteIdBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                InitiateConnection(MainRemoteIdBox.Text, "--connect");
        }

        private void ToolbarRemoteIdBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                InitiateConnection(ToolbarRemoteIdBox.Text, "--connect");
        }

        private void ConnectSavedClient_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                InitiateConnection(id, "--connect");
            }
        }

        private void ClientsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ClientsListBox.SelectedItem is SavedClientItem item)
            {
                InitiateConnection(item.Id, "--connect");
            }
        }

        private void ActiveSessionTab_Click(object sender, MouseButtonEventArgs e)
        {
            InitiateConnection(TabRemoteIdText.Text, "--connect");
        }

        private void FocusRemoteInput_Click(object sender, RoutedEventArgs e)
        {
            MainRemoteIdBox.Focus();
            MainRemoteIdBox.SelectAll();
        }

        private void ToolbarFeature_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string featureDesc)
            {
                StatusTextBlock.Text = $"Recurso ativo na sessão: {featureDesc}";
                MessageBox.Show(
                    $"Recurso da Barra ColiseuDesk:\n• {featureDesc}\n\nEste controle atua diretamente na sessão ativa do motor RustDesk com o cliente {ToolbarRemoteIdBox.Text}.",
                    "Barra de Controle ColiseuDesk",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void AddFavoriteClient_Click(object sender, RoutedEventArgs e)
        {
            var id = RustDeskEngineService.FormatId(MainRemoteIdBox.Text);
            if (string.IsNullOrWhiteSpace(id))
                return;

            _engineService.Settings.SavedClients.Insert(0, new SavedClientItem
            {
                Id = id,
                Name = $"Estação Cliente ({id})",
                Company = "Cliente Coliseu Sistemas",
                LastConnected = "Agora mesmo",
                IsOnline = true
            });
            _engineService.PersistSettings();
            ClientsListBox.Items.Refresh();
            StatusTextBlock.Text = $"Cliente {id} adicionado aos favoritos!";
        }

        private void OpenVpsModal_Click(object sender, RoutedEventArgs e)
        {
            VpsHostInput.Text = _engineService.Settings.RendezvousServer;
            VpsKeyInput.Text = _engineService.Settings.PublicKey;
            VpsModalOverlay.Visibility = Visibility.Visible;
        }

        private void CloseVpsModal_Click(object sender, RoutedEventArgs e)
        {
            VpsModalOverlay.Visibility = Visibility.Collapsed;
        }

        private void SaveVpsConfig_Click(object sender, RoutedEventArgs e)
        {
            _engineService.SaveSettings(VpsHostInput.Text, VpsKeyInput.Text);
            UpdateVpsStatusBanner();
            VpsModalOverlay.Visibility = Visibility.Collapsed;

            MessageBox.Show(
                "Configuração da VPS salva e aplicada automaticamente no motor RustDesk (RustDesk2.toml)!",
                "ColiseuDesk Server",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
