namespace Launcher.Models
{
    // Only path/model plumbing is substituted; report formatting and process capture are production code.
    public sealed class ServerInfo
    {
        public string Name { get; init; } = "Fixture";
        public string ClientPath { get; init; } = "";
    }
}
namespace Launcher.Helpers
{
    public static class InstallPaths
    {
        public static string GetClientPath(Launcher.Models.ServerInfo info) => info.ClientPath;
    }
}
