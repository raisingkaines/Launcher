namespace Launcher.Models;

public enum ManifestResult
{
    Success,
    HttpError,
    InvalidFormat,
    InvalidVersion,
    Outdated,
    DeserializeError
}