using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
        public string RendezvousServer { get; set; } = "";
        public string PublicKey { get; set; } = "";
        public string ApiServer { get; set; } = "";
        public string UnattendedPassword { get; set; } = "";
        public List<SavedClientItem> SavedClients { get; set; } = new();
    }

    public class RustDeskEngineService
    {
        private readonly string _appDataDir;
        private readonly string _settingsFile;
        private readonly string _engineDir;
        private readonly string _rustDeskConfigDir;

        public ColiseuSettings Settings { get; private set; } = new();

        public RustDeskEngineService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _appDataDir = Path.Combine(appData, "ColiseuDesk");
            _settingsFile = Path.Combine(_appDataDir, "settings.json");
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
                    var json = File.ReadAllText(_settingsFile);
                    var loaded = JsonSerializer.Deserialize<ColiseuSettings>(json);
                    if (loaded != null)
                    {
                        Settings = loaded;
                    }
                }

                // Lê valores embutidos do coliseudesk.json se ainda não houver servidor configurado
                var bundledConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "coliseudesk.json");
                if (File.Exists(bundledConfigPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(bundledConfigPath));
                    if (doc.RootElement.TryGetProperty("server", out var serverElem))
                    {
                        var srv = serverElem.TryGetProperty("rendezvous_server", out var r) ? r.GetString() : "";
                        var key = serverElem.TryGetProperty("rs_pub_key", out var k) ? k.GetString() : "";
                        var api = serverElem.TryGetProperty("api_server", out var a) ? a.GetString() : "";

                        if (!string.IsNullOrWhiteSpace(srv) && string.IsNullOrWhiteSpace(Settings.RendezvousServer))
                            Settings.RendezvousServer = srv;
                        if (!string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(Settings.PublicKey))
                            Settings.PublicKey = key;
                        if (!string.IsNullOrWhiteSpace(api) && string.IsNullOrWhiteSpace(Settings.ApiServer))
                            Settings.ApiServer = api;
                    }
                }

                if (Settings.SavedClients.Count == 0)
                {
                    Settings.SavedClients = new List<SavedClientItem>
                    {
                        new SavedClientItem { Id = "302 594 014", Name = "Servidor Principal - Matriz", Company = "Cliente Coliseu #01", LastConnected = "Hoje, 17:45", IsOnline = true },
                        new SavedClientItem { Id = "418 920 115", Name = "PDV Caixa 01 - Frente de Loja", Company = "Supermercado Central", LastConnected = "Hoje, 15:12", IsOnline = true },
                        new SavedClientItem { Id = "789 104 552", Name = "Estação Financeiro / NFe", Company = "Auto Peças Brasil", LastConnected = "Ontem, 11:30", IsOnline = false },
                        new SavedClientItem { Id = "512 883 901", Name = "Servidor Banco de Dados ERP", Company = "Distribuidora Alfa", LastConnected = "14/10/2026", IsOnline = true }
                    };
                }
            }
            catch
            {
                // Ignora erros de leitura inicial
            }
        }

        public void SaveSettings(string rendezvousServer, string publicKey, string apiServer = "")
        {
            Settings.RendezvousServer = rendezvousServer.Trim();
            Settings.PublicKey = publicKey.Trim();
            Settings.ApiServer = apiServer.Trim();

            PersistSettings();
            ApplyVpsConfigToEngine();
        }

        public void PersistSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFile, json, Encoding.UTF8);
            }
            catch
            {
                // Ignora falha de escrita
            }
        }

        /// <summary>
        /// Grava automaticamente o IP da VPS (hbbs/hbbr) e a Chave Pública (id_ed25519.pub)
        /// no arquivo RustDesk2.toml utilizado pelo motor RustDesk.
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
                sb.AppendLine($"rendezvous_server = '{host}:21116'");
                sb.AppendLine("nat_type = 1");
                sb.AppendLine("serial = 0");
                sb.AppendLine();
                sb.AppendLine("[options]");
                sb.AppendLine($"custom-rendezvous-server = '{host}'");
                sb.AppendLine($"relay-server = '{host}'");
                if (!string.IsNullOrEmpty(key))
                    sb.AppendLine($"key = '{key}'");
                if (!string.IsNullOrEmpty(api))
                    sb.AppendLine($"api-server = '{api}'");
                sb.AppendLine("direct-server = 'Y'");
                sb.AppendLine("enable-audio = 'Y'");
                sb.AppendLine("enable-file-transfer = 'Y'");
                sb.AppendLine("enable-clipboard = 'Y'");

                File.WriteAllText(tomlPath, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Ignora se o arquivo estiver bloqueado pelo serviço
            }
        }

        /// <summary>
        /// Localiza o executável do motor RustDesk na máquina ou na pasta do ColiseuDesk.
        /// </summary>
        public string? FindEngineExecutable()
        {
            var candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine", "coliseu-engine.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine", "rustdesk.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rustdesk.exe"),
                Path.Combine(_engineDir, "rustdesk.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RustDesk", "rustdesk.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ColiseuDesk", "ColiseuDesk.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "RustDesk", "rustdesk.exe")
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path) && !string.Equals(path, Process.GetCurrentProcess().MainModule?.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }

            return null;
        }

        /// <summary>
        /// Faz o download automático do motor oficial do RustDesk (Windows x64) caso não esteja presente.
        /// </summary>
        public async Task<string?> EnsureEngineInstalledAsync(Action<string>? onProgress = null)
        {
            var existing = FindEngineExecutable();
            if (existing != null)
                return existing;

            var targetExe = Path.Combine(_engineDir, "rustdesk.exe");
            try
            {
                onProgress?.Invoke("Baixando motor de alta performance (RustDesk Engine x64)...");
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("ColiseuDesk/1.0");

                // Busca a última release oficial do motor RustDesk no GitHub
                var downloadUrl = "https://github.com/rustdesk/rustdesk/releases/download/1.3.8/rustdesk-1.3.8-x86_64.exe";
                using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using var fs = new FileStream(targetExe, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs);

                onProgress?.Invoke("Motor instalado com sucesso!");
                ApplyVpsConfigToEngine();
                return targetExe;
            }
            catch (Exception ex)
            {
                onProgress?.Invoke($"Aviso ao baixar motor: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Obtém o ID de 9 dígitos deste computador diretamente do motor ou gera um ID vinculado à máquina.
        /// </summary>
        public async Task<string> GetLocalMachineIdAsync()
        {
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
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        var output = await proc.StandardOutput.ReadToEndAsync();
                        await proc.WaitForExitAsync();
                        var cleaned = Regex.Replace(output ?? "", @"[^\d]", "");
                        if (cleaned.Length >= 6)
                        {
                            return FormatId(cleaned);
                        }
                    }
                }
                catch
                {
                    // Fallback para o ID de configuração
                }
            }

            // Tenta ler do RustDesk.toml caso já exista
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

            // Gera ID determinístico de 9 dígitos baseado na máquina local até o serviço iniciar
            int hash = Math.Abs(Environment.MachineName.GetHashCode());
            string digits = (100000000 + (hash % 899999999)).ToString();
            return FormatId(digits);
        }

        public static string FormatId(string raw)
        {
            var digits = Regex.Replace(raw ?? "", @"[^\d]", "");
            if (digits.Length == 9)
                return $"{digits.Substring(0, 3)} {digits.Substring(3, 3)} {digits.Substring(6, 3)}";
            return raw ?? "";
        }

        /// <summary>
        /// Inicia uma conexão de controle remoto completa usando o motor RustDesk.
        /// </summary>
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
                    Arguments = $"{mode} {cleanId}",
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Define a senha fixa (Acesso Não Supervisionado) no motor RustDesk.
        /// </summary>
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
                    Arguments = $"--password {newPassword}",
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
