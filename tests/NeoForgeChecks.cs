// Made by PapaGordon
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using WindowsGSM.Functions;
using NeoForge = WindowsGSM.Plugins.Minecraft_NeoForge;

internal sealed class FixtureNeoForge : NeoForge
{
    internal FixtureNeoForge(ServerConfig settings) : base(settings) { }
    protected override string FindJava21() { return Environment.ProcessPath; }
    internal string Runtime(string root) { return FindWinArgsPath(root); }
    internal FileStream Lock(string root) { return AcquireUpdateLock(root); }
    internal string Recovery(string root) { return RecoveryMarker(root); }
}

internal static class NeoForgeChecks
{
    private static int passed;
    private static void Check(bool condition, string text)
    {
        if (!condition) throw new Exception(text);
        passed++;
        Console.WriteLine("PASS " + text);
    }

    private static object Static(string name, params object[] args)
    {
        try { return typeof(NeoForge).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
        catch (TargetInvocationException e) { throw e.InnerException; }
    }

    private static async Task<string> Prepare(NeoForge server, string root, string version, Func<string, Task<bool>> installer)
    {
        try { return await (Task<string>)typeof(NeoForge).GetMethod("PrepareAndCommit", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(server, new object[] { root, version, false, installer }); }
        catch (TargetInvocationException e) { throw e.InnerException; }
    }

    private static void Reject(Action action, string text)
    {
        try { action(); }
        catch (IOException) { Check(true, text); return; }
        throw new Exception("Expected rejection: " + text);
    }

    private static void Runtime(string root, string version)
    {
        string path = Path.Combine(root, "libraries", "net", "neoforged", "neoforge", version, "win_args.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, "fixture-runtime");
        File.WriteAllText(Path.Combine(root, "user_jvm_args.txt"), "-Xmx4G");
    }

    private static async Task Run(string id)
    {
        string metadata = "<metadata><versioning><versions>" + string.Concat(new[] {
            "21.1.9", "21.1.10", "21.1.11-beta", "21.4.100", "26.1.0.9", "26.1.0.10", "invalid", "../../bad"
        }.Select(v => "<version>" + v + "</version>")) + "</versions></versioning></metadata>";
        Check((string)Static("SelectVersion", metadata, "21.1") == "21.1.10", "Numeric stable selection stays on its Minecraft family");
        Check((string)Static("SelectVersion", metadata, "21.1.9") == "21.1.9", "Exact release pins are respected");
        Check((string)Static("SelectVersion", metadata, "21.1.11-beta") == "21.1.11-beta", "Betas require an exact explicit pin");
        Check((string)Static("SelectVersion", metadata, "26.1") == "26.1.0.10", "Four-component NeoForge versions sort numerically");
        Reject(() => Static("SelectVersion", metadata, "21.2"), "Missing stable family fails without selecting another Minecraft release");
        Reject(() => Static("SelectVersion", metadata, "21.1.99"), "Missing exact release is rejected");
        Reject(() => Static("SelectVersion", metadata, "../../bad"), "Version path traversal is rejected");
        Check((int)Static("RequiredJava", "20.4.100") == 17, "Minecraft 1.20.4 uses Java 17");
        Check((int)Static("RequiredJava", "20.6.100") == 21 && (int)Static("RequiredJava", "21.1.100") == 21, "Minecraft 1.20.6 and 1.21 use Java 21");
        Check((int)Static("RequiredJava", "26.1.0.10") == 25, "Minecraft 26.1 uses Java 25");
        Reject(() => Static("RequiredJava", "27.1.0.1"), "Unknown future Java requirements fail explicitly");

        var settings = new ServerConfig(id) { ServerGame = NeoForge.ServerName, ServerName = "NeoForge fixture", ServerPort = "25575",
            ServerQueryPort = "25576", ServerMaxPlayer = "12", EmbedConsole = false, ServerParam = "--fixture" };
        var server = new FixtureNeoForge(settings);
        string root = ServerPath.GetServersServerFiles(id);
        Directory.CreateDirectory(root);
        Check(server.FullName == NeoForge.ServerName && server.StartPath == "run.bat", "NeoForge exposes its own server metadata");
        Check(!server.IsImportValid(root), "Empty server folders cannot be imported");
        Runtime(root, "21.1.9");
        Check(server.IsImportValid(root) && server.GetLocalBuild() == "21.1.9", "A single installed runtime can be imported without a marker");
        Runtime(root, "21.1.10");
        Check(!server.IsImportValid(root) && server.Error.Contains("Multiple"), "Ambiguous imported runtimes require an explicit marker");
        File.WriteAllText(Path.Combine(root, ".wgsm-neoforge-version.txt"), "21.1.9");
        Check(server.IsImportValid(root) && server.Runtime(root).Contains("21.1.9"), "The marker selects the intended runtime instead of the highest directory");
        File.WriteAllText(Path.Combine(root, ".wgsm-atm10-version.txt"), "fixture");
        Check(!server.IsImportValid(root) && server.Error.Contains("ATM10"), "ATM10 folders retain their separate import path");
        File.Delete(Path.Combine(root, ".wgsm-atm10-version.txt"));
        File.WriteAllText(Path.Combine(root, "eula.txt"), "# eula=true\neula=false\n");
        Check(await server.Start() == null && server.Error.Contains("EULA"), "An EULA comment is not acceptance");
        File.WriteAllText(Path.Combine(root, "eula.txt"), "eula=true\n");
        File.WriteAllText(Path.Combine(root, "server.properties"), "motd=Private MOTD\nlevel-name=custom-world\nonline-mode=true\n");
        using (Process process = await server.Start())
        {
            Check(process != null, "NeoForge launches the isolated Java replacement fixture");
            process.WaitForExit(10000);
            Check(process.HasExited && process.ExitCode == 0, "The launch fixture exits cleanly");
        }
        string[] launchArgs = File.ReadAllLines(Path.Combine(root, "fixture-args.txt"));
        Check(launchArgs.Contains("@user_jvm_args.txt") && launchArgs.Any(a => a.Contains("21.1.9") && a.EndsWith("win_args.txt"))
            && launchArgs.Contains("nogui"), "Launch uses the selected runtime and existing JVM argument file");
        string properties = File.ReadAllText(Path.Combine(root, "server.properties"));
        Check(properties.Contains("server-port=25575") && properties.Contains("query.port=25576") && properties.Contains("max-players=12"), "WGSM ports and slots reach server.properties");
        Check(properties.Contains("motd=Private MOTD") && properties.Contains("level-name=custom-world") && properties.Contains("online-mode=true"), "Custom MOTD, world and authentication settings survive startup");
        File.WriteAllText(Path.Combine(root, "user_jvm_args.txt"), "# custom\n-Xms3G\n-Xmx6G\n-Dcustom=yes");
        Directory.CreateDirectory(Path.Combine(root, "custom-world"));
        File.WriteAllText(Path.Combine(root, "custom-world", "level.dat"), "world-data");
        Directory.CreateDirectory(Path.Combine(root, "mods"));
        File.WriteAllText(Path.Combine(root, "mods", "my-mod.jar"), "mod-data");
        File.WriteAllText(Path.Combine(root, "mods", "another-additional-mod.jar"), "additional-mod-data");
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(Path.Combine(root, "config", "custom.toml"), "private-config");
        try { await Prepare(server, root, "21.1.10", stage => Task.FromResult(false)); throw new Exception("Failed installer accepted"); }
        catch (IOException) { Check(server.GetLocalBuild() == "21.1.9", "Failed preparation keeps the live version"); }
        try { await Prepare(server, root, "21.1.10", stage => { throw new IOException("fixture installer error"); }); throw new Exception("Thrown installer accepted"); }
        catch (IOException) { Check(File.ReadAllText(Path.Combine(root, "custom-world", "level.dat")) == "world-data", "Thrown installer errors leave the live world unchanged"); }
        string backup = await Prepare(server, root, "21.1.10", stage => { Runtime(stage, "21.1.10"); return Task.FromResult(true); });
        Check(server.GetLocalBuild() == "21.1.10" && server.IsInstallValid(), "A complete prepared runtime is activated");
        Check(File.ReadAllText(Path.Combine(root, "user_jvm_args.txt")).Contains("-Dcustom=yes"), "Installer defaults cannot overwrite custom JVM arguments");
        Check(File.ReadAllText(Path.Combine(root, "mods", "my-mod.jar")) == "mod-data" && File.ReadAllText(Path.Combine(root, "custom-world", "level.dat")) == "world-data",
            "Existing mods and world data survive activation");
        Check(File.ReadAllText(Path.Combine(root, "config", "custom.toml")) == "private-config", "Custom configuration survives activation");
        Check(File.ReadAllText(Path.Combine(root, "mods", "another-additional-mod.jar")) == "additional-mod-data", "Multiple additional NeoForge mods survive a runtime update unchanged");
        Check(!Directory.Exists(Path.Combine(root, "libraries", "net", "neoforged", "neoforge", "21.1.9")), "Old runtime libraries cannot shadow the selected version");
        Check(File.ReadAllText(Path.Combine(backup, ".wgsm-neoforge-version.txt")) == "21.1.9", "Previous server files remain in the backup");
        using (server.Lock(root)) Reject(() => { using (server.Lock(root)) { } }, "Concurrent operations cannot acquire the same lock");
        File.WriteAllText(server.Recovery(root), "fixture interrupted update");
        Reject(() => { using (server.Lock(root)) { } }, "Recovery marker blocks starting or updating an interrupted activation");
        Check(await server.Start() == null && server.Error.Contains("recovery"), "Startup reports an interrupted activation");
        File.Delete(server.Recovery(root));
        using (server.Lock(root)) Check(true, "Lock is released after recovery errors");

    }

    private static int Main(string[] args)
    {
        if (args.Contains("--fixture")) { File.WriteAllLines("fixture-args.txt", args); return 0; }
        string id = "neoforge-check-" + Guid.NewGuid().ToString("N");
        string parent = Path.GetFullPath(ServerPath.GetServers(id));
        try
        {
            if (args.Length == 2 && args[0] == "--online-installer") OnlineInstaller(id, args[1]).GetAwaiter().GetResult();
            else Run(id).GetAwaiter().GetResult();
            Console.WriteLine(passed + " NeoForge checks passed. No Minecraft game server started.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally
        {
            string safe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "servers")) + Path.DirectorySeparatorChar;
            if (parent.StartsWith(safe, StringComparison.OrdinalIgnoreCase) && Directory.Exists(parent)) Directory.Delete(parent, true);
        }
    }

    private static async Task OnlineInstaller(string id, string java)
    {
        string root = ServerPath.GetServersServerFiles(id);
        Directory.CreateDirectory(root);
        var server = new NeoForge(new ServerConfig(id));
        string metadata = await (Task<string>)Static("DownloadMetadata");
        string version = (string)Static("SelectVersion", metadata, "21.1");
        bool installed = await (Task<bool>)typeof(NeoForge).GetMethod("InstallRuntime", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(server, new object[] { root, java, version });
        Check(installed, "Official NeoForge installer downloads, verifies and creates the expected runtime");
        Check(server.IsImportValid(root), "The real installer output can be imported by the standalone NeoForge plugin");
        Console.WriteLine("Verified NeoForge " + version + " with " + java);
    }
}
