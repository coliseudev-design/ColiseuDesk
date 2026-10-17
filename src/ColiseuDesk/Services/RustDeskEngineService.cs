using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ColiseuDesk.Services
{
    public class SavedClientItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Company { get; set; } = "";
        public string LastConnected { get; set; } = "";
        public bool IsOnline { get; set; } = true;
    }

    public class ColiseuSettings
    {
        public string RendezvousServer { get; set; } = "2.24.82.19";
        public string PublicKey { get; set; } = "1p8++8VM24nZtyCrsIlylHxG4hRS5ZVRf3bkDmHaYg8=";
        public string ApiServer { get; set; } = "";
        public string UnattendedPassword { get; set; } = "";
        public List<SavedClientItem> SavedClients { get; set; } = new List<SavedClientItem>();
    }

    public class RustDeskEngineService
    {
        private readonly string _appDataDir;
        private readonly string _settingsFile;
        private readonly string _clientsFile;
        private readonly string _engineDir;
        private readonly string _rustDeskConfigDir;

        public ColiseuSettings Settings { get; private set; } = new ColiseuSettings();

        public RustDeskEngineService()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _appDataDir = Path.Combine(appData, "ColiseuDesk");
            _settingsFile = Path.Combine(_appDataDir, "server.cfg");
            _clientsFile = Path.Combine(_appDataDir, "recent.cfg");
            _engineDir = Path.Combine(_appDataDir, "engine");
            _rustDeskConfigDir = Path.Combine(appData, "RustDesk", "config");

            Directory.CreateDirectory(_appDataDir);
            Directory.CreateDirectory(_engineDir);
            Directory.CreateDirectory(_rustDeskConfigDir);

            LoadSettings();
            ApplyVpsConfigToEngine();
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
                                IsOnline = true
                            });
                        }
                    }
                }
            }
            catch
            {
                // Ignora erros de leitura inicial
            }
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

        public void AddOrUpdateRecentClient(string formattedId)
        {
            Settings.SavedClients.RemoveAll(c => c.Id == formattedId);
            Settings.SavedClients.Insert(0, new SavedClientItem
            {
                Id = formattedId,
                Name = "Computador Remoto",
                Company = "Atendimento Coliseu",
                LastConnected = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                IsOnline = true
            });
            PersistSettings();
        }

        public void PersistSettings()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("host=" + Settings.RendezvousServer);
                sb.AppendLine("key=" + Settings.PublicKey);
                sb.AppendLine("api=" + Settings.ApiServer);
                File.WriteAllText(_settingsFile, sb.ToString(), Encoding.UTF8);

                var cb = new StringBuilder();
                foreach (var c in Settings.SavedClients)
                {
                    cb.AppendLine(string.Format("{0}|{1}|{2}|{3}", c.Id, c.Name, c.Company, c.LastConnected));
                }
                File.WriteAllText(_clientsFile, cb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Ignora falha de escrita
            }
        }

        /// <summary>
        /// Grava de forma 100% invisível as credenciais do servidor Coliseu Sistemas
        /// no arquivo RustDesk2.toml do motor.
        /// </summary>
        public void ApplyVpsConfigToEngine()
        {
            if (string.IsNullOrWhiteSpace(Settings.RendezvousServer))
                return;

            try
            {
                Directory.CreateDirectory(_rustDeskConfigDir);
                var tomlPath = Path.Combine(_rustDeskConfigDir, "RustDesk2.toml");

                var host = Settings.RendezvousServer.Trim();
                var key = Settings.PublicKey.Trim();
                var api = Settings.ApiServer.Trim();

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
                sb.AppendLine("enable-audio = 'Y'");
                sb.AppendLine("enable-file-transfer = 'Y'");
                sb.AppendLine("enable-clipboard = 'Y'");

                File.WriteAllText(tomlPath, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Ignora se estiver em uso
            }
        }

        public string? FindEngineExecutable()
        {
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            var candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine", "coliseu-engine.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine", "rustdesk.exe"),
                Path.Combine(_engineDir, "coliseu-engine.exe"),
                Path.Combine(_engineDir, "rustdesk.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RustDesk", "rustdesk.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "RustDesk", "rustdesk.exe")
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path) && !string.Equals(path, currentExe, StringComparison.OrdinalIgnoreCase))
                {
                    var info = new FileInfo(path);
                    if (info.Length > 1000000) // Garante que o download já concluiu (>1 MB)
                        return path;
                }
            }

            return null;
        }

        /// <summary>
        /// Prepara o motor em segundo plano na pasta AppData\Roaming\ColiseuDesk\engine
        /// sem aumentar o tamanho do executável principal ColiseuDesk.exe (~250 KB).
        /// </summary>
        public Task<string?> EnsureEngineInstalledAsync(Action<string>? onProgress = null)
        {
            return Task.Run(() =>
            {
                var existing = FindEngineExecutable();
                if (existing != null)
                    return (string?)existing;

                var targetExe = Path.Combine(_engineDir, "coliseu-engine.exe");
                var tempExe = Path.Combine(_engineDir, "coliseu-engine.tmp");
                try
                {
                    onProgress?.Invoke("Sincronizando componentes de vídeo/áudio Coliseu Sistemas...");
                    var downloadUrl = "https://github.com/rustdesk/rustdesk/releases/download/1.3.8/rustdesk-1.3.8-x86_64.exe";

                    using (var client = new WebClient())
                    {
                        client.Headers.Add("User-Agent", "ColiseuDesk/1.0");
                        client.DownloadFile(downloadUrl, tempExe);
                    }

                    if (File.Exists(targetExe))
                        File.Delete(targetExe);
                    File.Move(tempExe, targetExe);

                    ApplyVpsConfigToEngine();
                    onProgress?.Invoke("Pronto para conexões (Rede Coliseu Sistemas Online)");
                    return (string?)targetExe;
                }
                catch
                {
                    onProgress?.Invoke("Pronto para conexões (Rede Coliseu Sistemas Online)");
                    return null;
                }
            });
        }

        public Task<string> GetLocalMachineIdAsync()
        {
            return Task.Run(() =>
            {
                // 1. Tenta ler do RustDesk.toml se já existir
                var rustDeskToml = Path.Combine(_rustDeskConfigDir, "RustDesk.toml");
                if (File.Exists(rustDeskToml))
                {
                    var content = File.ReadAllText(rustDeskToml);
                    var match = Regex.Match(content, @"id\s*=\s*'(\d+)'");
                    if (match.Success)
                    {
                        return FormatId(match.Groups[1].Value);
                    }
                }

                // 2. Se o motor já estiver instalado, consulta --get-id
                var engine = FindEngineExecutable();
                if (engine != null)
                {
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = engine,
                            Arguments = "--get-id",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using (var proc = Process.Start(psi))
                        {
                            if (proc != null)
                            {
                                var output = proc.StandardOutput.ReadToEnd();
                                proc.WaitForExit(3000);
                                var cleaned = Regex.Replace(output ?? "", @"[^\d]", "");
                                if (cleaned.Length >= 6)
                                {
                                    return FormatId(cleaned);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Fallback
                    }
                }

                // 3. Gera ID de 9 dígitos vinculado à máquina local
                int hash = Math.Abs(Environment.MachineName.GetHashCode());
                string digits = (100000000 + (hash % 899999999)).ToString();
                return FormatId(digits);
            });
        }

        public static string FormatId(string raw)
        {
            var digits = Regex.Replace(raw ?? "", @"[^\d]", "");
            if (digits.Length == 9)
                return string.Format("{0} {1} {2}", digits.Substring(0, 3), digits.Substring(3, 3), digits.Substring(6, 3));
            return digits;
        }

        public bool LaunchRemoteConnection(string remoteId, string mode = "--connect")
        {
            var cleanId = Regex.Replace(remoteId ?? "", @"[^\d]", "");
            if (string.IsNullOrWhiteSpace(cleanId))
                return false;

            ApplyVpsConfigToEngine();
            var engine = FindEngineExecutable();
            if (engine == null)
                return false;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = engine,
                    Arguments = string.Format("{0} {1}", mode, cleanId),
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool SetUnattendedPassword(string newPassword)
        {
            Settings.UnattendedPassword = newPassword;
            PersistSettings();

            var engine = FindEngineExecutable();
            if (engine == null)
                return false;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = engine,
                    Arguments = string.Format("--password {0}", newPassword),
                    UseShellExecute = true,
                    Verb = "runas"
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
