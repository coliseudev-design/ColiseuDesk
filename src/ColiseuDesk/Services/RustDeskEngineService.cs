using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Runtime.InteropServices;
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
        public string ImageQuality { get; set; } = "balanced";
        public bool EnableAudio { get; set; } = true;
        public bool EnableClipboard { get; set; } = true;
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
                            if (k == "quality" && !string.IsNullOrEmpty(v)) Settings.ImageQuality = v;
                            if (k == "audio") Settings.EnableAudio = v != "0";
                            if (k == "clipboard") Settings.EnableClipboard = v != "0";
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

        public void AddOrUpdateRecentClient(string formattedId, string? customName = null, string? customCompany = null)
        {
            var existing = Settings.SavedClients.Find(c => c.Id == formattedId);
            string name = customName ?? (existing != null ? existing.Name : "Estação Cliente");
            string company = customCompany ?? (existing != null ? existing.Company : "Cliente Coliseu Sistemas");

            Settings.SavedClients.RemoveAll(c => c.Id == formattedId);
            Settings.SavedClients.Insert(0, new SavedClientItem
            {
                Id = formattedId,
                Name = name,
                Company = company,
                LastConnected = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                IsOnline = true
            });
            PersistSettings();
        }

        public void RemoveClient(string formattedId)
        {
            Settings.SavedClients.RemoveAll(c => c.Id == formattedId);
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
                sb.AppendLine("quality=" + Settings.ImageQuality);
                sb.AppendLine("audio=" + (Settings.EnableAudio ? "1" : "0"));
                sb.AppendLine("clipboard=" + (Settings.EnableClipboard ? "1" : "0"));
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
        /// Grava as credenciais do servidor Coliseu Sistemas e as preferências de sessão
        /// nos arquivos de configuração do motor.
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
                sb.AppendLine(string.Format("enable-audio = '{0}'", Settings.EnableAudio ? "Y" : "N"));
                sb.AppendLine("enable-file-transfer = 'Y'");
                sb.AppendLine(string.Format("enable-clipboard = '{0}'", Settings.EnableClipboard ? "Y" : "N"));

                File.WriteAllText(tomlPath, sb.ToString(), Encoding.UTF8);

                // Atualiza peers.toml para refletir qualidade de vídeo, áudio, área de transferência e barra sempre visível
                var peersTomlPath = Path.Combine(_rustDeskConfigDir, "peers.toml");
                if (File.Exists(peersTomlPath))
                {
                    var pContent = File.ReadAllText(peersTomlPath, Encoding.UTF8);
                    pContent = Regex.Replace(pContent, @"image_quality\s*=\s*'[^']*'", string.Format("image_quality = '{0}'", Settings.ImageQuality));
                    pContent = Regex.Replace(pContent, @"disable_audio\s*=\s*(true|false)", string.Format("disable_audio = {0}", Settings.EnableAudio ? "false" : "true"));
                    pContent = Regex.Replace(pContent, @"disable_clipboard\s*=\s*(true|false)", string.Format("disable_clipboard = {0}", Settings.EnableClipboard ? "false" : "true"));
                    pContent = Regex.Replace(pContent, @"collapse_toolbar\s*=\s*'[^']*'", "collapse_toolbar = 'N'");
                    File.WriteAllText(peersTomlPath, pContent, Encoding.UTF8);
                }

                ApplyVisualBrandingToExtractedEngine();
            }
            catch
            {
                // Ignora se estiver em uso
            }
        }

        /// <summary>
        /// 1. Força o tema claro (branco predominante) em RustDesk_local.toml
        /// 2. Injeta o icon.svg com fundo branco da Coliseu Sistemas
        /// 3. Substitui strings em português ("Verifique a senha do RustDesk" -> "Digite a senha do ColiseuDesk")
        ///    e desativa o aviso rosa de nova versão em librustdesk.dll!
        /// </summary>
        public void ApplyVisualBrandingToExtractedEngine()
        {
            try
            {
                // 1. Garante que RustDesk_local.toml tenha theme = 'light'
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

                // 2. Substitui o icon.svg da janela de conexão pelo ícone com fundo branco da Coliseu Sistemas
                var flutterIconSvg = Path.Combine(_extractedRustDeskDir, "data", "flutter_assets", "assets", "icon.svg");
                if (Directory.Exists(Path.GetDirectoryName(flutterIconSvg)))
                {
                    const string coliseuSvg = @"<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""0 0 256 256""><rect width=""256"" height=""256"" rx=""36"" fill=""#FFFFFF""/><path d=""M 28 84 Q 82 54 132 62 L 154 96 Q 92 86 30 112 Z"" fill=""#0072BF""/><path d=""M 32 134 Q 98 112 168 120 L 188 154 Q 108 144 35 164 Z"" fill=""#0072BF""/><path d=""M 36 184 Q 116 168 202 176 L 218 208 Q 124 202 38 214 Z"" fill=""#0072BF""/><path d=""M 150 68 Q 194 66 228 88 L 228 118 Q 194 102 170 100 Z"" fill=""#0094D2""/><path d=""M 202 168 Q 218 172 230 180 L 230 206 L 220 206 Z"" fill=""#0094D2""/></svg>";
                    File.WriteAllText(flutterIconSvg, coliseuSvg, Encoding.UTF8);
                }

                // 3. Patch binário seguro (mesmo tamanho exato de bytes) em librustdesk.dll quando não estiver bloqueado
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

                byte[] bytes = File.ReadAllBytes(dllPath);
                bool modified = false;

                // Substituições de tamanho de bytes idêntico em UTF-8:
                // 1) "Verifique a senha do RustDesk" (29 bytes) -> "Digite a senha do ColiseuDesk" (29 bytes)
                modified |= ReplaceExactBytes(
                    bytes,
                    Encoding.UTF8.GetBytes("Verifique a senha do RustDesk"),
                    Encoding.UTF8.GetBytes("Digite a senha do ColiseuDesk"));

                // 2) "Conectando à rede do RustDesk..." (33 bytes) -> "Conectando à rede ColiseuDesk..." (33 bytes)
                modified |= ReplaceExactBytes(
                    bytes,
                    Encoding.UTF8.GetBytes("Conectando à rede do RustDesk..."),
                    Encoding.UTF8.GetBytes("Conectando à rede ColiseuDesk..."));

                // 3) Desativa aviso rosa de nova versão ("https://api.rustdesk.com/version/latest" - 39 bytes)
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
                        // Se a DLL ainda não tinha sido atualizada e está em uso por um processo anterior,
                        // reiniciamos brevemente o processo para aplicar a identidade visual da Coliseu Sistemas.
                        foreach (var proc in Process.GetProcessesByName("rustdesk"))
                        {
                            try { proc.Kill(); proc.WaitForExit(1500); } catch { }
                        }
                        Thread.Sleep(300);
                        File.WriteAllBytes(dllPath, bytes);
                    }
                }
            }
            catch
            {
                // Ignora caso não tenha permissão de escrita no diretório
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
                        SendIpcQuery("{\"t\":\"Theme\",\"c\":\"light\"}");
                        return true;
                    }

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

                    // Aplica o branding em librustdesk.dll e icon.svg ANTES de subir o --server!
                    ApplyVisualBrandingToExtractedEngine();

                    onProgress?.Invoke("Conectando e registrando computador na Rede Coliseu Sistemas...");
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

                    for (int i = 0; i < 16; i++)
                    {
                        Thread.Sleep(500);
                        var status = QueryLiveStatusFromPipe();
                        if (!string.IsNullOrEmpty(status.Id) && !string.IsNullOrEmpty(status.TemporaryPassword))
                        {
                            SendIpcQuery("{\"t\":\"Theme\",\"c\":\"light\"}");
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
            SendIpcQuery("{\"t\":\"Config\",\"c\":[\"temporary-password\",\"\"]}");
            Thread.Sleep(150);
            var status = QueryLiveStatusFromPipe();
            return string.IsNullOrEmpty(status.TemporaryPassword) ? null : status.TemporaryPassword;
        }

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

        /// <summary>
        /// Verifica se já existe uma janela de sessão remota aberta e a traz para frente
        /// em vez de abrir processos duplicados sem necessidade.
        /// </summary>
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

        /// <summary>
        /// Renomeia automaticamente o título de janelas de sessão abertas para ColiseuDesk - Coliseu Sistemas.
        /// </summary>
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

                Task.Run(async () =>
                {
                    for (int i = 0; i < 6; i++)
                    {
                        await Task.Delay(500);
                        BrandOpenSessionWindows();
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
