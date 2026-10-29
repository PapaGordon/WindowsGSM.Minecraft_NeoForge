// Made by PapaGordon
// Shares the built-in NeoForge logic without requiring ATM10 or the MeFriendos fork.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Query;
using System.Net.Http;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace WindowsGSM.Plugins
{
    public abstract class NeoForgeRuntime
    {
        protected readonly ServerConfig _serverData;
        public Plugin Plugin;
        public string Error, Notice;
        protected NeoForgeRuntime(ServerConfig serverData) { _serverData = serverData; }
        public virtual string StartPath { get { return "run.bat"; } }
        public string FullName;
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 1;
        public object QueryMethod = new UT3();
        public string Port = "25565", QueryPort = "25565", Defaultmap = "world", Maxplayers = "20", Additional = "";
        protected abstract string FindJava21();
        protected abstract string FindWinArgsPath(string root);
        protected abstract string RecoveryMarker(string root);
        protected abstract FileStream AcquireUpdateLock(string root);

        public async void CreateServerCFG()
        {
            try
            {
                EnsureServerProperties();
            }
            catch (Exception e)
            {
                Error = e.Message;
            }
        }

        public async Task<Process> Start()
        {
            try
            {
                using (AcquireUpdateLock(ServerPath.GetServersServerFiles(_serverData.ServerID))) { }
            }
            catch (Exception e)
            {
                Error = e.Message;
                return null;
            }

            string root = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string javaPath = FindJava21();
            if (string.IsNullOrWhiteSpace(javaPath))
                return null;

            string winArgs = FindWinArgsPath(root);
            if (string.IsNullOrWhiteSpace(winArgs))
            {
                Error = "NeoForge runtime not found. Run Install/Update first so the NeoForge installer can create libraries\\...\\win_args.txt.";
                return null;
            }

            string eulaPath = Path.Combine(root, "eula.txt");
            if (!File.Exists(eulaPath) || File.ReadAllText(eulaPath).IndexOf("eula=true", StringComparison.OrdinalIgnoreCase) < 0)
            {
                Error = "Minecraft EULA is not accepted. Set eula=true in eula.txt first.";
                return null;
            }

            try
            {
                EnsureServerProperties();
            }
            catch (Exception e)
            {
                Error = "Unable to update server.properties: " + e.Message;
                return null;
            }

            string relativeWinArgs = MakeRelativePath(root, winArgs).Replace('/', '\\');
            var args = new StringBuilder();
            args.Append("@user_jvm_args.txt ");
            args.Append("@").Append(relativeWinArgs).Append(" ");
            args.Append("nogui ");
            if (!string.IsNullOrWhiteSpace(_serverData.ServerParam))
                args.Append(_serverData.ServerParam).Append(" ");

            var p = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = root,
                    FileName = javaPath,
                    Arguments = args.ToString(),
                    WindowStyle = ProcessWindowStyle.Minimized,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true
            };

            if (_serverData.EmbedConsole)
            {
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardInput = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;

                var serverConsole = new ServerConsole(_serverData.ServerID);
                p.OutputDataReceived += serverConsole.AddOutput;
                p.ErrorDataReceived += serverConsole.AddOutput;
            }

            try
            {
                p.Start();
                if (_serverData.EmbedConsole)
                {
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                }
                return p;
            }
            catch (Exception e)
            {
                Error = e.Message;
                return null;
            }
        }

        public async Task Stop(Process p)
        {
            await Task.Run(() =>
            {
                if (p == null || p.HasExited)
                    return;

                try
                {
                    if (p.StartInfo.RedirectStandardInput)
                    {
                        p.StandardInput.WriteLine("stop");
                        p.StandardInput.Flush();
                    }
                    else
                    {
                        ServerConsole.SendMessageToMainWindow(p.MainWindowHandle, "stop");
                    }

                    if (!p.WaitForExit(30000))
                        p.Kill();
                }
                catch
                {
                    try
                    {
                        if (!p.HasExited)
                            p.Kill();
                    }
                    catch { }
                }
            });
        }

        protected void PublishPreparedUpdate(string root, string stage, string backup)
        {
            string previous = Path.Combine(backup, "serverfiles");
            string marker = RecoveryMarker(root);
            // Flush recovery instructions before either rename. Keep this marker if
            // restoration fails so Start() cannot launch a partially recovered server.
            using (var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] text = Encoding.UTF8.GetBytes("ATM10 update recovery\r\nLive: " + root +
                    "\r\nPrevious server: " + previous + "\r\nPrepared server: " + stage +
                    "\r\nIf Live is missing, restore Previous server to Live. Keep the backup until the server is verified.\r\n" +
                    "After checking/restoring the server, remove this recovery file to allow startup.\r\n");
                stream.Write(text, 0, text.Length);
                stream.Flush(true);
            }
            try
            {
                Directory.Move(root, previous);
                try
                {
                    Directory.Move(stage, root);
                }
                catch (Exception publishError)
                {
                    try { Directory.Move(previous, root); }
                    catch (Exception restoreError)
                    {
                        throw new IOException("Could not activate the update or restore the previous server. " +
                            "The previous server is retained at " + previous + ". Recovery instructions: " + marker +
                            ". Activate: " + publishError.Message + "; restore: " + restoreError.Message, restoreError);
                    }
                    throw new IOException("Could not activate the update; the previous server was restored. " +
                        publishError.Message, publishError);
                }
            }
            catch
            {
                if (!Directory.Exists(previous) && Directory.Exists(root))
                    File.Delete(marker);
                throw;
            }
            File.Delete(marker);
        }

        protected void ValidateTree(string root)
        {
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked files or directories need a manual update: " + root);
            if (!Directory.Exists(root)) return;
            foreach (string entry in Directory.GetFileSystemEntries(root)) ValidateTree(entry);
        }

        private void CopyWritableFile(string source, string destination, bool overwrite)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            if (File.Exists(destination)) ClearReadOnly(destination);
            File.Copy(source, destination, overwrite);
            ClearReadOnly(destination);
        }

        private void ClearReadOnly(string path)
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }

        private void DeleteWritableFile(string path)
        {
            ClearReadOnly(path);
            File.Delete(path);
        }

        protected void DeleteWritableDirectory(string path)
        {
            ValidateTree(path);
            foreach (string file in Directory.GetFiles(path)) DeleteWritableFile(file);
            foreach (string dir in Directory.GetDirectories(path)) DeleteWritableDirectory(dir);
            ClearReadOnly(path);
            Directory.Delete(path);
        }

        protected void CopyDirectoryContents(string source, string destination, bool overwrite,
            bool keepJvmArgs = false)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly))
            {
                string target = Path.Combine(destination, Path.GetFileName(file));
                if (keepJvmArgs &&
                    string.Equals(Path.GetFileName(file), "user_jvm_args.txt", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(target)) continue;
                CopyWritableFile(file, target, overwrite);
            }
            foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.TopDirectoryOnly))
                CopyDirectoryContents(dir, Path.Combine(destination, Path.GetFileName(dir)), overwrite);
        }

        protected void EnsureServerProperties()
        {
            EnsureServerProperties(ServerPath.GetServersServerFiles(_serverData.ServerID));
        }

        protected void EnsureServerProperties(string root)
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "server.properties");

            var lines = File.Exists(path)
                ? File.ReadAllLines(path).ToList()
                : new List<string>();

            // Preserve a custom MOTD. WindowsGSM's ServerName is not the Minecraft
            // MOTD and must not overwrite server.properties on every start/update.
            bool hasMotd = lines.Any(line =>
                line.StartsWith("motd=", StringComparison.OrdinalIgnoreCase));

            if (!hasMotd)
                SetProperty(lines, "motd", FullName);

            SetProperty(lines, "server-port", _serverData.ServerPort);
            SetProperty(lines, "enable-query", "true");
            SetProperty(lines, "query.port", _serverData.ServerQueryPort);
            SetProperty(lines, "max-players", _serverData.ServerMaxPlayer);
            SetProperty(lines, "allow-flight", "true");

            File.WriteAllLines(path, lines.ToArray());
        }

        private void SetProperty(List<string> lines, string key, string value)
        {
            string prefix = key + "=";
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = prefix + EscapePropertyValue(value);
                    return;
                }
            }
            lines.Add(prefix + EscapePropertyValue(value));
        }

        private string EscapePropertyValue(string value)
        {
            if (value == null)
                return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", " ");
        }

        private string MakeRelativePath(string basePath, string fullPath)
        {
            string baseFull = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string targetFull = Path.GetFullPath(fullPath);
            if (targetFull.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
                return targetFull.Substring(baseFull.Length);
            return targetFull;
        }
    }

    // Same installation, version selection and update behavior as the built-in module.
    public class Minecraft_NeoForge : NeoForgeRuntime
    {
        public const string ServerName = "Minecraft: NeoForge";
        private const string Selector = "neoforge-version.txt";
        private const string Marker = ".wgsm-neoforge-version.txt";
        private const string Maven = "https://maven.neoforged.net/releases/net/neoforged/neoforge/";
        public override string StartPath => "run.bat";

        public Minecraft_NeoForge(ServerConfig serverData) : base(serverData)
        {
            FullName = ServerName;
            Plugin = new Plugin { name = "WindowsGSM.Minecraft_NeoForge", author = "MeFriendos",
                description = "Minecraft NeoForge server without a bundled modpack", version = "0.1.0",
                url = "https://github.com/PapaGordon/WindowsGSM.Minecraft_NeoForge", color = "#D77D42" };
        }

        public async Task<Process> Install()
        {
            Error = null;
            if (!await UI.CreateYesNoPromptV1("Minecraft EULA",
                "By continuing you confirm that you agree to the Minecraft EULA.\nhttps://www.minecraft.net/eula", "Agree", "Decline"))
            {
                Error = "Minecraft EULA was not accepted.";
                return null;
            }
            return await InstallOrUpdate(true);
        }

        public Task<Process> Update() { return InstallOrUpdate(false); }

        public new async Task<Process> Start()
        {
            Error = null;
            Notice = null;
            try
            {
                if (!IsInstallValid()) return null;
                string eula = Path.Combine(ServerPath.GetServersServerFiles(_serverData.ServerID), "eula.txt");
                string assignment = File.Exists(eula) ? File.ReadLines(eula).LastOrDefault(l => Regex.IsMatch(l, @"^\s*eula\s*=")) : null;
                if (assignment == null || !Regex.IsMatch(assignment, @"^\s*eula\s*=\s*true\s*$", RegexOptions.IgnoreCase))
                    throw new IOException("Minecraft EULA is not accepted. Set eula=true in eula.txt first.");
                return await base.Start();
            }
            catch (Exception e) { Error = "NeoForge could not start: " + e.Message; return null; }
        }

        private async Task<Process> InstallOrUpdate(bool acceptEula)
        {
            Error = null;
            Notice = null;
            string root = ServerPath.GetServersServerFiles(_serverData.ServerID);
            try
            {
                Directory.CreateDirectory(root);
                using (AcquireUpdateLock(root))
                {
                    string selection = ReadSelection(root);
                    string version = SelectVersion(await DownloadMetadata(), selection);
                    if (!acceptEula && GetLocalBuild() == version && IsInstallValid())
                    {
                        Notice = "NeoForge " + version + " is already up to date.";
                        return null;
                    }
                    string java = await Task.Run(() => FindJava(RequiredJava(version)));
                    if (java == null) return null;
                    string backup = await PrepareAndCommit(root, version, acceptEula,
                        stage => InstallRuntime(stage, java, version));
                    Notice = "NeoForge " + version + " installed. Previous server files: " + backup;
                }
            }
            catch (Exception e) { Error = "NeoForge installation/update failed: " + e.Message; }
            return null;
        }

        private string ReadSelection(string root)
        {
            string file = Path.Combine(root, Selector);
            if (File.Exists(file)) return File.ReadAllText(file).Trim();
            // Stay on the installed Minecraft family unless the user changes it.
            string installed = InstalledVersion(root);
            string selection = installed == null ? "21.1" : Family(installed);
            File.WriteAllText(file, selection + Environment.NewLine);
            return selection;
        }

        private static string Family(string version)
        {
            return string.Join(".", version.Split('.').Take(2));
        }

        internal static string SelectVersion(string metadata, string selection)
        {
            if (!Regex.IsMatch(selection, @"\A\d+\.\d+(?:\.\d+){0,2}(?:-beta)?\z"))
                throw new IOException("Use a NeoForge family (21.1) or an exact version in neoforge-version.txt.");
            string[] versions = XDocument.Parse(metadata).Descendants("version").Select(e => e.Value.Trim())
                .Where(IsVersion).ToArray();
            if (versions.Contains(selection)) return selection;
            if (selection.Split('.').Length != 2 || selection.EndsWith("-beta", StringComparison.Ordinal))
                throw new IOException("The selected NeoForge version was not found on the official Maven.");
            string latest = versions.Where(v => !v.EndsWith("-beta", StringComparison.Ordinal) && Family(v) == selection)
                .OrderByDescending(v => Version.Parse(v)).FirstOrDefault();
            if (latest == null) throw new IOException("No stable NeoForge release was found for " + selection + ". Pin an exact beta version if needed.");
            return latest;
        }

        private static bool IsVersion(string version)
        {
            return version != null && Regex.IsMatch(version, @"\A\d+\.\d+\.\d+(?:\.\d+)?(?:-beta)?\z");
        }

        internal static int RequiredJava(string version)
        {
            if (!IsVersion(version)) throw new IOException("Invalid installed NeoForge version.");
            string[] parts = version.Split('.');
            int major = int.Parse(parts[0]), minor = int.Parse(parts[1]);
            if (major == 20 && minor >= 2 && minor <= 4) return 17;
            if (major == 20 && minor >= 5 && minor <= 6 || major == 21) return 21;
            if (major == 26) return 25;
            throw new IOException("The Java requirement for this NeoForge family is not supported yet.");
        }

        private static async Task<string> DownloadMetadata()
        {
            using (var client = new HttpClient()) return await client.GetStringAsync(Maven + "maven-metadata.xml");
        }

        public string GetLocalBuild()
        {
            try { return InstalledVersion(ServerPath.GetServersServerFiles(_serverData.ServerID)) ?? "unknown"; }
            catch { return "unknown"; }
        }

        public async Task<string> GetRemoteBuild()
        {
            try { return SelectVersion(await DownloadMetadata(), ReadSelection(ServerPath.GetServersServerFiles(_serverData.ServerID))); }
            catch (Exception e) { Notice = "Could not check NeoForge updates: " + e.Message; return GetLocalBuild(); }
        }

        private static string ArgsPath(string root, string version)
        {
            return Path.Combine(root, "libraries", "net", "neoforged", "neoforge", version, "win_args.txt");
        }

        private static string InstalledVersion(string root)
        {
            string marker = Path.Combine(root, Marker);
            if (File.Exists(marker))
            {
                string version = File.ReadAllText(marker).Trim();
                if (!IsVersion(version)) throw new IOException("Invalid NeoForge version marker.");
                return version;
            }
            string library = Path.Combine(root, "libraries", "net", "neoforged", "neoforge");
            if (!Directory.Exists(library)) return null;
            string[] versions = Directory.GetDirectories(library).Select(Path.GetFileName)
                .Where(v => IsVersion(v) && File.Exists(ArgsPath(root, v))).ToArray();
            if (versions.Length > 1) throw new IOException("Multiple NeoForge runtimes found. Set .wgsm-neoforge-version.txt to the intended installed version before importing.");
            return versions.FirstOrDefault();
        }

        protected override string FindWinArgsPath(string root)
        {
            string version = InstalledVersion(root);
            return version != null && File.Exists(ArgsPath(root, version)) ? ArgsPath(root, version) : null;
        }

        public bool IsInstallValid()
        {
            try { return IsImportValid(ServerPath.GetServersServerFiles(_serverData.ServerID)); }
            catch { return false; }
        }

        public bool IsImportValid(string path)
        {
            try
            {
                if (File.Exists(Path.Combine(path, ".wgsm-atm10-version.txt")))
                    throw new IOException("This is an ATM10 server. Use the separate ATM10 entry.");
                bool valid = File.Exists(Path.Combine(path, "user_jvm_args.txt")) && FindWinArgsPath(path) != null;
                if (!valid) Error = "An installed NeoForge runtime with win_args.txt and user_jvm_args.txt is required.";
                return valid;
            }
            catch (Exception e) { Error = e.Message; return false; }
        }

        protected override string FindJava21()
        {
            return FindJava(RequiredJava(InstalledVersion(ServerPath.GetServersServerFiles(_serverData.ServerID))));
        }

        private string FindJava(int required)
        {
            var candidates = new List<string>();
            string home = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrWhiteSpace(home)) candidates.Add(Path.Combine(home.Trim().Trim('"'), "bin", "java.exe"));
            string programFiles = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (string vendor in new[] { "Java", "Eclipse Adoptium", "Microsoft", "Amazon Corretto", "BellSoft", "Zulu" })
            {
                string folder = Path.Combine(programFiles, vendor);
                if (!Directory.Exists(folder)) continue;
                try { candidates.AddRange(Directory.GetDirectories(folder).OrderByDescending(d => d).Select(d => Path.Combine(d, "bin", "java.exe"))); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
                if (!string.IsNullOrWhiteSpace(folder)) candidates.Add(Path.Combine(folder.Trim().Trim('"'), "java.exe"));
            foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
            {
                try
                {
                    using (var p = new Process { StartInfo = new ProcessStartInfo(candidate, "-XshowSettings:properties -version") {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } })
                    {
                        p.Start();
                        var output = p.StandardOutput.ReadToEndAsync();
                        var error = p.StandardError.ReadToEndAsync();
                        if (!p.WaitForExit(10000)) { p.Kill(); continue; }
                        string text = output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();
                        Match match = Regex.Match(text, @"java\.specification\.version\s*=\s*(\d+)");
                        if (p.ExitCode == 0 && match.Success && int.Parse(match.Groups[1].Value) == required &&
                            Regex.IsMatch(text, @"sun\.arch\.data\.model\s*=\s*64\b")) return candidate;
                    }
                }
                catch { }
            }
            Error = "64-bit Java " + required + " was not found. Set JAVA_HOME or PATH to a matching JDK.";
            return null;
        }

        private async Task<bool> InstallRuntime(string stage, string java, string version)
        {
            string name = "neoforge-" + version + "-installer.jar";
            string installer = Path.Combine(stage, name);
            using (var client = new HttpClient())
            {
                string expected = (await client.GetStringAsync(Maven + version + "/" + name + ".sha256")).Trim();
                byte[] bytes = await client.GetByteArrayAsync(Maven + version + "/" + name);
                using (var hash = SHA256.Create())
                {
                    string actual = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
                    if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)) throw new IOException("NeoForge installer SHA-256 mismatch.");
                }
                File.WriteAllBytes(installer, bytes);
            }
            using (var p = new Process { StartInfo = new ProcessStartInfo(java, "-jar \"" + installer + "\" --installServer") {
                WorkingDirectory = stage, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true } })
            {
                p.Start();
                var output = p.StandardOutput.ReadToEndAsync();
                var error = p.StandardError.ReadToEndAsync();
                await Task.Run(() => p.WaitForExit());
                File.WriteAllText(Path.Combine(stage, "wgsm-neoforge-installer.log"), await output + await error);
                if (p.ExitCode != 0) throw new IOException("NeoForge installer exited with code " + p.ExitCode + ". See wgsm-neoforge-installer.log in the prepared folder.");
            }
            return File.Exists(ArgsPath(stage, version)) && File.Exists(Path.Combine(stage, "user_jvm_args.txt"));
        }

        internal async Task<string> PrepareAndCommit(string root, string version, bool acceptEula, Func<string, Task<bool>> installer)
        {
            if (!IsVersion(version)) throw new IOException("Invalid NeoForge version.");
            ValidateTree(root);
            string parent = Path.GetDirectoryName(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar));
            string backup = Path.Combine(parent, "neoforge-update-backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            string stage = Path.Combine(backup, "prepared-serverfiles");
            CopyDirectoryContents(root, stage, true);
            string libraries = Path.Combine(stage, "libraries");
            if (Directory.Exists(libraries)) DeleteWritableDirectory(libraries);
            if (!await installer(stage) || !File.Exists(ArgsPath(stage, version)))
                throw new IOException("NeoForge preparation failed. The existing server is unchanged. Prepared files: " + stage);
            string existingArgs = Path.Combine(root, "user_jvm_args.txt");
            if (File.Exists(existingArgs)) File.Copy(existingArgs, Path.Combine(stage, "user_jvm_args.txt"), true);
            if (!File.Exists(Path.Combine(stage, "user_jvm_args.txt"))) throw new IOException("NeoForge did not create user_jvm_args.txt.");
            if (acceptEula) File.WriteAllText(Path.Combine(stage, "eula.txt"), "eula=true" + Environment.NewLine);
            EnsureServerProperties(stage);
            Directory.CreateDirectory(Path.Combine(stage, "mods"));
            File.WriteAllText(Path.Combine(stage, Marker), version + Environment.NewLine);
            PublishPreparedUpdate(root, stage, backup);
            return Path.Combine(backup, "serverfiles");
        }

        protected override string RecoveryMarker(string root)
        {
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)), ".wgsm-neoforge-update-recovery.txt");
        }

        protected override FileStream AcquireUpdateLock(string root)
        {
            string parent = Path.GetDirectoryName(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar));
            Directory.CreateDirectory(parent);
            var file = new FileStream(Path.Combine(parent, ".wgsm-neoforge-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (File.Exists(RecoveryMarker(root)))
            {
                file.Dispose();
                throw new IOException("An interrupted NeoForge update needs recovery. Read " + RecoveryMarker(root));
            }
            return file;
        }
    }
}
