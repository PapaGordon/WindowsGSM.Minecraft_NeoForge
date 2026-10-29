// Made by PapaGordon
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

// Small API fixture; integration checks also use the real WindowsGSM assembly.
namespace WindowsGSM.Functions
{
    public class Plugin { public string name, author, description, version, url, color; }
    public class ServerConfig
    {
        public string ServerID = "5", ServerParam = "", ServerPort = "25565",
            ServerQueryPort = "25565", ServerMaxPlayer = "20";
        public bool EmbedConsole;
    }
    public static class ServerPath
    {
        public static string Root;
        public static string GetServersServerFiles(string id, params string[] parts)
        {
            string result = Root;
            foreach (string part in parts) result = Path.Combine(result, part);
            return result;
        }
    }
    public class ServerConsole
    {
        public ServerConsole(string id) { }
        public void AddOutput(object sender, DataReceivedEventArgs args) { }
        public static void SendMessageToMainWindow(IntPtr handle, string message) { }
    }
    public static class UI
    {
        public static bool Agree = true;
        public static Task<bool> CreateYesNoPromptV1(string title, string text, string yes, string no)
        { return Task.FromResult(Agree); }
    }
}
namespace WindowsGSM.GameServer.Query { public class UT3 { } }
