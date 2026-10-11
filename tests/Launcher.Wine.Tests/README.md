# Wine diagnostics tests

Run `dotnet run --project tests/Launcher.Wine.Tests -c Release` from the repository root. This standalone harness launches itself as a fake Wine process; it does not start the game or require Wine. Production `WineProcessDiagnostics` and `CrashReportHelper` are linked directly. Only the report's server/path model plumbing is stubbed. Results are written under the test's printed temporary directory.

Set `TEMP` / `TMP` to an available scratch directory when needed. The test requires .NET 10 and restores NLog at the repository's existing version. These tests do not establish Mac compatibility or reproduce a TCG crash.
