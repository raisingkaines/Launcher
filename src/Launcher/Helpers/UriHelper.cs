using System.Linq;

namespace Launcher.Helpers;

public static class UriHelper
{
    public static string JoinUriPaths(string url, params string[] paths)
    {
        return paths.Aggregate(url, (c, s) => $"{c.TrimEnd('/')}/{s.TrimStart('/')}");
    }
}