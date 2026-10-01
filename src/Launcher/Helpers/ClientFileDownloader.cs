using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

using Downloader;
using HashDepot;

namespace Launcher.Helpers;

internal static class ClientFileDownloader
{
    public static async Task DownloadAsync(HttpClient httpClient, string address, string destinationPath,
        long expectedSize, ulong expectedHash, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedSize);

        var destination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);

        // Keep the replacement on the same volume. Never truncate the current file until
        // the complete replacement has passed both manifest checks.
        var temporaryPath = Path.Combine(directory, $".launcher-{Guid.NewGuid():N}.download");
        var ownsTemporaryFile = false;
        Exception? failure = null;

        try
        {
            await using (var temporary = new FileStream(temporaryPath, FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                ownsTemporaryFile = true;

                // A failed Downloader 5.8 transfer can leave a stale chunk length in its
                // reusable package. Isolate that mutable state to this one file.
                await using var download = new DownloadService(new DownloadConfiguration
                {
                    CustomHttpClientFactory = () => httpClient,
                    MaxTryAgainOnFailure = 5,
                    ChunkCount = 1,
                    ParallelDownload = false
                });
                await using var package = download.Package;
                AsyncCompletedEventArgs? completion = null;
                download.DownloadFileCompleted += (_, args) => completion = args;

                // The package owns the returned stream and disposes it with its writer.
                var stream = await download.DownloadFileTaskAsync(address, cancellationToken);

                if (completion?.Error is { } error)
                    ExceptionDispatchInfo.Capture(error).Throw();

                cancellationToken.ThrowIfCancellationRequested();
                if (completion?.Cancelled == true)
                    throw new OperationCanceledException("The file download was cancelled.", cancellationToken);

                if (completion is null || download.Status != DownloadStatus.Completed || stream is null)
                    throw new IOException($"The file download did not complete (status: {download.Status}).");

                if (stream.Length != expectedSize)
                    throw new InvalidDataException($"Downloaded size does not match the manifest: expected {expectedSize} bytes, received {stream.Length}.");

                await stream.CopyToAsync(temporary, cancellationToken);
                if (temporary.Length != expectedSize)
                    throw new InvalidDataException($"Written size does not match the manifest: expected {expectedSize} bytes, received {temporary.Length}.");

                temporary.Position = 0;
                var hash = await Task.Run(() => XXHash.Hash64(temporary), cancellationToken);
                if (hash != expectedHash)
                    throw new InvalidDataException("Downloaded checksum does not match the manifest.");

                await temporary.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destination, overwrite: true);
            ownsTemporaryFile = false;
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            if (ownsTemporaryFile)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception cleanupError) when (failure is not null)
                {
                    // Preserve the original diagnostic and record why our own temporary
                    // file could not be removed. Never delete any other file here.
                    failure.Data["TemporaryFileCleanupError"] = cleanupError.ToString();
                }
            }
        }
    }
}
