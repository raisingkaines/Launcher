using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HashDepot;
using Launcher.Helpers;

if (args.Length == 3 && args[0] == "--log-child")
{
    using var factory = new NLog.LogFactory();
    var config = new NLog.Config.LoggingConfiguration(factory);
    var target = new NLog.Targets.FileTarget("file") { ArchiveOldFileOnStartup = true,
        ArchiveFileName = Path.Combine(Path.GetDirectoryName(args[1])!,"Launcher.previous.log"),
        MaxArchiveFiles = 3, FileName = args[1] };
    config.AddRule(NLog.LogLevel.Info,NLog.LogLevel.Fatal,target);
    factory.Configuration = config;
    factory.GetLogger("fixture").Info(args[2]); factory.Flush();
    return;
}

var root = Path.Combine(Path.GetTempPath(), "LauncherDownloadTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var outcomes = new List<object>();
int passed = 0, failed = 0;
var handler = new FixtureHandler();
using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
byte[] original = [91,92,93,94];
static byte[] Bytes(int size) => Enumerable.Range(0,size).Select(i => (byte)(i%251)).ToArray();
static ulong Hash(byte[] bytes) { using var stream = new MemoryStream(bytes); return XXHash.Hash64(stream); }
static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
async Task Run(string name, Func<string,Task> test)
{
    string directory = Path.Combine(root,name); Directory.CreateDirectory(directory);
    try { await test(directory); Check(!Directory.EnumerateFiles(directory, "*.download").Any(), "Leaked owned temporary file");
        outcomes.Add(new { name, passed = true }); passed++; Console.WriteLine("PASS " + name); }
    catch(Exception ex) { outcomes.Add(new { name, passed = false, error = ex.ToString() }); failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
}
async Task<Exception> Reject(Func<Task> task)
{
    try { await task(); } catch(Exception ex) { return ex; }
    throw new Exception("Expected failure, but operation succeeded");
}
async Task Download(string mode, int size, string destination, ulong? expectedHash = null, CancellationToken token = default)
    => await ClientFileDownloader.DownloadAsync(client, $"https://fixture.invalid/{mode}/{size}", destination, size,
        expectedHash ?? Hash(Bytes(size)), token);

foreach (int size in new[] { 1,100,955,1015,1023,1024,1025,2048 })
    await Run("Success"+size, async directory => {
        string path = Path.Combine(directory,"client.bin"); await File.WriteAllBytesAsync(path,original);
        await Download("ok",size,path);
        Check((await File.ReadAllBytesAsync(path)).SequenceEqual(Bytes(size)), "Output differs from manifest bytes");
    });
await Run("ForbiddenThenValid", async directory => {
    string path = Path.Combine(directory,"client.bin"); await File.WriteAllBytesAsync(path,original);
    var error = await Reject(() => Download("forbidden",955,path));
    Check(error is HttpRequestException { StatusCode: HttpStatusCode.Forbidden }, "Original HTTP403 status was lost: " + error);
    Check((await File.ReadAllBytesAsync(path)).SequenceEqual(original), "Failure replaced existing file");
    await Download("ok",955,path);
    Check((await File.ReadAllBytesAsync(path)).SequenceEqual(Bytes(955)), "Failed predecessor corrupted next successful file");
});
await Run("BadHashPreservesExisting", async directory => {
    string path = Path.Combine(directory,"client.bin"); await File.WriteAllBytesAsync(path,original);
    var error = await Reject(() => Download("wronghash",955,path));
    Check(error is InvalidDataException, "Checksum mismatch did not produce InvalidDataException");
    Check((await File.ReadAllBytesAsync(path)).SequenceEqual(original), "Hash rejection overwrote existing file");
});
await Run("WrongSizePreservesExisting", async directory => {
    string path = Path.Combine(directory,"client.bin"); await File.WriteAllBytesAsync(path,original);
    var error = await Reject(() => Download("undersize",955,path));
    Check(error is InvalidDataException, "Size mismatch did not produce InvalidDataException");
    Check((await File.ReadAllBytesAsync(path)).SequenceEqual(original), "Size rejection overwrote existing file");
});
await Run("TruncatedBodyPreservesExisting", async directory => {
    string path = Path.Combine(directory,"client.bin"); await File.WriteAllBytesAsync(path,original);
    await Reject(() => Download("truncated",955,path));
    Check((await File.ReadAllBytesAsync(path)).SequenceEqual(original), "Truncated body overwrote existing file");
});
await Run("DestinationDirectoryFailureCleansTemporary", async directory => {
    string path = Path.Combine(directory,"client.bin"); Directory.CreateDirectory(path);
    string sentinel = Path.Combine(path,"sentinel"); await File.WriteAllBytesAsync(sentinel,original);
    var error = await Reject(() => Download("ok",955,path));
    Check(error is IOException or UnauthorizedAccessException, "Disk error type was obscured");
    Check((await File.ReadAllBytesAsync(sentinel)).SequenceEqual(original), "Disk failure damaged destination");
});
await Run("CancelledBeforeStartPreservesExisting", async directory => {
    string path = Path.Combine(directory,"client.bin"); await File.WriteAllBytesAsync(path,original);
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    var error = await Reject(() => Download("ok",955,path,token:cancellation.Token));
    Check(error is OperationCanceledException, "Cancellation not preserved: " + error);
    Check((await File.ReadAllBytesAsync(path)).SequenceEqual(original), "Cancellation overwrote existing file");
});
await Run("CancelledDuringTransferPreservesExisting", async directory => {
    string path = Path.Combine(directory,"client.bin"); await File.WriteAllBytesAsync(path,original);
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
    var error = await Reject(() => Download("waiting",955,path,token:cancellation.Token));
    Check(error is OperationCanceledException, "In-flight cancellation not preserved: " + error);
    Check((await File.ReadAllBytesAsync(path)).SequenceEqual(original), "In-flight cancellation overwrote existing file");
});
await Run("LockedDestinationPreservesExisting", async directory => {
    string path = Path.Combine(directory,"client.bin"); await File.WriteAllBytesAsync(path,original);
    using (var locked = new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
    {
        var error = await Reject(() => Download("ok",955,path));
        Check(error is IOException or UnauthorizedAccessException, "Locked destination did not surface a disk error");
    }
    Check((await File.ReadAllBytesAsync(path)).SequenceEqual(original), "Locked destination contents changed");
});
await Run("ZeroByteManifestFile", async directory => {
    string path = Path.Combine(directory,"client.bin"); await File.WriteAllBytesAsync(path,original);
    await Download("ok",0,path);
    Check(new FileInfo(path).Length == 0, "Valid empty manifest file was not installed");
});
await Run("SharedHttpClientRemainsUsable", async _ => {
    using var response = await client.GetAsync("https://fixture.invalid/ok/955");
    Check((await response.Content.ReadAsByteArrayAsync()).SequenceEqual(Bytes(955)), "Shared HttpClient no longer works");
});
await Run("StartupLogRotationPreservesThreeArchives", async directory => {
    string path = Path.Combine(directory,"Launcher.log"); await File.WriteAllTextAsync(path,"run-0\n");
    for (int i = 1; i <= 4; i++)
    {
        var start = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--log-child"); start.ArgumentList.Add(path); start.ArgumentList.Add("run-" + i);
        using var child = System.Diagnostics.Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync();
        Check(child.ExitCode == 0, "Logging child failed: " + await output + await error);
    }
    var archives = Directory.EnumerateFiles(directory).Where(p => !string.Equals(p,path,StringComparison.OrdinalIgnoreCase)).ToArray();
    Check(archives.Length == 3, $"Expected three prior logs, found {archives.Length}");
    var prior = string.Join("\n",await Task.WhenAll(archives.Select(p => File.ReadAllTextAsync(p))));
    Check(new[] { "run-1","run-2","run-3" }.All(prior.Contains), "Rotation lost one of the three prior run logs");
    Check(!prior.Contains("run-0"), "Oldest archive was not pruned");
    Check((await File.ReadAllTextAsync(path)).Contains("run-4"), "Current run was not logged");
});

var result = new { passed, failed, scratch = root, outcomes, requests = handler.Requests };
await File.WriteAllTextAsync("test-results.json", JsonSerializer.Serialize(result,new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{passed} passed, {failed} failed; scratch retained at {root}");
Environment.ExitCode = failed == 0 ? 0 : 1;

sealed class FixtureHandler : HttpMessageHandler
{
    public List<object> Requests { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var parts = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
        string mode = parts[0]; int size = int.Parse(parts[1]);
        if (mode == "waiting" && request.Headers.Range is null) await Task.Delay(Timeout.Infinite,token);
        var body = Enumerable.Range(0,size).Select(i => (byte)(i%251)).ToArray();
        if (mode == "undersize") body = body[..^1];
        if (mode == "wronghash") body[0] ^= 255;
        int status = mode == "forbidden" ? 403 : 200;
        bool probe = request.Headers.Range is not null;
        HttpContent content;
        if (status == 403) content = new ByteArrayContent("Forbidden"u8.ToArray());
        else if (probe && body.Length > 0)
        {
            status = 206; content = new ByteArrayContent([body[0]]);
            content.Headers.ContentRange = new ContentRangeHeaderValue(0,0,body.Length);
        }
        else if (mode == "truncated")
        {
            content = new StreamContent(new MemoryStream(body[..^1]));
            content.Headers.ContentLength = size;
        }
        else content = new ByteArrayContent(body);
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = content, RequestMessage = request };
        response.Headers.AcceptRanges.Add("bytes");
        Requests.Add(new { mode, size, probe, status, declaredLength = content.Headers.ContentLength });
        return response;
    }
}
