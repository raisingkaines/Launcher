using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Launcher.Helpers;
using Launcher.Models;

const string secret = "private-session-1234567890";
if (args.FirstOrDefault() == "--fake-wine")
{
    switch (args[1])
    {
        case "crash": Console.Error.Write("err:module:import_dll QtGui4.dll failed\nUnhandled page fault in FreeRealmsTCG.dll"); break;
        case "flood":
            for (var i = 0; i < 40000; i++) Console.Error.WriteLine($"fixme:fixture earlier noise {i} "+new string('x', 60));
            Console.Error.WriteLine("LAST: tcg fault 0x1234"); break;
        case "secrets":
            Console.Error.Write(new string('x', 1015)); Console.Error.Flush();
            Console.Error.WriteLine(secret + " Ticket=another-ticket Password='spaces in password' Authorization=Bearer private-bearer-token");
            Console.Error.WriteLine("{\"access_token\": \"quoted-access-token\"}");
            Console.Error.WriteLine("WINEPREFIX is not enumerated by the capture"); break;
        case "overlong": Console.Error.WriteLine(new string('x', 100000) + secret); Console.Error.WriteLine("last safe line"); break;
        case "idle": Console.Error.WriteLine("ready"); Console.Error.Flush(); await Task.Delay(1000); break;
        case "empty": break;
    }
    Environment.ExitCode = 7;
    return;
}

var results = new List<object>();
var scratch = Path.Combine(Path.GetTempPath(), "LauncherWineTests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
static void Check(bool condition, string failure) { if (!condition) throw new Exception(failure); }
Process Fake(string mode)
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
    if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add("--fake-wine"); start.ArgumentList.Add(mode);
    return new Process { StartInfo = start };
}
async Task Run(string name, Func<Task> action)
{
    try { await action(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { results.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex); }
}
async Task<string> Capture(string mode, bool readAfterExit = false)
{
    using var process = Fake(mode);
    var capture = new WineProcessDiagnostics(process, secret);
    Check(process.Start(), "Could not start fake runner");
    if (readAfterExit) await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    capture.StartReading();
    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    Check(process.ExitCode == 7, "Capture changed child exit status");
    return await capture.CompleteAsync();
}
await Run("CrashTailIncludesFinalUnterminatedLine", async () => {
    var report = await Capture("crash");
    Check(report.Contains("QtGui4.dll failed") && report.Contains("Unhandled page fault in FreeRealmsTCG.dll"), "Lost loader/fault diagnostic");
    Check(!report.Contains("incomplete"), "Did not drain EOF");
});
await Run("FloodCannotFillPipeOrGrowReportWithoutBound", async () => {
    var report = await Capture("flood");
    Check(report.Length < WineProcessDiagnostics.MaximumCharacters + 300, "Report grew beyond bound");
    Check(report.Contains("LAST: tcg fault") && report.Contains("Earlier stderr output omitted"), "Lost useful tail or truncation marker");
    Check(!report.Contains("earlier noise 0 "), "Retained beginning instead of tail");
});
await Run("RedactsCredentialsAcrossReadBuffers", async () => {
    var report = await Capture("secrets");
    foreach (var value in new[] { secret, "another-ticket", "spaces in password", "private-bearer-token", "quoted-access-token" })
        Check(!report.Contains(value), "Exposed credential " + value);
    Check(report.Contains("[redacted]"), "Redaction marker absent");
});
await Run("OversizedLineCannotLeakClippedCredential", async () => {
    var report = await Capture("overlong");
    Check(report.Contains("overlong stderr line was omitted") && report.Contains("last safe line"), "Oversized line handling failed");
    Check(!report.Contains(secret) && report.Length < 300, "Oversized data retained");
});
await Run("AlreadyExitedProcessStillDrainsPipe", async () => {
    var report = await Capture("crash", true);
    Check(report.Contains("FreeRealmsTCG.dll"), "Very early crash lost stderr");
});
await Run("DrainDeadlineDoesNotKillStillRunningProcess", async () => {
    using var process = Fake("idle");
    var capture = new WineProcessDiagnostics(process);
    process.Start(); capture.StartReading();
    var timer = Stopwatch.StartNew();
    var report = await capture.CompleteAsync(TimeSpan.FromMilliseconds(30));
    Check(timer.Elapsed < TimeSpan.FromSeconds(1), "Unbounded drain wait");
    Check(!process.HasExited, "Capture killed the runner");
    Check(report.Contains("may be incomplete"), "Incomplete capture presented as complete");
    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
});
await Run("NextLaunchCannotReusePreviousCrashTail", async () => {
    await Capture("crash");
    var report = await Capture("empty");
    Check(report.Contains("No stderr output") && !report.Contains("FreeRealmsTCG.dll"), "Stale previous launch output reused");
});
await Run("RunnerArgumentsWorkingDirectoryAndEnvironmentRemainUnchanged", () => {
    using var process = Fake("empty");
    process.StartInfo.WorkingDirectory = scratch;
    process.StartInfo.Environment["WINEPREFIX"] = "fixture-prefix-never-reported";
    var arguments = process.StartInfo.ArgumentList.ToArray();
    var environment = process.StartInfo.Environment.ToArray();
    _ = new WineProcessDiagnostics(process);
    Check(arguments.SequenceEqual(process.StartInfo.ArgumentList), "Arguments changed");
    Check(environment.SequenceEqual(process.StartInfo.Environment), "Environment changed");
    Check(process.StartInfo.WorkingDirectory == scratch && !process.StartInfo.RedirectStandardOutput, "Launch settings changed unexpectedly");
    Check(!process.StartInfo.UseShellExecute && process.StartInfo.RedirectStandardError, "Stderr not redirected");
    return Task.CompletedTask;
});
await Run("ReportIncludesWineEvenWithoutClientLog", async () => {
    var report = CrashReportHelper.Build(new ServerInfo { ClientPath = scratch }, 7, TimeSpan.FromSeconds(4), await Capture("crash"));
    Check(report.Contains("No client log was found") && report.Contains("FreeRealmsTCG.dll"), "Early failure lost Wine diagnostic");
});
await Run("ExistingReportAndClientTailPreservedWithoutWine", () => {
    var logs = Path.Combine(scratch, "Logs"); Directory.CreateDirectory(logs);
    File.WriteAllText(Path.Combine(logs, "FreeRealms.log"), "native client line\n");
    var info = new ServerInfo { ClientPath = scratch };
    var report = CrashReportHelper.Build(info, -1073741819, TimeSpan.FromSeconds(80));
    Check(report == CrashReportHelper.Build(info, -1073741819, TimeSpan.FromSeconds(80), null), "Optional diagnostics changed existing path");
    Check(report.Contains("0xC0000005") && report.Contains("native client line") && !report.Contains("Wine stderr"), "Windows report changed");
    return Task.CompletedTask;
});
await File.WriteAllTextAsync(Path.Combine(scratch, "test-results.json"), JsonSerializer.Serialize(new { scratch, results }, new JsonSerializerOptions { WriteIndented = true }));
var failed = results.Count(value => JsonSerializer.Serialize(value).Contains("\"passed\":false"));
Console.WriteLine($"{results.Count - failed} passed, {failed} failed");
Console.WriteLine($"Results: {Path.Combine(scratch, "test-results.json")}");
Environment.ExitCode = failed == 0 ? 0 : 1;
