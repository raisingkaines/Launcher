namespace Launcher.Models;

public sealed class RegisterRequest
{
    public required string Username { get; set; }
    public required string Password { get; set; }
}