using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
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

    public class EngineLiveStatus
    {
        public string Id { get; set; } = "";
        public string TemporaryPassword { get; set; } = "";
        public bool IsOnline { get; set; }
    }

    public class RustDeskEngineService
    {
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

        /// <summary>
        /// Localiza o binário já extraído do motor (LocalAppData\rustdesk\rustdesk.exe)
        /// ou o instalador portátil (AppData\Roaming\ColiseuDesk\engine\coliseu-engine.exe).
        /// </summary>
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

        /// <summary>
        /// Baixa o motor (se necessário), extrai os componentes para LocalAppData\rustdesk
        /// e inicia o servidor em segundo plano (--server) conectado à VPS 2.24.82.19!
        /// </summary>
        public Task<bool> InitializeAndStartServerAsync(Action<string>? onProgress = null)
        {
            return Task.Run(() =>
            {
                try
                {
                    ApplyVpsConfigToEngine();

                    // 1. Se já existe pipe ativo do servidor RustDesk, apenas garante que está conectado à VPS
                    var live = QueryLiveStatusFromPipe();
                    if (!string.IsNullOrEmpty(live.Id))
                    {
                        return true;
                    }

                    // 2. Garante que o pacote do motor existe
                    var pkgExe = FindEnginePackageExecutable();
                    if (pkgExe == null)
                    {
                        onProgress?.Invoke("Baixando motor de acesso remoto Coliseu Sistemas (1ª execução)...");
                        var targetExe = Path.Combine(_engineDir, "coliseu-engine.exe");
                        var tempExe = Path.Combine(_engineDir, "coliseu-engine.tmp");
                        var downloadUrl = "https://github.com/rustdesk/rustdesk/releases/download/1.3.8/rustdesk-1.3.8-x86_64.exe";

                        using (var client = new WebClient())
                        {
                            client.Headers.Add("User-Agent", "ColiseuDesk/1.0");
                            client.DownloadFile(downloadUrl, tempExe);
                        }

                        if (File.Exists(targetExe))
                            File.Delete(targetExe);
                        File.Move(tempExe, targetExe);
                        pkgExe = targetExe;
                    }

                    // 3. Se ainda não extraiu para LocalAppData\rustdesk\rustdesk.exe, executa --get-id uma vez para extrair silenciosamente
                    var extractedExe = FindExtractedRuntimeExecutable();
                    if (extractedExe == null && pkgExe != null)
                    {
                        onProgress?.Invoke("Preparando motor de vídeo e criptografia...");
                        var extractPsi = new ProcessStartInfo
                        {
                            FileName = pkgExe,
                            Arguments = "--get-id",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        using (var p = Process.Start(extractPsi))
                        {
                            if (p != null)
                            {
                                p.StandardOutput.ReadToEnd();
                                p.WaitForExit(15000);
                            }
                        }
                        extractedExe = FindExtractedRuntimeExecutable() ?? pkgExe;
                    }

                    if (extractedExe == null)
                        return false;

                    // 4. Inicia o daemon --server em segundo plano para registrar o ID na VPS (2.24.82.19) e aceitar conexões!
                    onProgress?.Invoke("Conectando e registrando computador na Rede Coliseu Sistemas...");
                    ApplyVpsConfigToEngine();

                    var srvPsi = new ProcessStartInfo
                    {
                        FileName = extractedExe,
                        Arguments = "--server",
                        WorkingDirectory = Path.GetDirectoryName(extractedExe) ?? _extractedRustDeskDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    _serverProcess = Process.Start(srvPsi);

                    // Aguarda o pipe \\.\pipe\RustDesk\query ficar pronto e registrar na VPS (até 8 segundos)
                    for (int i = 0; i < 16; i++)
                    {
                        Thread.Sleep(500);
                        var status = QueryLiveStatusFromPipe();
                        if (!string.IsNullOrEmpty(status.Id) && !string.IsNullOrEmpty(status.TemporaryPassword))
                        {
                            if (status.IsOnline)
                                break;
                        }
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        /// <summary>
        /// Comunica-se diretamente com o Named Pipe \\.\pipe\RustDesk\query do motor em execução
        /// usando o protocolo BytesCodec + JSON nativo do RustDesk.
        /// </summary>
        public string? SendIpcQuery(string jsonRequest, int timeoutMs = 1200)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", @"RustDesk\query", PipeDirection.InOut))
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

        /// <summary>
        /// Consulta em tempo real o ID verdadeiro, a Senha Temporária verdadeira e o Status Online na VPS!
        /// </summary>
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
                // Formato: {"t":"OnlineStatus","c":[196785,true]} onde o primeiro número > 0 indica conectado ao hbbs
                var m = Regex.Match(onlineResp, "\\[\\s*(\\d+)\\s*,\\s*(true|false)\\s*\\]");
                if (m.Success)
                {
                    long state = long.Parse(m.Groups[1].Value);
                    result.IsOnline = state > 0;
                }
            }

            return result;
        }

        /// <summary>
        /// Solicita ao motor RustDesk em execução que gere uma nova senha temporária real.
        /// </summary>
        public string? RegenerateTemporaryPasswordInEngine()
        {
            SendIpcQuery("{\"t\":\"Config\",\"c\":[\"temporary-password\",\"\"]}");
            Thread.Sleep(150);
            var status = QueryLiveStatusFromPipe();
            return string.IsNullOrEmpty(status.TemporaryPassword) ? null : status.TemporaryPassword;
        }

        /// <summary>
        /// Define a Senha Fixa (Acesso Permanente) diretamente no daemon RustDesk em execução via IPC.
        /// </summary>
        public bool SetUnattendedPassword(string newPassword)
        {
            Settings.UnattendedPassword = newPassword;
            PersistSettings();

            var escaped = newPassword.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var resp = SendIpcQuery(string.Format("{{\"t\":\"Config\",\"c\":[\"permanent-password\",\"{0}\"]}}", escaped));
            return resp != null;
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
            var engine = FindExtractedRuntimeExecutable() ?? FindEnginePackageExecutable();
            if (engine == null)
                return false;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = engine,
                    Arguments = string.Format("{0} {1}", mode, cleanId),
                    WorkingDirectory = Path.GetDirectoryName(engine) ?? _extractedRustDeskDir,
                    UseShellExecute = true
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
