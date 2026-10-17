using System;
using System.Collections.ObjectModel;
using System.Linq;
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
        private readonly ObservableCollection<string> _openSessionTabs = new ObservableCollection<string>();

        public MainWindow()
        {
            InitializeComponent();
            SessionTabsControl.ItemsSource = _openSessionTabs;
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshClientsList();
            UpdateToolbarToggleButtonsUI();

            LocalIdText.Text = "Sincronizando...";
            TempPasswordText.Text = "......";
            TopVpsBadge.Text = "Conectando à Rede Coliseu Sistemas...";

            await _engineService.InitializeAndStartServerAsync(msg =>
            {
                Dispatcher.Invoke(() => StatusTextBlock.Text = msg);
            });

            await SyncLiveEngineStatusAsync();

            _statusTimer.Interval = TimeSpan.FromSeconds(2);
            _statusTimer.Tick += async (s, args) => await SyncLiveEngineStatusAsync();
            _statusTimer.Start();
        }

        private async Task SyncLiveEngineStatusAsync()
        {
            var status = await Task.Run(() =>
            {
                _engineService.BrandOpenSessionWindows();
                return _engineService.QueryLiveStatusFromPipe();
            });

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

        private void UpdateToolbarToggleButtonsUI()
        {
            // Qualidade de Imagem
            string qLabel;
            switch (_engineService.Settings.ImageQuality)
            {
                case "balanced":
                    qLabel = "🎨 Imagem: Balanceada";
                    break;
                case "low":
                    qLabel = "🎨 Imagem: Rápida";
                    break;
                default:
                    qLabel = "🎨 Imagem: Alta";
                    break;
            }
            BtnQualityToggle.Content = qLabel;

            // Som Remoto
            BtnAudioToggle.Content = _engineService.Settings.EnableAudio ? "🔊 Som: Ativo" : "🔇 Som: Mudo";
            BtnAudioToggle.Foreground = new SolidColorBrush(_engineService.Settings.EnableAudio
                ? Color.FromRgb(51, 51, 51)
                : Color.FromRgb(150, 150, 150));

            // Área de Transferência (Ctrl+C / Ctrl+V)
            BtnClipboardToggle.Content = _engineService.Settings.EnableClipboard ? "📋 Ctrl+C/V: Ativo" : "📋 Ctrl+C/V: Inativo";
            BtnClipboardToggle.Foreground = new SolidColorBrush(_engineService.Settings.EnableClipboard
                ? Color.FromRgb(51, 51, 51)
                : Color.FromRgb(150, 150, 150));
        }

        private void RefreshClientsList()
        {
            ClientsListBox.ItemsSource = null;
            ClientsListBox.ItemsSource = _engineService.Settings.SavedClients;
            EmptyHistoryPlaceholder.Visibility = _engineService.Settings.SavedClients.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private async void CopyLocalId_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(LocalIdText.Text) || LocalIdText.Text.Contains("Sincronizando"))
            {
                StatusTextBlock.Text = "Aguarde a sincronização do seu ID antes de copiar.";
                return;
            }

            Clipboard.SetText(LocalIdText.Text);
            StatusTextBlock.Text = string.Format("ID {0} copiado para a área de transferência!", LocalIdText.Text);
            BtnCopyId.Content = "✔ Copiado!";
            await Task.Delay(1800);
            BtnCopyId.Content = "📋 Copiar";
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

        private async void CopyPassword_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TempPasswordText.Text) || TempPasswordText.Text.Contains("."))
            {
                StatusTextBlock.Text = "Aguarde a geração da senha temporária antes de copiar.";
                return;
            }

            Clipboard.SetText(TempPasswordText.Text);
            StatusTextBlock.Text = "Senha temporária copiada para a área de transferência!";
            BtnCopyPwd.Content = "✔ Copiado!";
            await Task.Delay(1800);
            BtnCopyPwd.Content = "Copiar";
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

            if (!_openSessionTabs.Contains(formatted))
            {
                _openSessionTabs.Add(formatted);
            }

            ToolbarRemoteIdBox.Text = formatted;
            MainRemoteIdBox.Text = formatted;

            _engineService.AddOrUpdateRecentClient(formatted);
            RefreshClientsList();

            StatusTextBlock.Text = string.Format("Iniciando conexão com {0} ({1} sessão(ões) na barra)...", formatted, _openSessionTabs.Count);

            if (_engineService.FindExtractedRuntimeExecutable() == null)
            {
                await _engineService.InitializeAndStartServerAsync(msg =>
                    Dispatcher.Invoke(() => StatusTextBlock.Text = msg));
            }

            if (_engineService.LaunchRemoteConnection(formatted, mode))
            {
                StatusTextBlock.Text = string.Format("Sessão remota aberta para {0}. Os controles de Monitor 1/2 ficam no topo da janela de vídeo.", formatted);
            }
            else
            {
                StatusTextBlock.Text = "Aguarde a conclusão da inicialização do motor e tente novamente.";
            }
        }

        private void SelectSessionTab_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null && btn.Tag is string id)
            {
                ToolbarRemoteIdBox.Text = id;
                MainRemoteIdBox.Text = id;

                // Se já houver uma janela de sessão aberta, foca nela em vez de abrir outra conexão repetida
                if (_engineService.FocusExistingSessionWindow())
                {
                    StatusTextBlock.Text = string.Format("Janela da sessão ({0}) trazida para frente.", id);
                }
                else
                {
                    InitiateConnection(id, "--connect");
                }
            }
        }

        private void CloseSessionTab_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null && btn.Tag is string id)
            {
                _openSessionTabs.Remove(id);
            }
        }

        private void FocusSessionWindow_Click(object sender, RoutedEventArgs e)
        {
            if (_engineService.FocusExistingSessionWindow())
            {
                StatusTextBlock.Text = "Janela da sessão ativa focada. Use os ícones no topo da janela de vídeo para alternar entre Monitor 1 e 2.";
            }
            else
            {
                StatusTextBlock.Text = "Nenhuma janela de sessão remota aberta no momento. Conecte-se a um ID primeiro.";
                MessageBox.Show(
                    "Nenhuma sessão remota está aberta no momento.\n\n" +
                    "Quando você estiver conectado a um cliente, a barra superior dentro da janela de vídeo exibirá automaticamente os botões [1] e [2] para alternar entre os monitores do cliente em tempo real.",
                    "ColiseuDesk - Controle de Sessão",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void ToggleImageQuality_Click(object sender, RoutedEventArgs e)
        {
            switch (_engineService.Settings.ImageQuality)
            {
                case "best":
                    _engineService.Settings.ImageQuality = "balanced";
                    break;
                case "balanced":
                    _engineService.Settings.ImageQuality = "low";
                    break;
                default:
                    _engineService.Settings.ImageQuality = "best";
                    break;
            }

            _engineService.PersistSettings();
            _engineService.ApplyVpsConfigToEngine();
            UpdateToolbarToggleButtonsUI();
            StatusTextBlock.Text = string.Format("Preferência de imagem ajustada para: {0}", BtnQualityToggle.Content);
        }

        private void ToggleAudio_Click(object sender, RoutedEventArgs e)
        {
            _engineService.Settings.EnableAudio = !_engineService.Settings.EnableAudio;
            _engineService.PersistSettings();
            _engineService.ApplyVpsConfigToEngine();
            UpdateToolbarToggleButtonsUI();
            StatusTextBlock.Text = _engineService.Settings.EnableAudio
                ? "Transmissão de som remoto ativada para as conexões."
                : "Transmissão de som remoto silenciada (Mudo).";
        }

        private void ToggleClipboard_Click(object sender, RoutedEventArgs e)
        {
            _engineService.Settings.EnableClipboard = !_engineService.Settings.EnableClipboard;
            _engineService.PersistSettings();
            _engineService.ApplyVpsConfigToEngine();
            UpdateToolbarToggleButtonsUI();
            StatusTextBlock.Text = _engineService.Settings.EnableClipboard
                ? "Sincronização de Ctrl+C / Ctrl+V ativada."
                : "Sincronização de Ctrl+C / Ctrl+V desativada.";
        }

        private void OpenRecordingsFolder_Click(object sender, RoutedEventArgs e)
        {
            _engineService.OpenRecordingsFolder();
            StatusTextBlock.Text = "Pasta de gravações de sessão aberta no Windows Explorer.";
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

        private void FileTransferSavedClient_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null && btn.Tag is string id)
            {
                InitiateConnection(id, "--file-transfer");
            }
        }

        private void EditSavedClient_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null && btn.Tag is string id)
            {
                var existing = _engineService.Settings.SavedClients.FirstOrDefault(c => c.Id == id);
                ClientModalIdBox.Text = id;
                ClientModalNameBox.Text = existing != null ? existing.Name : string.Format("Terminal {0}", id);
                ClientModalCompanyBox.Text = existing != null ? existing.Company : "Cliente Coliseu Sistemas";
                ClientModalOverlay.Visibility = Visibility.Visible;
                ClientModalNameBox.Focus();
                ClientModalNameBox.SelectAll();
            }
        }

        private void DeleteSavedClient_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null && btn.Tag is string id)
            {
                _engineService.RemoveClient(id);
                RefreshClientsList();
                StatusTextBlock.Text = string.Format("Computador {0} removido da lista de recentes.", id);
            }
        }

        private void AddFavoriteClient_Click(object sender, RoutedEventArgs e)
        {
            var formatted = RustDeskEngineService.FormatId(MainRemoteIdBox.Text);
            var existing = _engineService.Settings.SavedClients.FirstOrDefault(c => c.Id == formatted);

            ClientModalIdBox.Text = formatted;
            ClientModalNameBox.Text = existing != null ? existing.Name : (string.IsNullOrWhiteSpace(formatted) ? "" : string.Format("Terminal {0}", formatted));
            ClientModalCompanyBox.Text = existing != null ? existing.Company : "Cliente Coliseu Sistemas";
            ClientModalOverlay.Visibility = Visibility.Visible;

            if (string.IsNullOrWhiteSpace(formatted))
                ClientModalIdBox.Focus();
            else
            {
                ClientModalNameBox.Focus();
                ClientModalNameBox.SelectAll();
            }
        }

        private void CloseClientModal_Click(object sender, RoutedEventArgs e)
        {
            ClientModalOverlay.Visibility = Visibility.Collapsed;
        }

        private void SaveClientModal_Click(object sender, RoutedEventArgs e)
        {
            var id = RustDeskEngineService.FormatId(ClientModalIdBox.Text);
            if (string.IsNullOrWhiteSpace(id))
            {
                MessageBox.Show("Informe um ID de 9 dígitos válido.", "ColiseuDesk", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _engineService.AddOrUpdateRecentClient(id, ClientModalNameBox.Text, ClientModalCompanyBox.Text);
            RefreshClientsList();
            ClientModalOverlay.Visibility = Visibility.Collapsed;
            StatusTextBlock.Text = string.Format("Cliente {0} ({1}) salvo nos favoritos!", ClientModalNameBox.Text, id);
        }

        private void ClientsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ClientsListBox.SelectedItem is SavedClientItem item)
            {
                InitiateConnection(item.Id, "--connect");
            }
        }

        private void FocusRemoteInput_Click(object sender, RoutedEventArgs e)
        {
            MainRemoteIdBox.Clear();
            MainRemoteIdBox.Focus();
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
