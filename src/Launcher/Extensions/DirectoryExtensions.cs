using System.IO;

namespace Launcher.Extensions;

public static class DirectoryExtensions
{
    public static string ToValidDirectoryName(this string name)
    {
        return string.Join('_', name.Split(Path.GetInvalidPathChars()));
    }
}