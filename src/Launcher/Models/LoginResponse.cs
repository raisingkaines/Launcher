namespace Launcher.Models;

public sealed class LoginResponse
{
    public required string SessionId { get; set; }

    public string? LaunchArguments { get; set; }
}