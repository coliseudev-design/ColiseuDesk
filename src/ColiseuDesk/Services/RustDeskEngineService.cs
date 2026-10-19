using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ColiseuDesk.Services
{
    public class SavedClientItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Company { get; set; } = "";
        public string LastConnected { get; set; } = "";
        public string SavedPassword { get; set; } = "";
        public bool IsFavorite { get; set; }
        public string MacAddress { get; set; } = "";
        public bool IsOnline { get; set; } = true;

        public string FavoriteStar => IsFavorite ? "⭐" : "☆";
        public string PasswordBadge => !string.IsNullOrEmpty(SavedPassword) ? "🔑 1-Clique" : "";
        public System.Windows.Visibility HasPasswordVisibility =>
            !string.IsNullOrEmpty(SavedPassword) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public class ColiseuSettings
    {
        public string RendezvousServer { get; set; } = "2.24.82.19";
        public string PublicKey { get; set; } = "1p8++8VM24nZtyCrsIlylHxG4hRS5ZVRf3bkDmHaYg8=";
        public string ApiServer { get; set; } = "";
        public string UnattendedPassword { get; set; } = "";
        public string ImageQuality { get; set; } = "balanced";
        public bool EnableAudio { get; set; } = true;
        public bool EnableClipboard { get; set; } = true;
        public string AccessProfile { get; set; } = "full"; // "full" (Controle Total) ou "viewonly" (Somente Visualização)
        public bool MinimizeToTrayOnClose { get; set; } = true;
        public List<SavedClientItem> SavedClients { get; set; } = new List<SavedClientItem>();
    }

    public class EngineLiveStatus
    {
        public string Id { get; set; } = "";
        public string TemporaryPassword { get; set; } = "";
        public bool IsOnline { get; set; }
    }

    public class RustDeskEngineService
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);

        private const int SW_RESTORE = 9;
        private const uint WM_SETTEXT = 0x000C;
        private const string RegistryRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RegistryAppName = "ColiseuDesk";

        private readonly string _appDataDir;
        private readonly string _settingsFile;
        private readonly string _clientsFile;
        private readonly string _engineDir;
        private readonly string _rustDeskConfigDir;
        private readonly string _extractedRustDeskDir;
        private Process? _serverProcess;

        public ColiseuSettings Settings { get; private set; } = new ColiseuSettings();

        public RustDeskEngineService()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            _appDataDir = Path.Combine(appData, "ColiseuDesk");
            _settingsFile = Path.Combine(_appDataDir, "server.cfg");
            _clientsFile = Path.Combine(_appDataDir, "recent.cfg");
            _engineDir = Path.Combine(_appDataDir, "engine");
            _rustDeskConfigDir = Path.Combine(appData, "RustDesk", "config");
            _extractedRustDeskDir = Path.Combine(localAppData, "rustdesk");

            Directory.CreateDirectory(_appDataDir);
            Directory.CreateDirectory(_engineDir);
            Directory.CreateDirectory(_rustDeskConfigDir);

            LoadSettings();
        }

        private static string EncodeSecret(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
            }
            catch
            {
                return "";
            }
        }

        private static string DecodeSecret(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)) return "";
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            }
            catch
            {
                return "";
            }
        }

        public void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFile))
                {
                    foreach (var line in File.ReadAllLines(_settingsFile, Encoding.UTF8))
                    {
                        var parts = line.Split(new[] { '=' }, 2);
                        if (parts.Length == 2)
                        {
                            var k = parts[0].Trim();
                            var v = parts[1].Trim();
                            if (k == "host" && !string.IsNullOrEmpty(v)) Settings.RendezvousServer = v;
                            if (k == "key" && !string.IsNullOrEmpty(v)) Settings.PublicKey = v;
                            if (k == "api" && !string.IsNullOrEmpty(v)) Settings.ApiServer = v;
                            if (k == "quality" && !string.IsNullOrEmpty(v)) Settings.ImageQuality = v;
                            if (k == "audio") Settings.EnableAudio = v != "0";
                            if (k == "clipboard") Settings.EnableClipboard = v != "0";
                            if (k == "profile" && !string.IsNullOrEmpty(v)) Settings.AccessProfile = v;
                            if (k == "tray") Settings.MinimizeToTrayOnClose = v != "0";
                        }
                    }
                }

                Settings.SavedClients.Clear();
                if (File.Exists(_clientsFile))
                {
                    foreach (var line in File.ReadAllLines(_clientsFile, Encoding.UTF8))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var cols = line.Split('|');
                        if (cols.Length >= 4)
                        {
                            Settings.SavedClients.Add(new SavedClientItem
                            {
                                Id = cols[0],
                                Name = cols[1],
                                Company = cols[2],
                                LastConnected = cols[3],
                                SavedPassword = cols.Length >= 5 ? DecodeSecret(cols[4]) : "",
                                IsFavorite = cols.Length >= 6 && cols[5] == "1",
                                MacAddress = cols.Length >= 7 ? cols[6] : "",
                                IsOnline = true
                            });
                        }
                    }
                    SortSavedClients();
                }
            }
            catch
            {
                // Ignora erros de leitura inicial
            }
        }

        private void SortSavedClients()
        {
            var sorted = Settings.SavedClients
                .OrderByDescending(c => c.IsFavorite)
                .ToList();
            Settings.SavedClients.Clear();
            Settings.SavedClients.AddRange(sorted);
        }

        public void SaveSettings(string rendezvousServer, string publicKey, string apiServer = "")
        {
            if (!string.IsNullOrWhiteSpace(rendezvousServer))
                Settings.RendezvousServer = rendezvousServer.Trim();
            if (!string.IsNullOrWhiteSpace(publicKey))
                Settings.PublicKey = publicKey.Trim();
            Settings.ApiServer = (apiServer ?? "").Trim();

            PersistSettings();
            ApplyVpsConfigToEngine();
        }

        public void AddOrUpdateRecentClient(
            string formattedId,
            string? customName = null,
            string? customCompany = null,
            string? customPassword = null,
            bool? isFavorite = null,
            string? macAddress = null)
        {
            var existing = Settings.SavedClients.Find(c => c.Id == formattedId);
            string name = !string.IsNullOrWhiteSpace(customName)
                ? customName!.Trim()
                : (existing != null ? existing.Name : string.Format("Terminal {0}", formattedId));
            string company = !string.IsNullOrWhiteSpace(customCompany)
                ? customCompany!.Trim()
                : (existing != null ? existing.Company : "Cliente Coliseu Sistemas");
            string pwd = customPassword != null
                ? customPassword
                : (existing != null ? existing.SavedPassword : "");
            bool fav = isFavorite.HasValue
                ? isFavorite.Value
                : (existing != null && existing.IsFavorite);
            string mac = macAddress != null
                ? macAddress.Trim()
                : (existing != null ? existing.MacAddress : "");

            Settings.SavedClients.RemoveAll(c => c.Id == formattedId);
            Settings.SavedClients.Insert(0, new SavedClientItem
            {
                Id = formattedId,
                Name = name,
                Company = company,
                LastConnected = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                SavedPassword = pwd,
                IsFavorite = fav,
                MacAddress = mac,
                IsOnline = true
            });
            SortSavedClients();
            PersistSettings();
        }

        public void ToggleFavoriteClient(string formattedId)
        {
            var existing = Settings.SavedClients.Find(c => c.Id == formattedId);
            if (existing != null)
            {
                existing.IsFavorite = !existing.IsFavorite;
                SortSavedClients();
                PersistSettings();
            }
        }

        public void RemoveClient(string formattedId)
        {
            Settings.SavedClients.RemoveAll(c => c.Id == formattedId);
            PersistSettings();
        }

        public int ExportClientsToFile(string filePath)
        {
            var cb = new StringBuilder();
            foreach (var c in Settings.SavedClients)
            {
                cb.AppendLine(string.Format("{0}|{1}|{2}|{3}|{4}|{5}|{6}",
                    c.Id,
                    c.Name.Replace("|", "/"),
                    c.Company.Replace("|", "/"),
                    c.LastConnected,
                    EncodeSecret(c.SavedPassword),
                    c.IsFavorite ? "1" : "0",
                    (c.MacAddress ?? "").Replace("|", "")));
            }
            File.WriteAllText(filePath, cb.ToString(), Encoding.UTF8);
            return Settings.SavedClients.Count;
        }

        public int ImportClientsFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return 0;
            int imported = 0;
            foreach (var line in File.ReadAllLines(filePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cols = line.Split('|');
                if (cols.Length >= 3)
                {
                    var id = FormatId(cols[0]);
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    Settings.SavedClients.RemoveAll(c => c.Id == id);
                    Settings.SavedClients.Add(new SavedClientItem
                    {
                        Id = id,
                        Name = cols[1],
                        Company = cols[2],
                        LastConnected = cols.Length >= 4 ? cols[3] : DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                        SavedPassword = cols.Length >= 5 ? DecodeSecret(cols[4]) : "",
                        IsFavorite = cols.Length >= 6 && cols[5] == "1",
                        MacAddress = cols.Length >= 7 ? cols[6] : "",
                        IsOnline = true
                    });
                    imported++;
                }
            }
            SortSavedClients();
            PersistSettings();
            return imported;
        }

        public void PersistSettings()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("host=" + Settings.RendezvousServer);
                sb.AppendLine("key=" + Settings.PublicKey);
                sb.AppendLine("api=" + Settings.ApiServer);
                sb.AppendLine("quality=" + Settings.ImageQuality);
                sb.AppendLine("audio=" + (Settings.EnableAudio ? "1" : "0"));
                sb.AppendLine("clipboard=" + (Settings.EnableClipboard ? "1" : "0"));
                sb.AppendLine("profile=" + Settings.AccessProfile);
                sb.AppendLine("tray=" + (Settings.MinimizeToTrayOnClose ? "1" : "0"));
                File.WriteAllText(_settingsFile, sb.ToString(), Encoding.UTF8);

                var cb = new StringBuilder();
                foreach (var c in Settings.SavedClients)
                {
                    cb.AppendLine(string.Format("{0}|{1}|{2}|{3}|{4}|{5}|{6}",
                        c.Id,
                        c.Name.Replace("|", "/"),
                        c.Company.Replace("|", "/"),
                        c.LastConnected,
                        EncodeSecret(c.SavedPassword),
                        c.IsFavorite ? "1" : "0",
                        (c.MacAddress ?? "").Replace("|", "")));
                }
                File.WriteAllText(_clientsFile, cb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Ignora falha de escrita
            }
        }

        /// <summary>
        /// Verifica se o ColiseuDesk está configurado para iniciar junto com o Windows.
        /// </summary>
        public bool IsStartWithWindowsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryRunKeyPath, false))
                {
                    var val = key?.GetValue(RegistryAppName) as string;
                    return !string.IsNullOrEmpty(val);
                }
            }
            catch
            {
                return false;
            }
        }

        public bool SetStartWithWindows(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryRunKeyPath, true))
                {
                    if (key == null) return false;
                    if (enable)
                    {
                        var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            key.SetValue(RegistryAppName, string.Format("\"{0}\"", exePath));
                        }
                    }
                    else
                    {
                        key.DeleteValue(RegistryAppName, false);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool IsRunningAsAdmin()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Ativa o Modo Serviço / Administrador para permitir controle de UAC e tela de login do Windows.
        /// </summary>
        public bool InstallOrElevateService()
        {
            try
            {
                var engine = FindExtractedRuntimeExecutable() ?? FindEnginePackageExecutable();
                if (engine != null)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = engine,
                        Arguments = "--install-service",
                        WorkingDirectory = Path.GetDirectoryName(engine) ?? _extractedRustDeskDir,
                        Verb = "runas",
                        UseShellExecute = true
                    });
                    return true;
                }
            }
            catch
            {
                // Usuário cancelou o UAC ou falha
            }
            return false;
        }

        /// <summary>
        /// Envia pacote mágico Wake-on-LAN (UDP broadcast portas 9 e 7) para ligar computador na rede local.
        /// </summary>
        public bool SendWakeOnLan(string macAddress)
        {
            try
            {
                var cleanHex = Regex.Replace(macAddress ?? "", "[^0-9A-Fa-f]", "");
                if (cleanHex.Length != 12)
                    return false;

                byte[] macBytes = new byte[6];
                for (int i = 0; i < 6; i++)
                {
                    macBytes[i] = Convert.ToByte(cleanHex.Substring(i * 2, 2), 16);
                }

                byte[] packet = new byte[6 + 16 * 6];
                for (int i = 0; i < 6; i++)
                    packet[i] = 0xFF;
                for (int i = 1; i <= 16; i++)
                    Buffer.BlockCopy(macBytes, 0, packet, i * 6, 6);

                using (var client = new UdpClient())
                {
                    client.EnableBroadcast = true;
                    client.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 9));
                    client.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 7));
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public string GetLocalLanIpAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        return ip.ToString();
                    }
                }
            }
            catch
            {
                // Ignora
            }
            return "127.0.0.1";
        }

        public static string FormatWhatsAppInvite(string id, string password)
        {
            return string.Format(
                "🖥️ *Suporte Remoto — Coliseu Sistemas*\r\n" +
                "• *ID ColiseuDesk:* {0}\r\n" +
                "• *Senha da Sessão:* {1}",
                id,
                password);
        }

        /// <summary>
        /// Grava as credenciais do servidor Coliseu Sistemas e as preferências de sessão
        /// nos arquivos de configuração do motor + IPC ao vivo.
        /// </summary>
        /// <summary>
        /// Grava as credenciais do servidor Coliseu Sistemas e aplica otimizações de Ultra-Baixa Latência
        /// (60 FPS, H.264 GPU Hardware, DirectX VRAM Capture, P2P Direto sem Relay forçado e Cursor Local 0ms)
        /// nos arquivos globais e individuais de cada máquina remota (peers\&lt;ID&gt;.toml).
        /// </summary>
        public void ApplyVpsConfigToEngine()
        {
            if (string.IsNullOrWhiteSpace(Settings.RendezvousServer))
                return;

            try
            {
                Directory.CreateDirectory(_rustDeskConfigDir);

                var host = Settings.RendezvousServer.Trim();
                var key = Settings.PublicKey.Trim();
                var api = Settings.ApiServer.Trim();
                bool isFullAccess = Settings.AccessProfile != "viewonly";

                var sb = new StringBuilder();
                sb.AppendLine(string.Format("rendezvous_server = '{0}:21116'", host));
                sb.AppendLine("nat_type = 1");
                sb.AppendLine("serial = 0");
                sb.AppendLine();
                sb.AppendLine("[options]");
                sb.AppendLine(string.Format("custom-rendezvous-server = '{0}'", host));
                sb.AppendLine(string.Format("relay-server = '{0}'", host));
                if (!string.IsNullOrEmpty(key))
                    sb.AppendLine(string.Format("key = '{0}'", key));
                if (!string.IsNullOrEmpty(api))
                    sb.AppendLine(string.Format("api-server = '{0}'", api));
                sb.AppendLine("direct-server = 'Y'");
                sb.AppendLine("av1-test = 'N'");
                sb.AppendLine("enable-hwcodec = 'Y'");
                sb.AppendLine("enable-directx-capture = 'Y'");
                sb.AppendLine("codec-preference = 'h264'");
                sb.AppendLine(string.Format("enable-keyboard = '{0}'", isFullAccess ? "Y" : "N"));
                sb.AppendLine(string.Format("enable-file-transfer = '{0}'", isFullAccess ? "Y" : "N"));
                sb.AppendLine(string.Format("enable-audio = '{0}'", Settings.EnableAudio ? "Y" : "N"));
                sb.AppendLine(string.Format("enable-clipboard = '{0}'", (isFullAccess && Settings.EnableClipboard) ? "Y" : "N"));

                var tomlContent = sb.ToString();

                // Grava no perfil do usuário atual E nos perfis de Serviço do Windows (LocalService / SystemProfile)
                var configDirs = new[]
                {
                    _rustDeskConfigDir,
                    @"C:\Windows\ServiceProfiles\LocalService\AppData\Roaming\RustDesk\config",
                    @"C:\Windows\System32\config\systemprofile\AppData\Roaming\RustDesk\config"
                };

                foreach (var dir in configDirs)
                {
                    try
                    {
                        Directory.CreateDirectory(dir);
                        File.WriteAllText(Path.Combine(dir, "RustDesk2.toml"), tomlContent, Encoding.UTF8);
                        ConfirmVpsKeyInRustDeskToml(Path.Combine(dir, "RustDesk.toml"), host);
                    }
                    catch
                    {
                        // Ignora diretórios de serviço quando executado sem elevação Admin
                    }
                }

                // Otimiza peers.toml global
                var peersTomlPath = Path.Combine(_rustDeskConfigDir, "peers.toml");
                if (File.Exists(peersTomlPath))
                {
                    OptimizePeerTomlFile(peersTomlPath);
                }

                // Otimiza todos os arquivos individuais em config\peers\<ID>.toml (onde o motor lê cada sessão)
                var peersSubDir = Path.Combine(_rustDeskConfigDir, "peers");
                Directory.CreateDirectory(peersSubDir);
                foreach (var peerFile in Directory.GetFiles(peersSubDir, "*.toml"))
                {
                    OptimizePeerTomlFile(peerFile);
                    SyncPeerHostnameToSavedClients(peerFile);
                }

                // Injeta as opções via IPC ao vivo caso o processo --server já esteja rodando em memória
                EnsureVpsOptionsSyncedInLivePipe(force: false);

                ApplyVisualBrandingToExtractedEngine();
                BoostEngineProcessesPriority();
            }
            catch
            {
                // Ignora se estiver em uso
            }
        }

        private static void ConfirmVpsKeyInRustDeskToml(string rustDeskTomlPath, string host)
        {
            try
            {
                if (!File.Exists(rustDeskTomlPath))
                    return;

                var content = File.ReadAllText(rustDeskTomlPath, Encoding.UTF8);
                bool modified = false;

                if (content.Contains("key_confirmed = false"))
                {
                    content = content.Replace("key_confirmed = false", "key_confirmed = true");
                    modified = true;
                }

                var serverEntry = string.Format("\"{0}:21116\" = true", host);
                if (!content.Contains(serverEntry))
                {
                    if (content.Contains("[keys_confirmed]"))
                    {
                        content = content.Replace("[keys_confirmed]", "[keys_confirmed]\r\n" + serverEntry);
                    }
                    else
                    {
                        content += string.Format("\r\n[keys_confirmed]\r\n{0}\r\n", serverEntry);
                    }
                    modified = true;
                }

                if (modified)
                {
                    File.WriteAllText(rustDeskTomlPath, content, Encoding.UTF8);
                }
            }
            catch
            {
                // Ignora se em uso
            }
        }

        public string BuildLiveOptionsJson()
        {
            var host = Settings.RendezvousServer.Trim();
            var key = Settings.PublicKey.Trim();
            bool isFullAccess = Settings.AccessProfile != "viewonly";

            return string.Format(
                "{{\"t\":\"Options\",\"c\":{{" +
                "\"custom-rendezvous-server\":\"{0}\"," +
                "\"relay-server\":\"{0}\"," +
                "\"key\":\"{1}\"," +
                "\"direct-server\":\"Y\"," +
                "\"av1-test\":\"N\"," +
                "\"enable-hwcodec\":\"Y\"," +
                "\"enable-directx-capture\":\"Y\"," +
                "\"codec-preference\":\"h264\"," +
                "\"enable-keyboard\":\"{2}\"," +
                "\"enable-file-transfer\":\"{2}\"," +
                "\"enable-audio\":\"{3}\"," +
                "\"enable-clipboard\":\"{4}\"" +
                "}}}}",
                host,
                key,
                isFullAccess ? "Y" : "N",
                Settings.EnableAudio ? "Y" : "N",
                (isFullAccess && Settings.EnableClipboard) ? "Y" : "N");
        }

        /// <summary>
        /// Verifica se o processo --server em execução já está usando o servidor Coliseu (2.24.82.19) em memória.
        /// Caso não esteja (ou se force=true), injeta as opções via Named Pipe para forçar reconexão imediata ao hbbs da VPS.
        /// </summary>
        public bool EnsureVpsOptionsSyncedInLivePipe(bool force = false)
        {
            try
            {
                var host = Settings.RendezvousServer.Trim();
                if (string.IsNullOrEmpty(host))
                    return false;

                if (!force)
                {
                    var currentOpts = SendIpcQuery("{\"t\":\"Options\",\"c\":null}", 500);
                    if (!string.IsNullOrEmpty(currentOpts) &&
                        currentOpts.Contains(string.Format("\"custom-rendezvous-server\":\"{0}\"", host)) &&
                        currentOpts.Contains("\"av1-test\":\"N\"") &&
                        currentOpts.Contains("\"enable-hwcodec\":\"Y\""))
                    {
                        return true;
                    }
                }

                return SendIpcCommand(BuildLiveOptionsJson(), 600);
            }
            catch
            {
                return false;
            }
        }

        private int GetTargetBitratePercent()
        {
            switch (Settings.ImageQuality)
            {
                case "low":
                    return 45; // Modo Rápido: menor tamanho de pacote + 60 FPS para resposta instantânea do mouse
                case "best":
                    return 100; // Modo Alta Nitidez: 100% bitrate + 60 FPS
                default:
                    return 65; // Modo Balanceado: nitidez ideal + 60 FPS
            }
        }

        public void EnsureOptimizedPeerConfig(string cleanId)
        {
            if (string.IsNullOrWhiteSpace(cleanId))
                return;

            try
            {
                var peersSubDir = Path.Combine(_rustDeskConfigDir, "peers");
                Directory.CreateDirectory(peersSubDir);
                var peerPath = Path.Combine(peersSubDir, cleanId + ".toml");

                if (!File.Exists(peerPath))
                {
                    int bitrate = GetTargetBitratePercent();
                    var sb = new StringBuilder();
                    sb.AppendLine("view_style = 'original'");
                    sb.AppendLine("scroll_style = 'scrollauto'");
                    sb.AppendLine("image_quality = 'custom'");
                    sb.AppendLine(string.Format("custom_image_quality = [{0}]", bitrate));
                    sb.AppendLine("show_remote_cursor = false");
                    sb.AppendLine("lock_after_session_end = false");
                    sb.AppendLine("privacy_mode = false");
                    sb.AppendLine("direct_failures = 0");
                    sb.AppendLine(string.Format("disable_audio = {0}", Settings.EnableAudio ? "false" : "true"));
                    sb.AppendLine(string.Format("disable_clipboard = {0}", Settings.EnableClipboard ? "false" : "true"));
                    sb.AppendLine("enable-file-copy-paste = true");
                    sb.AppendLine("keyboard_mode = 'map'");
                    sb.AppendLine("view_only = false");
                    sb.AppendLine();
                    sb.AppendLine("[options]");
                    sb.AppendLine("custom-fps = '60'");
                    sb.AppendLine("codec-preference = 'h264'");
                    sb.AppendLine("collapse_toolbar = 'N'");
                    sb.AppendLine("show_monitors_toolbar = 'Y'");
                    File.WriteAllText(peerPath, sb.ToString(), Encoding.UTF8);
                }
                else
                {
                    OptimizePeerTomlFile(peerPath);
                }
            }
            catch
            {
                // Ignora falhas de acesso concorrente
            }
        }

        private void OptimizePeerTomlFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return;

                var pContent = File.ReadAllText(filePath, Encoding.UTF8);
                int bitrate = GetTargetBitratePercent();

                // 1. Usa image_quality = 'custom' + custom_image_quality = [bitrate] para destravar 60 FPS reais no motor
                if (Regex.IsMatch(pContent, @"image_quality\s*=\s*'[^']*'"))
                    pContent = Regex.Replace(pContent, @"image_quality\s*=\s*'[^']*'", "image_quality = 'custom'");

                if (Regex.IsMatch(pContent, @"custom_image_quality\s*=\s*\[[^\]]*\]"))
                    pContent = Regex.Replace(pContent, @"custom_image_quality\s*=\s*\[[^\]]*\]", string.Format("custom_image_quality = [{0}]", bitrate));

                // 2. Zera direct_failures para nunca forçar Relay europeu (310ms) quando P2P Direto / UDP (1-20ms) está disponível
                if (Regex.IsMatch(pContent, @"direct_failures\s*=\s*\d+"))
                    pContent = Regex.Replace(pContent, @"direct_failures\s*=\s*\d+", "direct_failures = 0");

                // 3. Cursor de hardware local instantâneo (0ms de atraso visual no ponteiro do mouse)
                if (Regex.IsMatch(pContent, @"show_remote_cursor\s*=\s*(true|false)"))
                    pContent = Regex.Replace(pContent, @"show_remote_cursor\s*=\s*(true|false)", "show_remote_cursor = false");

                // 4. Sincroniza preferências de áudio e área de transferência
                pContent = Regex.Replace(pContent, @"disable_audio\s*=\s*(true|false)", string.Format("disable_audio = {0}", Settings.EnableAudio ? "false" : "true"));
                pContent = Regex.Replace(pContent, @"disable_clipboard\s*=\s*(true|false)", string.Format("disable_clipboard = {0}", Settings.EnableClipboard ? "false" : "true"));

                // 5. Garante [options] com 60 FPS, H.264 via GPU e barra de monitores sempre expandida
                if (!pContent.Contains("[options]"))
                {
                    pContent += "\r\n[options]\r\ncustom-fps = '60'\r\ncodec-preference = 'h264'\r\ncollapse_toolbar = 'N'\r\nshow_monitors_toolbar = 'Y'\r\n";
                }
                else
                {
                    if (Regex.IsMatch(pContent, @"custom-fps\s*=\s*'[^']*'") )
                        pContent = Regex.Replace(pContent, @"custom-fps\s*=\s*'[^']*'", "custom-fps = '60'");
                    else
                        pContent = pContent.Replace("[options]", "[options]\r\ncustom-fps = '60'");

                    if (Regex.IsMatch(pContent, @"codec-preference\s*=\s*'[^']*'") )
                        pContent = Regex.Replace(pContent, @"codec-preference\s*=\s*'[^']*'", "codec-preference = 'h264'");
                    else
                        pContent = pContent.Replace("[options]", "[options]\r\ncodec-preference = 'h264'");

                    if (Regex.IsMatch(pContent, @"collapse_toolbar\s*=\s*'[^']*'") )
                        pContent = Regex.Replace(pContent, @"collapse_toolbar\s*=\s*'[^']*'", "collapse_toolbar = 'N'");
                    else
                        pContent = pContent.Replace("[options]", "[options]\r\ncollapse_toolbar = 'N'");

                    if (Regex.IsMatch(pContent, @"show_monitors_toolbar\s*=\s*'[^']*'") )
                        pContent = Regex.Replace(pContent, @"show_monitors_toolbar\s*=\s*'[^']*'", "show_monitors_toolbar = 'Y'");
                    else
                        pContent = pContent.Replace("[options]", "[options]\r\nshow_monitors_toolbar = 'Y'");
                }

                File.WriteAllText(filePath, pContent, Encoding.UTF8);
            }
            catch
            {
                // Ignora se arquivo estiver bloqueado
            }
        }

        private void SyncPeerHostnameToSavedClients(string peerFilePath)
        {
            try
            {
                var rawId = Path.GetFileNameWithoutExtension(peerFilePath);
                var formattedId = FormatId(rawId);
                if (string.IsNullOrWhiteSpace(formattedId))
                    return;

                var existing = Settings.SavedClients.Find(c => c.Id == formattedId);
                if (existing == null || (!existing.Name.StartsWith("Terminal ") && !existing.Name.StartsWith("Computador ")))
                    return;

                var content = File.ReadAllText(peerFilePath, Encoding.UTF8);
                var hostMatch = Regex.Match(content, @"hostname\s*=\s*'([^']+)'");
                var userMatch = Regex.Match(content, @"username\s*=\s*'([^']+)'");
                if (hostMatch.Success && !string.IsNullOrWhiteSpace(hostMatch.Groups[1].Value))
                {
                    var hostName = hostMatch.Groups[1].Value.Trim().ToUpperInvariant();
                    var userName = userMatch.Success ? userMatch.Groups[1].Value.Trim() : "";
                    existing.Name = !string.IsNullOrWhiteSpace(userName)
                        ? string.Format("{0} ({1})", hostName, userName)
                        : hostName;
                    PersistSettings();
                }
            }
            catch
            {
                // Ignora
            }
        }

        public void BoostEngineProcessesPriority()
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName("rustdesk"))
                {
                    try
                    {
                        if (proc.PriorityClass == ProcessPriorityClass.Normal ||
                            proc.PriorityClass == ProcessPriorityClass.BelowNormal)
                        {
                            proc.PriorityClass = ProcessPriorityClass.AboveNormal;
                        }
                    }
                    catch
                    {
                        // Ignora processos elevados se não tiver permissão
                    }
                }
            }
            catch
            {
                // Ignora
            }
        }

        public void ApplyVisualBrandingToExtractedEngine()
        {
            try
            {
                Directory.CreateDirectory(_rustDeskConfigDir);
                var localTomlPath = Path.Combine(_rustDeskConfigDir, "RustDesk_local.toml");
                if (File.Exists(localTomlPath))
                {
                    var content = File.ReadAllText(localTomlPath, Encoding.UTF8);
                    if (Regex.IsMatch(content, @"^\s*theme\s*=\s*'[^']*'", RegexOptions.Multiline))
                    {
                        content = Regex.Replace(content, @"^\s*theme\s*=\s*'[^']*'", "theme = 'light'", RegexOptions.Multiline);
                    }
                    else if (content.Contains("[options]"))
                    {
                        content = content.Replace("[options]", "[options]\r\ntheme = 'light'");
                    }
                    else
                    {
                        content += "\r\n[options]\r\ntheme = 'light'\r\n";
                    }
                    File.WriteAllText(localTomlPath, content, Encoding.UTF8);
                }
                else
                {
                    File.WriteAllText(localTomlPath, "[options]\r\ntheme = 'light'\r\n", Encoding.UTF8);
                }

                var flutterIconSvg = Path.Combine(_extractedRustDeskDir, "data", "flutter_assets", "assets", "icon.svg");
                if (Directory.Exists(Path.GetDirectoryName(flutterIconSvg)))
                {
                    const string coliseuSvg = @"<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""0 0 256 256""><rect width=""256"" height=""256"" rx=""36"" fill=""#FFFFFF""/><path d=""M 28 84 Q 82 54 132 62 L 154 96 Q 92 86 30 112 Z"" fill=""#0072BF""/><path d=""M 32 134 Q 98 112 168 120 L 188 154 Q 108 144 35 164 Z"" fill=""#0072BF""/><path d=""M 36 184 Q 116 168 202 176 L 218 208 Q 124 202 38 214 Z"" fill=""#0072BF""/><path d=""M 150 68 Q 194 66 228 88 L 228 118 Q 194 102 170 100 Z"" fill=""#0094D2""/><path d=""M 202 168 Q 218 172 230 180 L 230 206 L 220 206 Z"" fill=""#0094D2""/></svg>";
                    File.WriteAllText(flutterIconSvg, coliseuSvg, Encoding.UTF8);
                }

                PatchLibRustDeskStrings();
            }
            catch
            {
                // Ignora se o arquivo estiver em uso
            }
        }

        public void PatchLibRustDeskStrings()
        {
            try
            {
                var dllPath = Path.Combine(_extractedRustDeskDir, "librustdesk.dll");
                if (!File.Exists(dllPath))
                    return;

                var stampFile = Path.Combine(_appDataDir, "dll_brand_v12.stamp");
                var currentTicks = File.GetLastWriteTimeUtc(dllPath).Ticks.ToString();
                if (File.Exists(stampFile) && File.ReadAllText(stampFile).Trim() == currentTicks)
                    return;

                byte[] bytes = File.ReadAllBytes(dllPath);
                bool modified = false;

                modified |= ReplaceExactBytes(
                    bytes,
                    Encoding.UTF8.GetBytes("Verifique a senha do RustDesk"),
                    Encoding.UTF8.GetBytes("Digite a senha do ColiseuDesk"));

                modified |= ReplaceExactBytes(
                    bytes,
                    Encoding.UTF8.GetBytes("Conectando à rede do RustDesk..."),
                    Encoding.UTF8.GetBytes("Conectando à rede ColiseuDesk..."));

                modified |= ReplaceExactBytes(
                    bytes,
                    Encoding.UTF8.GetBytes("https://api.rustdesk.com/version/latest"),
                    Encoding.UTF8.GetBytes("http://127.0.0.1:9/coliseu/ver/latest  "));

                if (modified)
                {
                    try
                    {
                        File.WriteAllBytes(dllPath, bytes);
                    }
                    catch (IOException)
                    {
                        foreach (var proc in Process.GetProcessesByName("rustdesk"))
                        {
                            try { proc.Kill(); proc.WaitForExit(1500); } catch { }
                        }
                        Thread.Sleep(300);
                        File.WriteAllBytes(dllPath, bytes);
                    }
                }

                File.WriteAllText(stampFile, File.GetLastWriteTimeUtc(dllPath).Ticks.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Ignora caso não tenha permissão de escrita
            }
        }

        private static bool ReplaceExactBytes(byte[] buffer, byte[] pattern, byte[] replacement)
        {
            if (pattern.Length != replacement.Length || pattern.Length == 0)
                return false;

            bool changed = false;
            for (int i = 0; i <= buffer.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (buffer[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    Buffer.BlockCopy(replacement, 0, buffer, i, replacement.Length);
                    changed = true;
                }
            }
            return changed;
        }

        public string? FindExtractedRuntimeExecutable()
        {
            var extracted = Path.Combine(_extractedRustDeskDir, "rustdesk.exe");
            if (File.Exists(extracted))
                return extracted;

            var progFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RustDesk", "rustdesk.exe");
            if (File.Exists(progFiles))
                return progFiles;

            return null;
        }

        public string? FindEnginePackageExecutable()
        {
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            var candidates = new[]
            {
                Path.Combine(_extractedRustDeskDir, "rustdesk.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine", "coliseu-engine.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine", "rustdesk.exe"),
                Path.Combine(_engineDir, "coliseu-engine.exe"),
                Path.Combine(_engineDir, "rustdesk.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RustDesk", "rustdesk.exe")
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path) && !string.Equals(path, currentExe, StringComparison.OrdinalIgnoreCase))
                {
                    var info = new FileInfo(path);
                    if (info.Length > 500000)
                        return path;
                }
            }

            return null;
        }

        public Task<bool> InitializeAndStartServerAsync(Action<string>? onProgress = null)
        {
            return Task.Run(() =>
            {
                try
                {
                    ApplyVpsConfigToEngine();

                    var live = QueryLiveStatusFromPipe();
                    if (!string.IsNullOrEmpty(live.Id))
                    {
                        EnsureVpsOptionsSyncedInLivePipe(force: !live.IsOnline);
                        SendIpcCommand("{\"t\":\"Theme\",\"c\":\"light\"}");
                        if (live.IsOnline)
                        {
                            onProgress?.Invoke("Motor ColiseuDesk ativo e sincronizado.");
                            return true;
                        }

                        // Aguarda até 3.2s para ver se a injeção via IPC colocou o motor Online na VPS
                        for (int w = 0; w < 8; w++)
                        {
                            Thread.Sleep(400);
                            live = QueryLiveStatusFromPipe();
                            if (live.IsOnline)
                            {
                                onProgress?.Invoke("Pronto para conexões (Rede Coliseu Sistemas Online)");
                                return true;
                            }
                        }

                        // Se continuou Offline (processo antigo travado sem a configuração da VPS), reinicia o motor limpo
                        onProgress?.Invoke("Sincronizando motor com o servidor Coliseu Sistemas...");
                        foreach (var proc in Process.GetProcessesByName("rustdesk"))
                        {
                            try { proc.Kill(); proc.WaitForExit(1500); } catch { }
                        }
                        Thread.Sleep(300);
                        ApplyVpsConfigToEngine();
                    }

                    var runtimeExe = FindExtractedRuntimeExecutable();
                    if (runtimeExe == null)
                    {
                        var pkgExe = FindEnginePackageExecutable();
                        if (pkgExe == null)
                        {
                            onProgress?.Invoke("Preparando motor de acesso remoto (somente no 1º uso)...");
                            var targetPkg = Path.Combine(_engineDir, "coliseu-engine.exe");
                            using (var wc = new WebClient())
                            {
                                wc.Headers.Add("User-Agent", "ColiseuDesk/1.3");
                                wc.DownloadFile(
                                    "https://github.com/rustdesk/rustdesk/releases/download/1.3.8/rustdesk-1.3.8-x86_64.exe",
                                    targetPkg);
                            }
                            pkgExe = targetPkg;
                        }

                        onProgress?.Invoke("Inicializando serviço de recepção ColiseuDesk...");
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = pkgExe,
                            Arguments = "--server",
                            WorkingDirectory = Path.GetDirectoryName(pkgExe) ?? _engineDir,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            UseShellExecute = false
                        });

                        for (int i = 0; i < 30; i++)
                        {
                            Thread.Sleep(500);
                            runtimeExe = FindExtractedRuntimeExecutable();
                            if (runtimeExe != null)
                                break;
                        }
                    }

                    // Reaplica a configuração da VPS após a extração inicial e injeta via IPC
                    ApplyVpsConfigToEngine();

                    if (runtimeExe != null)
                    {
                        var checkPipe = QueryLiveStatusFromPipe();
                        if (string.IsNullOrEmpty(checkPipe.Id))
                        {
                            _serverProcess = Process.Start(new ProcessStartInfo
                            {
                                FileName = runtimeExe,
                                Arguments = "--server",
                                WorkingDirectory = Path.GetDirectoryName(runtimeExe) ?? _extractedRustDeskDir,
                                CreateNoWindow = true,
                                WindowStyle = ProcessWindowStyle.Hidden,
                                UseShellExecute = false
                            });
                        }
                    }

                    for (int i = 0; i < 25; i++)
                    {
                        var status = QueryLiveStatusFromPipe();
                        if (!string.IsNullOrEmpty(status.Id))
                        {
                            if (i == 0 || i == 5 || i == 12)
                            {
                                EnsureVpsOptionsSyncedInLivePipe(force: true);
                            }
                            SendIpcCommand("{\"t\":\"Theme\",\"c\":\"light\"}");
                            if (status.IsOnline)
                            {
                                onProgress?.Invoke("Pronto para conexões (Rede Coliseu Sistemas Online)");
                                return true;
                            }
                        }
                        Thread.Sleep(400);
                    }

                    EnsureVpsOptionsSyncedInLivePipe(force: true);
                    return true;
                }
                catch
                {
                    onProgress?.Invoke("Modo de conexão pronto.");
                    return false;
                }
            });
        }

        private readonly object _ipcLock = new object();

        private bool SendIpcCommand(string jsonCommand, int timeoutMs = 400)
        {
            lock (_ipcLock)
            {
                try
                {
                    using (var pipe = new NamedPipeClientStream(".", @"RustDesk\query", PipeDirection.InOut, PipeOptions.None))
                    {
                        pipe.Connect(timeoutMs);
                        byte[] payload = Encoding.UTF8.GetBytes(jsonCommand);
                        int len = payload.Length;

                        byte[] hdr = len <= 63
                            ? new byte[] { (byte)(len << 2) }
                            : BitConverter.GetBytes((ushort)((len << 2) | 1));

                        pipe.Write(hdr, 0, hdr.Length);
                        pipe.Write(payload, 0, payload.Length);
                        pipe.Flush();
                        return true;
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        private string? SendIpcQuery(string jsonRequest, int timeoutMs = 800)
        {
            lock (_ipcLock)
            {
                try
                {
                    using (var pipe = new NamedPipeClientStream(".", @"RustDesk\query", PipeDirection.InOut, PipeOptions.None))
                    {
                        pipe.Connect(timeoutMs);
                        byte[] payload = Encoding.UTF8.GetBytes(jsonRequest);
                        int len = payload.Length;

                        byte[] hdr;
                        if (len <= 63)
                        {
                            hdr = new byte[] { (byte)(len << 2) };
                        }
                        else
                        {
                            ushort v = (ushort)((len << 2) | 1);
                            hdr = BitConverter.GetBytes(v);
                        }

                        pipe.Write(hdr, 0, hdr.Length);
                        pipe.Write(payload, 0, payload.Length);
                        pipe.Flush();

                        int b0 = pipe.ReadByte();
                        if (b0 < 0) return null;

                        int tag = b0 & 3;
                        int rlen = 0;
                        if (tag == 0)
                        {
                            rlen = b0 >> 2;
                        }
                        else if (tag == 1)
                        {
                            int b1 = pipe.ReadByte();
                            if (b1 < 0) return null;
                            rlen = ((b0 | (b1 << 8)) >> 2);
                        }
                        else
                        {
                            return null;
                        }

                        byte[] buf = new byte[rlen];
                        int read = 0;
                        while (read < rlen)
                        {
                            int n = pipe.Read(buf, read, rlen - read);
                            if (n <= 0) break;
                            read += n;
                        }

                        return Encoding.UTF8.GetString(buf, 0, read);
                    }
                }
                catch
                {
                    return null;
                }
            }
        }

        public EngineLiveStatus QueryLiveStatusFromPipe()
        {
            var result = new EngineLiveStatus();

            var idResp = SendIpcQuery("{\"t\":\"Config\",\"c\":[\"id\",null]}");
            if (!string.IsNullOrEmpty(idResp))
            {
                var m = Regex.Match(idResp, "\"id\"\\s*,\\s*\"(\\d+)\"");
                if (m.Success)
                {
                    result.Id = FormatId(m.Groups[1].Value);
                }
            }

            var pwdResp = SendIpcQuery("{\"t\":\"Config\",\"c\":[\"temporary-password\",null]}");
            if (!string.IsNullOrEmpty(pwdResp))
            {
                var m = Regex.Match(pwdResp, "\"temporary-password\"\\s*,\\s*\"([^\"]+)\"");
                if (m.Success)
                {
                    result.TemporaryPassword = m.Groups[1].Value;
                }
            }

            var onlineResp = SendIpcQuery("{\"t\":\"OnlineStatus\",\"c\":null}");
            if (!string.IsNullOrEmpty(onlineResp))
            {
                var m = Regex.Match(onlineResp, "\\[\\s*(\\d+)\\s*,\\s*(true|false)\\s*\\]");
                if (m.Success)
                {
                    long state = long.Parse(m.Groups[1].Value);
                    result.IsOnline = state > 0;
                }
            }

            return result;
        }

        public string? RegenerateTemporaryPasswordInEngine()
        {
            SendIpcCommand("{\"t\":\"Config\",\"c\":[\"temporary-password\",\"\"]}");
            Thread.Sleep(200);
            var status = QueryLiveStatusFromPipe();
            return string.IsNullOrEmpty(status.TemporaryPassword) ? null : status.TemporaryPassword;
        }

        public bool SetUnattendedPassword(string newPassword)
        {
            Settings.UnattendedPassword = newPassword;
            PersistSettings();

            var escaped = newPassword.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return SendIpcCommand(string.Format("{{\"t\":\"Config\",\"c\":[\"permanent-password\",\"{0}\"]}}", escaped));
        }

        public static string FormatId(string? raw)
        {
            var digits = Regex.Replace(raw ?? "", @"[^\d]", "");
            if (digits.Length == 9)
                return string.Format("{0} {1} {2}", digits.Substring(0, 3), digits.Substring(3, 3), digits.Substring(6, 3));
            return digits;
        }

        public bool FocusExistingSessionWindow()
        {
            try
            {
                BrandOpenSessionWindows();
                foreach (var proc in Process.GetProcessesByName("rustdesk"))
                {
                    if (proc.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(proc.MainWindowHandle);
                        return true;
                    }
                }
            }
            catch
            {
                // Ignora
            }
            return false;
        }

        public int BrandOpenSessionWindows()
        {
            int count = 0;
            try
            {
                foreach (var proc in Process.GetProcessesByName("rustdesk"))
                {
                    if (proc.MainWindowHandle != IntPtr.Zero)
                    {
                        count++;
                        var title = proc.MainWindowTitle ?? "";
                        if (title.IndexOf("RustDesk", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var newTitle = title.Replace("RustDesk", "ColiseuDesk — Coliseu Sistemas");
                            SendMessage(proc.MainWindowHandle, WM_SETTEXT, IntPtr.Zero, newTitle);
                        }
                        else if (string.IsNullOrWhiteSpace(title))
                        {
                            SendMessage(proc.MainWindowHandle, WM_SETTEXT, IntPtr.Zero, "ColiseuDesk — Sessão Remota (Coliseu Sistemas)");
                        }
                    }
                }
            }
            catch
            {
                // Ignora
            }
            return count;
        }

        public void OpenRecordingsFolder()
        {
            try
            {
                var recDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", "RustDesk");
                if (!Directory.Exists(recDir))
                    Directory.CreateDirectory(recDir);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = string.Format("\"{0}\"", recDir),
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignora
            }
        }

        public bool LaunchPortForward(string remoteId, int localPort, int remotePort, string? passwordOverride = null)
        {
            var cleanId = Regex.Replace(remoteId ?? "", @"[^\d]", "");
            if (string.IsNullOrWhiteSpace(cleanId))
                return false;

            ApplyVpsConfigToEngine();
            EnsureOptimizedPeerConfig(cleanId);
            var engine = FindExtractedRuntimeExecutable() ?? FindEnginePackageExecutable();
            if (engine == null)
                return false;

            var formatted = FormatId(remoteId);
            var saved = Settings.SavedClients.Find(c => c.Id == formatted);
            string pwd = !string.IsNullOrEmpty(passwordOverride)
                ? passwordOverride!
                : (saved != null ? saved.SavedPassword : "");

            var args = string.Format("--port-forward {0} {1}:localhost:{2}", cleanId, localPort, remotePort);
            if (!string.IsNullOrEmpty(pwd))
            {
                args += string.Format(" --password \"{0}\"", pwd.Replace("\"", "\\\""));
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = engine,
                    Arguments = args,
                    WorkingDirectory = Path.GetDirectoryName(engine) ?? _extractedRustDeskDir,
                    UseShellExecute = true
                });

                Task.Run(async () =>
                {
                    for (int i = 0; i < 6; i++)
                    {
                        await Task.Delay(500);
                        BrandOpenSessionWindows();
                        BoostEngineProcessesPriority();
                    }
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool LaunchRemoteConnection(string remoteId, string mode = "--connect", string? passwordOverride = null)
        {
            var cleanId = Regex.Replace(remoteId ?? "", @"[^\d]", "");
            if (string.IsNullOrWhiteSpace(cleanId))
                return false;

            ApplyVpsConfigToEngine();
            EnsureOptimizedPeerConfig(cleanId);
            var engine = FindExtractedRuntimeExecutable() ?? FindEnginePackageExecutable();
            if (engine == null)
                return false;

            var formatted = FormatId(remoteId);
            var saved = Settings.SavedClients.Find(c => c.Id == formatted);
            string pwd = !string.IsNullOrEmpty(passwordOverride)
                ? passwordOverride!
                : (saved != null ? saved.SavedPassword : "");

            var args = string.Format("{0} {1}", mode, cleanId);
            if (!string.IsNullOrEmpty(pwd))
            {
                args += string.Format(" --password \"{0}\"", pwd.Replace("\"", "\\\""));
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = engine,
                    Arguments = args,
                    WorkingDirectory = Path.GetDirectoryName(engine) ?? _extractedRustDeskDir,
                    UseShellExecute = true
                });

                Task.Run(async () =>
                {
                    for (int i = 0; i < 6; i++)
                    {
                        await Task.Delay(500);
                        BrandOpenSessionWindows();
                        BoostEngineProcessesPriority();
                    }
                });

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
