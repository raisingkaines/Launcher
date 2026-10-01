namespace Launcher.Models;

public sealed class LocalFile
{
    public required string Path { get; set; }
    public required string Name { get; set; }
    public required uint Size { get; set; }
    public required ulong Hash { get; set; }
}
