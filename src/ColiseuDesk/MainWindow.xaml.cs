using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ColiseuDesk.Services;

namespace ColiseuDesk
{
    public partial class MainWindow : Window
    {
        private readonly RustDeskEngineService _engineService = new RustDeskEngineService();
        private readonly DispatcherTimer _statusTimer = new DispatcherTimer();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshClientsList();
            LocalIdText.Text = "Sincronizando...";
            TempPasswordText.Text = "......";
            TopVpsBadge.Text = "Conectando à Rede Coliseu Sistemas...";

            await _engineService.InitializeAndStartServerAsync(msg =>
            {
                Dispatcher.Invoke(() => StatusTextBlock.Text = msg);
            });

            await SyncLiveEngineStatusAsync();

            _statusTimer.Interval = TimeSpan.FromSeconds(3);
            _statusTimer.Tick += async (s, args) => await SyncLiveEngineStatusAsync();
            _statusTimer.Start();
        }

        private async Task SyncLiveEngineStatusAsync()
        {
            var status = await Task.Run(() => _engineService.QueryLiveStatusFromPipe());
            if (!string.IsNullOrEmpty(status.Id))
            {
                LocalIdText.Text = status.Id;
            }
            if (!string.IsNullOrEmpty(status.TemporaryPassword))
            {
                TempPasswordText.Text = status.TemporaryPassword;
            }

            if (status.IsOnline)
            {
                TopVpsBadge.Text = "Rede Coliseu Sistemas Online • Pronto para Receber ou Dar Suporte";
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(27, 135, 63));
                if (StatusTextBlock.Text.StartsWith("Conectando") || StatusTextBlock.Text.StartsWith("Preparando") || StatusTextBlock.Text.StartsWith("Baixando"))
                {
                    StatusTextBlock.Text = "Pronto para conexões (Registrado na Rede Coliseu Sistemas)";
                }
            }
        }

        private void RefreshClientsList()
        {
            ClientsListBox.ItemsSource = null;
            ClientsListBox.ItemsSource = _engineService.Settings.SavedClients;
            EmptyHistoryPlaceholder.Visibility = _engineService.Settings.SavedClients.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void CopyLocalId_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(LocalIdText.Text);
            StatusTextBlock.Text = string.Format("ID {0} copiado para a área de transferência!", LocalIdText.Text);
        }

        private async void RegeneratePassword_Click(object sender, RoutedEventArgs e)
        {
            var newPwd = await Task.Run(() => _engineService.RegenerateTemporaryPasswordInEngine());
            if (!string.IsNullOrEmpty(newPwd))
            {
                TempPasswordText.Text = newPwd;
                StatusTextBlock.Text = "Nova senha temporária gerada no motor de acesso.";
            }
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
                    "Defina uma senha fixa com pelo menos 6 caracteres.",
                    "ColiseuDesk - Senha Fixa",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            bool ok = _engineService.SetUnattendedPassword(pwd);
            StatusTextBlock.Text = ok
                ? "Acesso permanente (Senha Fixa) ativado neste computador!"
                : "Senha fixa salva nas configurações.";

            MessageBox.Show(
                "Senha fixa configurada com sucesso!\nAgora você pode conectar neste computador usando essa senha fixa.",
                "ColiseuDesk - Coliseu Sistemas",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private async void InitiateConnection(string remoteId, string mode = "--connect")
        {
            var formatted = RustDeskEngineService.FormatId(remoteId);
            if (string.IsNullOrWhiteSpace(formatted))
            {
                MessageBox.Show("Digite o ID de 9 dígitos do computador remoto.", "ColiseuDesk", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TabRemoteIdText.Text = formatted;
            ActiveSessionTab.Visibility = Visibility.Visible;
            ToolbarRemoteIdBox.Text = formatted;
            MainRemoteIdBox.Text = formatted;

            _engineService.AddOrUpdateRecentClient(formatted);
            RefreshClientsList();

            StatusTextBlock.Text = string.Format("Iniciando conexão com {0}...", formatted);

            if (_engineService.FindExtractedRuntimeExecutable() == null)
            {
                await _engineService.InitializeAndStartServerAsync(msg =>
                    Dispatcher.Invoke(() => StatusTextBlock.Text = msg));
            }

            if (_engineService.LaunchRemoteConnection(formatted, mode))
            {
                StatusTextBlock.Text = string.Format("Sessão remota iniciada com {0}.", formatted);
            }
            else
            {
                StatusTextBlock.Text = "Aguarde a conclusão da inicialização do motor e tente novamente.";
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
            var btn = sender as Button;
            if (btn != null && btn.Tag is string id)
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
            var btn = sender as Button;
            if (btn != null && btn.Tag is string featureDesc)
            {
                StatusTextBlock.Text = string.Format("Recurso selecionado: {0}", featureDesc);
                if (!string.IsNullOrWhiteSpace(ToolbarRemoteIdBox.Text))
                {
                    InitiateConnection(ToolbarRemoteIdBox.Text, "--connect");
                }
            }
        }

        private void AddFavoriteClient_Click(object sender, RoutedEventArgs e)
        {
            var id = RustDeskEngineService.FormatId(MainRemoteIdBox.Text);
            if (string.IsNullOrWhiteSpace(id))
            {
                MessageBox.Show("Digite um ID de 9 dígitos no campo acima para salvar nos favoritos.", "ColiseuDesk", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _engineService.AddOrUpdateRecentClient(id);
            RefreshClientsList();
            StatusTextBlock.Text = string.Format("Computador {0} salvo na lista de conexões rápidas!", id);
        }

        // Atalho exclusivo e invisível para técnico (Ctrl + Shift + F12)
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F12 &&
                (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                VpsHostInput.Text = _engineService.Settings.RendezvousServer;
                VpsKeyInput.Text = _engineService.Settings.PublicKey;
                VpsModalOverlay.Visibility = Visibility.Visible;
                e.Handled = true;
            }
        }

        private void CloseVpsModal_Click(object sender, RoutedEventArgs e)
        {
            VpsModalOverlay.Visibility = Visibility.Collapsed;
        }

        private void SaveVpsConfig_Click(object sender, RoutedEventArgs e)
        {
            _engineService.SaveSettings(VpsHostInput.Text, VpsKeyInput.Text);
            VpsModalOverlay.Visibility = Visibility.Collapsed;
        }
    }
}
