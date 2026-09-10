namespace Launcher.Models;

public sealed class Notification
{
    public bool IsError { get; set; } = false;
    public string Message { get; set; } = string.Empty;
}