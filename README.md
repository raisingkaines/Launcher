<a id="readme-top"></a>

<div align="center">
  <img src="assets/icon.png" alt="Free Realms Legacy" width="100" height="100">

  <h3 align="center">Free Realms Legacy Launcher</h3>

  <p align="center">
    The launcher for Free Realms Legacy.
    <br />
    <a href="https://github.com/raisingkaines/Launcher/releases"><strong>Download the latest release &raquo;</strong></a>
  </p>
</div>

---

### Getting started

1. Download `FreeRealmsLegacyLauncher-win-Setup.exe` from the
   [releases page](https://github.com/raisingkaines/Launcher/releases). A portable
   build is there too if you would rather not install anything.
2. Run it, then press **Play** and log in.

`https://play.raisingkaines.com` is added for you the first time the launcher runs.
Use **+** in the bottom-left to add any other server.

### Where the game gets installed

By default the game lands in `%LOCALAPPDATA%\FreeRealmsLegacyLauncher\Servers`, but it
does not have to stay there. Open **Settings** (the cog) and use **Change...** next to
*Game install location* - that opens the standard Windows folder picker, so any drive
with enough room works.

Leave **Move installed game files to the new folder** ticked and anything already
downloaded is moved across, including onto a different drive, rather than downloaded
again. The launcher checks the new folder is writable and has room before it starts,
and refuses to move while the game is running.

The launcher's own settings and saved logins stay in your user profile - only the game
files move.

### Pushing an update to everyone

Installed launchers check GitHub on every launch and again whenever someone presses the
refresh button in the rail. Publishing a new release is what reaches them.

One command does the whole thing from your own machine:

```powershell
$env:GITHUB_TOKEN = "ghp_..."     # a token with "repo" scope
.\release.ps1 -Version 1.2.0
```

That will:

1. Download the current published release, so this build ships as a delta - players
   download a few MB rather than the whole 30.
2. Publish and pack the app with Velopack.
3. Create the GitHub release and upload everything to it.

Add `-Draft` to upload without releasing, and publish it on GitHub when you are ready.

Bump the version every time; Velopack refuses to pack a version that is not newer than
what is already in `releases/`. Nothing in `releases/` or `.build-temp/` is committed.

Players on the portable build cannot self-update - pressing the button sends them to the
downloads page instead.

### Building from source

Needs the .NET 10 SDK and [Velopack](https://velopack.io) (`dotnet tool install -g vpk`).

```bash
dotnet build ./src/Launcher.slnx -c Release
./build_win-x64.bat 1.2.0
```

`build_win-x64.bat` publishes the app and packs a Velopack release into `releases/`.
There are matching scripts for Linux and macOS.

### Credits

Forked from the [Open Source Free Realms launcher](https://github.com/Open-Source-Free-Realms/Launcher)
and licensed under the AGPL-3.0, the same as upstream - see [LICENSE](LICENSE).

Artwork notes and how to swap the branding are in [brand/README.md](brand/README.md).
