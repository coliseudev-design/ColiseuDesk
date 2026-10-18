using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ColiseuDesk.Services;
using Forms = System.Windows.Forms;

namespace ColiseuDesk
{
    public partial class MainWindow : Window
    {
        private readonly RustDeskEngineService _engineService = new RustDeskEngineService();
        private readonly DispatcherTimer _statusTimer = new DispatcherTimer();
        private readonly ObservableCollection<string> _openSessionTabs = new ObservableCollection<string>();
        private Forms.NotifyIcon? _trayIcon;
        private bool _forceExit;

        public MainWindow()
        {
            InitializeComponent();
            SessionTabsControl.ItemsSource = _openSessionTabs;
            Loaded += MainWindow_Loaded;
        }

        private void InitializeSystemTrayIcon()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                System.Drawing.Icon? icon = null;
                if (!string.IsNullOrEmpty(exePath))
                {
                    icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                }

                var menu = new Forms.ContextMenuStrip();
                menu.Items.Add("🖥 Abrir Painel ColiseuDesk", null, (s, e) => RestoreFromTray());
                menu.Items.Add("📋 Copiar Meu ID", null, (s, e) => Dispatcher.Invoke(() => CopyLocalId_Click(this, new RoutedEventArgs())));
                menu.Items.Add("📲 Copiar Convite WhatsApp (ID + Senha)", null, (s, e) => Dispatcher.Invoke(() => CopyWhatsAppInvite_Click(this, new RoutedEventArgs())));
                menu.Items.Add(new Forms.ToolStripSeparator());
                menu.Items.Add("✕ Encerrar ColiseuDesk Completamente", null, (s, e) =>
                {
                    _forceExit = true;
                    if (_trayIcon != null) _trayIcon.Visible = false;
                    Dispatcher.Invoke(() => Application.Current.Shutdown());
                });

                _trayIcon = new Forms.NotifyIcon
                {
                    Icon = icon ?? System.Drawing.SystemIcons.Application,
                    Text = "ColiseuDesk — Coliseu Sistemas (Ativo)",
                    Visible = true,
                    ContextMenuStrip = menu
                };
                _trayIcon.DoubleClick += (s, e) => RestoreFromTray();
            }
            catch
            {
                // Ignora caso o ambiente não suporte bandeja
            }
        }

        private void RestoreFromTray()
        {
            Dispatcher.Invoke(() =>
            {
                Show();
                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;
                Activate();
                Focus();
            });
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!_forceExit && _engineService.Settings.MinimizeToTrayOnClose && _trayIcon != null)
            {
                e.Cancel = true;
                Hide();
                _trayIcon.ShowBalloonTip(
                    2500,
                    "ColiseuDesk Continua Ativo",
                    "O ColiseuDesk permanece em segundo plano perto do relógio para manter o acesso remoto disponível.",
                    Forms.ToolTipIcon.Info);
                return;
            }

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeSystemTrayIcon();
            RefreshClientsList();
            UpdateToolbarAndSecurityControlsUI();

            var lanIp = _engineService.GetLocalLanIpAddress();
            FooterInfoText.Text = string.Format("IP Local (LAN): {0}  •  ColiseuDesk v1.2 Pro • Coliseu Sistemas", lanIp);

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
                if (_trayIcon != null)
                {
                    _trayIcon.Text = string.Format("ColiseuDesk — ID: {0}", status.Id);
                }
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

        private void UpdateToolbarAndSecurityControlsUI()
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

            // Perfil de Permissão de Entrada (Controle Total vs Somente Visualização)
            bool isFull = _engineService.Settings.AccessProfile != "viewonly";
            BtnAccessProfile.Content = isFull
                ? "🔓 Controle Total (Teclado, Mouse e Arquivos)"
                : "👁 Somente Visualização (Bloquear Teclado/Arquivos)";
            BtnAccessProfile.Foreground = new SolidColorBrush(isFull
                ? Color.FromRgb(0, 114, 191)
                : Color.FromRgb(180, 83, 9));

            // Checkboxes de Inicialização e Bandeja
            ChkStartWithWindows.IsChecked = _engineService.IsStartWithWindowsEnabled();
            ChkMinimizeToTray.IsChecked = _engineService.Settings.MinimizeToTrayOnClose;

            // Indicador de Modo Administrador / Serviço
            if (_engineService.IsRunningAsAdmin())
            {
                BtnInstallService.Content = "🛡 Serviço / Admin Ativo";
                BtnInstallService.Background = new SolidColorBrush(Color.FromRgb(232, 245, 233));
                BtnInstallService.BorderBrush = new SolidColorBrush(Color.FromRgb(165, 214, 167));
                BtnInstallService.Foreground = new SolidColorBrush(Color.FromRgb(27, 94, 32));
            }
        }

        private void RefreshClientsList()
        {
            string query = (ClientSearchBox?.Text ?? "").Trim().ToLowerInvariant();
            List<SavedClientItem> filtered;

            if (string.IsNullOrEmpty(query))
            {
                filtered = _engineService.Settings.SavedClients.ToList();
            }
            else
            {
                filtered = _engineService.Settings.SavedClients
                    .Where(c =>
                        (c.Name ?? "").ToLowerInvariant().Contains(query) ||
                        (c.Company ?? "").ToLowerInvariant().Contains(query) ||
                        (c.Id ?? "").ToLowerInvariant().Contains(query) ||
                        (c.Id ?? "").Replace(" ", "").Contains(query.Replace(" ", "")))
                    .ToList();
            }

            ClientsListBox.ItemsSource = null;
            ClientsListBox.ItemsSource = filtered;

            if (ClientCountText != null)
            {
                ClientCountText.Text = string.Format("{0} cliente(s)", filtered.Count);
            }

            EmptyHistoryPlaceholder.Visibility = filtered.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void ClientSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshClientsList();
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

        private async void CopyWhatsAppInvite_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(LocalIdText.Text) || LocalIdText.Text.Contains("Sincronizando"))
            {
                StatusTextBlock.Text = "Aguarde a sincronização do ID e senha antes de copiar o convite.";
                return;
            }

            var text = RustDeskEngineService.FormatWhatsAppInvite(LocalIdText.Text, TempPasswordText.Text);
            Clipboard.SetText(text);
            StatusTextBlock.Text = "Convite formatado (ID + Senha) copiado! Basta dar Ctrl+V no WhatsApp.";
            BtnCopyWhatsApp.Content = "✔ Convite Copiado para o WhatsApp!";
            await Task.Delay(2200);
            BtnCopyWhatsApp.Content = "📲 Copiar ID + Senha para WhatsApp";
        }

        private void ToggleAccessProfile_Click(object sender, RoutedEventArgs e)
        {
            _engineService.Settings.AccessProfile = _engineService.Settings.AccessProfile == "viewonly"
                ? "full"
                : "viewonly";
            _engineService.PersistSettings();
            _engineService.ApplyVpsConfigToEngine();
            UpdateToolbarAndSecurityControlsUI();

            StatusTextBlock.Text = _engineService.Settings.AccessProfile == "full"
                ? "Perfil de entrada alterado para: Controle Total (Teclado, Mouse e Arquivos permitidos)."
                : "Perfil de entrada alterado para: Somente Visualização (Teclado, Mouse e Arquivos bloqueados).";
        }

        private void ChkStartWithWindows_Click(object sender, RoutedEventArgs e)
        {
            bool enable = ChkStartWithWindows.IsChecked == true;
            if (_engineService.SetStartWithWindows(enable))
            {
                StatusTextBlock.Text = enable
                    ? "Inicialização automática junto com o Windows ativada!"
                    : "Inicialização automática com o Windows desativada.";
            }
        }

        private void ChkMinimizeToTray_Click(object sender, RoutedEventArgs e)
        {
            _engineService.Settings.MinimizeToTrayOnClose = ChkMinimizeToTray.IsChecked == true;
            _engineService.PersistSettings();
            StatusTextBlock.Text = _engineService.Settings.MinimizeToTrayOnClose
                ? "Ao fechar no X, o ColiseuDesk continuará ativo ao lado do relógio do Windows."
                : "Ao fechar no X, o aplicativo será encerrado.";
        }

        private void InstallService_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Deseja ativar o Modo Serviço / Administrador do Windows?\n\n" +
                "Vantagens (igual ao AnyDesk instalado):\n" +
                "• Permite visualizar e clicar em janelas de permissão de Administrador (UAC);\n" +
                "• Permite acessar a Tela de Login / Bloqueio do Windows;\n" +
                "• Mantém o serviço de recepção ativo em segundo plano.",
                "ColiseuDesk — Modo Serviço do Windows",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (_engineService.InstallOrElevateService())
                {
                    StatusTextBlock.Text = "Serviço do Windows / Elevação UAC solicitado com sucesso!";
                }
            }
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
                "Senha fixa configurada com sucesso!\nAgora você pode conectar neste computador usando essa senha fixa (ou salvá-la no Catálogo para Login em 1 Clique).",
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

            var saved = _engineService.Settings.SavedClients.FirstOrDefault(c => c.Id == formatted);
            bool hasOneClickPwd = saved != null && !string.IsNullOrEmpty(saved.SavedPassword);

            StatusTextBlock.Text = hasOneClickPwd
                ? string.Format("Autenticando em 1 clique com {0} (Senha Salva)...", formatted)
                : string.Format("Iniciando conexão com {0} ({1} sessão(ões) na barra)...", formatted, _openSessionTabs.Count);

            if (_engineService.FindExtractedRuntimeExecutable() == null)
            {
                await _engineService.InitializeAndStartServerAsync(msg =>
                    Dispatcher.Invoke(() => StatusTextBlock.Text = msg));
            }

            if (_engineService.LaunchRemoteConnection(formatted, mode))
            {
                StatusTextBlock.Text = hasOneClickPwd
                    ? string.Format("Sessão aberta em 1 clique para {0}!", formatted)
                    : string.Format("Sessão remota aberta para {0}. Os controles de Monitor 1/2 ficam no topo da janela de vídeo.", formatted);
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
            UpdateToolbarAndSecurityControlsUI();
            StatusTextBlock.Text = string.Format("Preferência de imagem ajustada para: {0}", BtnQualityToggle.Content);
        }

        private void ToggleAudio_Click(object sender, RoutedEventArgs e)
        {
            _engineService.Settings.EnableAudio = !_engineService.Settings.EnableAudio;
            _engineService.PersistSettings();
            _engineService.ApplyVpsConfigToEngine();
            UpdateToolbarAndSecurityControlsUI();
            StatusTextBlock.Text = _engineService.Settings.EnableAudio
                ? "Transmissão de som remoto ativada para as conexões."
                : "Transmissão de som remoto silenciada (Mudo).";
        }

        private void ToggleClipboard_Click(object sender, RoutedEventArgs e)
        {
            _engineService.Settings.EnableClipboard = !_engineService.Settings.EnableClipboard;
            _engineService.PersistSettings();
            _engineService.ApplyVpsConfigToEngine();
            UpdateToolbarAndSecurityControlsUI();
            StatusTextBlock.Text = _engineService.Settings.EnableClipboard
                ? "Sincronização de Ctrl+C / Ctrl+V ativada."
                : "Sincronização de Ctrl+C / Ctrl+V desativada.";
        }

        private void OpenRecordingsFolder_Click(object sender, RoutedEventArgs e)
        {
            _engineService.OpenRecordingsFolder();
            StatusTextBlock.Text = "Pasta de gravações de sessão aberta no Windows Explorer.";
        }

        // ==================== TÚNEL DE PORTA TCP (BANCO DE DADOS / ERP) ====================
        private void OpenPortForwardModal_Click(object sender, RoutedEventArgs e)
        {
            var id = !string.IsNullOrWhiteSpace(MainRemoteIdBox.Text)
                ? MainRemoteIdBox.Text
                : ToolbarRemoteIdBox.Text;
            TunnelRemoteIdBox.Text = RustDeskEngineService.FormatId(id);
            PortForwardModalOverlay.Visibility = Visibility.Visible;
            TunnelRemoteIdBox.Focus();
        }

        private void PresetPort_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null && btn.Tag is string port)
            {
                TunnelLocalPortBox.Text = port;
                TunnelRemotePortBox.Text = port;
            }
        }

        private void ClosePortForwardModal_Click(object sender, RoutedEventArgs e)
        {
            PortForwardModalOverlay.Visibility = Visibility.Collapsed;
        }

        private void StartPortForward_Click(object sender, RoutedEventArgs e)
        {
            var id = RustDeskEngineService.FormatId(TunnelRemoteIdBox.Text);
            if (string.IsNullOrWhiteSpace(id))
            {
                MessageBox.Show("Informe o ID de 9 dígitos do servidor/cliente.", "ColiseuDesk - Túnel TCP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(TunnelLocalPortBox.Text.Trim(), out int localPort) ||
                !int.TryParse(TunnelRemotePortBox.Text.Trim(), out int remotePort))
            {
                MessageBox.Show("Informe números de porta válidos (ex: 3050, 5432, 1433).", "ColiseuDesk - Túnel TCP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PortForwardModalOverlay.Visibility = Visibility.Collapsed;
            if (_engineService.LaunchPortForward(id, localPort, remotePort))
            {
                StatusTextBlock.Text = string.Format("Túnel TCP iniciado com {0} (localhost:{1} ➔ remoto:{2})!", id, localPort, remotePort);
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

        // ==================== CATÁLOGO DE CLIENTES, FAVORITOS E IMPORTAR/EXPORTAR ====================
        private void ToggleFavoriteClient_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null && btn.Tag is string id)
            {
                _engineService.ToggleFavoriteClient(id);
                RefreshClientsList();
                StatusTextBlock.Text = string.Format("Status de favorito atualizado para {0}.", id);
            }
        }

        private void ExportClients_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new Forms.SaveFileDialog())
            {
                dlg.Title = "Exportar Carteira de Clientes ColiseuDesk";
                dlg.Filter = "Catálogo ColiseuDesk (*.coliseu)|*.coliseu|Todos os arquivos (*.*)|*.*";
                dlg.FileName = string.Format("Clientes-ColiseuDesk-{0:yyyy-MM-dd}.coliseu", DateTime.Now);
                if (dlg.ShowDialog() == Forms.DialogResult.OK)
                {
                    int total = _engineService.ExportClientsToFile(dlg.FileName);
                    StatusTextBlock.Text = string.Format("{0} cliente(s) exportado(s) com sucesso!", total);
                    MessageBox.Show(
                        string.Format("{0} cliente(s) exportado(s) com sucesso para:\n{1}", total, dlg.FileName),
                        "ColiseuDesk - Exportação Concluída",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
        }

        private void ImportClients_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new Forms.OpenFileDialog())
            {
                dlg.Title = "Importar Carteira de Clientes ColiseuDesk";
                dlg.Filter = "Catálogo ColiseuDesk (*.coliseu;*.cfg)|*.coliseu;*.cfg|Todos os arquivos (*.*)|*.*";
                if (dlg.ShowDialog() == Forms.DialogResult.OK)
                {
                    int imported = _engineService.ImportClientsFromFile(dlg.FileName);
                    RefreshClientsList();
                    StatusTextBlock.Text = string.Format("{0} cliente(s) importado(s) para o catálogo!", imported);
                    MessageBox.Show(
                        string.Format("{0} cliente(s) importado(s) com sucesso!", imported),
                        "ColiseuDesk - Importação Concluída",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
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
                OpenClientEditModal(id);
            }
        }

        private void OpenClientEditModal(string formattedId)
        {
            var existing = _engineService.Settings.SavedClients.FirstOrDefault(c => c.Id == formattedId);
            ClientModalIdBox.Text = formattedId;
            ClientModalNameBox.Text = existing != null ? existing.Name : (string.IsNullOrWhiteSpace(formattedId) ? "" : string.Format("Terminal {0}", formattedId));
            ClientModalCompanyBox.Text = existing != null ? existing.Company : "Cliente Coliseu Sistemas";
            ClientModalPasswordBox.Password = existing != null ? existing.SavedPassword : "";
            ClientModalMacBox.Text = existing != null ? existing.MacAddress : "";
            ClientModalFavoriteCheck.IsChecked = existing != null && existing.IsFavorite;

            ClientModalOverlay.Visibility = Visibility.Visible;
            if (string.IsNullOrWhiteSpace(formattedId))
                ClientModalIdBox.Focus();
            else
            {
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
            OpenClientEditModal(formatted);
        }

        private void WakeOnLanModal_Click(object sender, RoutedEventArgs e)
        {
            var mac = ClientModalMacBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(mac))
            {
                MessageBox.Show(
                    "Informe o endereço MAC da placa de rede do computador (ex: 00:1A:2B:3C:4D:5E) para enviar o sinal de ligar pela rede.",
                    "ColiseuDesk — Wake-on-LAN",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (_engineService.SendWakeOnLan(mac))
            {
                StatusTextBlock.Text = string.Format("Pacote Mágico Wake-on-LAN enviado para {0}!", mac);
                MessageBox.Show(
                    string.Format("Sinal Wake-on-LAN enviado com sucesso para o endereço MAC {0} na rede local!", mac),
                    "ColiseuDesk — Wake-on-LAN",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    "Formato de endereço MAC inválido. Use 12 dígitos hexadecimais (ex: 00:1A:2B:3C:4D:5E).",
                    "ColiseuDesk — Wake-on-LAN",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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

            _engineService.AddOrUpdateRecentClient(
                id,
                ClientModalNameBox.Text,
                ClientModalCompanyBox.Text,
                ClientModalPasswordBox.Password,
                ClientModalFavoriteCheck.IsChecked == true,
                ClientModalMacBox.Text);

            RefreshClientsList();
            ClientModalOverlay.Visibility = Visibility.Collapsed;
            StatusTextBlock.Text = string.Format("Cliente {0} ({1}) salvo no catálogo!", ClientModalNameBox.Text, id);
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
