namespace Launcher.Models;

public enum ManifestResult
{
    Success,

    /// <summary>The server could not be reached at all - DNS, TLS or the connection itself.</summary>
    NetworkError,
    HttpError,
    InvalidFormat,
    InvalidVersion,
    UnsupportedVersion,
    DeserializeError
}