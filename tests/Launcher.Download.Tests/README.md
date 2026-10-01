# Download helper regression harness

Run from this directory:

```powershell
dotnet run --project Launcher.Download.Tests.csproj -c Release
```

The console exits nonzero on any failed assertion and writes `test-results.json`. It links the actual helper source and uses restored NuGet Downloader5.8.0/HashDepot3.2.0 packages; this is separate from the original exact-published-DLL reproduction. A controlled HttpMessageHandler supplies range-probe and transfer responses; no remote host or game server is contacted. Temporary files live under a unique OS temporary directory retained in the results for inspection. No client installation files are read or written.

Checks cover the1–2048-byte boundary matrix, an HTTP403 followed by a successful file, wrong checksum, wrong size, premature body end, destination move failure, a real exclusive file lock, cancellation before and during transfer, an empty manifest file, and continued use of the shared HttpClient. Every rejection must preserve the previous destination and clean up the helper's owned temporary file. A separate NLog6.1.3 fixture starts four child processes using the launcher's file-target settings, including its explicit archive filename, and verifies that the current log plus the three most recent prior logs survive. The separate exact-published-DLL loopback investigation contains the original reused-service truncation reproduction and full request trace.
