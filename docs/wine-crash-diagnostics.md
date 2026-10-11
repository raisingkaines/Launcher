# Wine crash reports

This change improves diagnosis on the launcher's existing macOS/Linux Wine path. It does not fix or establish the cause of a native TCG crash. It does not select a different Wine distribution, change a prefix/bottle, alter game arguments, replace native binaries, or enable extra Wine tracing.

After a launcher release containing this change is installed:

1. Start the game through that launcher and reproduce the TCG crash.
2. In the launcher's crash popup, select **Copy details** and share the complete report. It now includes **Wine stderr from this launch**, even if the game did not create `Logs/FreeRealms.log`.
3. Include which Mac runner is in use and the action that triggered the crash. No environment dump is needed.

The tail is held in memory for this launch only: at most 80 lines / 16 KiB, with oversized lines omitted. Known launch session/ticket values and common credential fields are redacted before lines are retained. The launcher does not collect arguments or environment variables, write a separate stderr file, or send the report anywhere. A two-second drain deadline prevents an inherited Wine pipe from blocking the popup. An incomplete capture is labelled.

The existing crash detection rule is unchanged: a nonzero process exit or a zero exit in the first 20 seconds triggers a popup. If Wine detaches from the game or reports a long clean exit, this change cannot make the launcher observe a native child crash. An empty tail is also reported explicitly.

## Verification and limits

`dotnet run --project tests/Launcher.Wine.Tests -c Release` runs ten fake-runner process tests against production capture/report code. They cover final output without newline, multi-megabyte pipe pressure, credential values split across read buffers, oversized lines, exit before reading starts, bounded drain without killing a running process, separation between launches, preserved arguments/environment/working directory, missing native logs, and the existing report without Wine.

`dotnet build src/Launcher/Launcher.csproj -c Release` verifies the UI integration compiles. Automated tests/build here run on Windows. No macOS installer, signing/notarization, Wine launch, or native TCG gameplay is validated by these tests. A Mac release and a reproduced report remain necessary.
