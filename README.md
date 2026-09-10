<a id="readme-top"></a>

<!-- PROJECT LOGO -->
<br />
<div align="center">
  <a href="https://github.com/raisingkaines/Launcher">
    <img src="assets/icon.png" alt="FreeRealms Legacy Launcher Logo" width="100" height="100">
  </a>

  <h3 align="center">FreeRealms Legacy Launcher</h3>

  <p align="center">
    A cross-platform game launcher for Free Realms servers.
    <br />
    <a href="https://github.com/raisingkaines/Launcher/releases"><strong>Download Latest Release »</strong></a>
    <br />
    <br />
    <a href="https://github.com/raisingkaines/Launcher/issues/new">Report Bug</a>
    ·
    <a href="https://github.com/raisingkaines/Launcher/issues/new">Request Feature</a>
  </p>
</div>

<!-- ABOUT THE PROJECT -->
## About The Project

The **FreeRealms Legacy Launcher** is a cross-platform desktop application built with [Avalonia UI](https://avaloniaui.net/) and C# for connecting to and managing Free Realms servers.

### Features
* **Cross-Platform**: Runs on Windows, macOS, and Linux.
* **Server Manager**: Add, manage, and switch between custom Free Realms servers with manifest checking.
* **Client Manifest Sync**: Automatically checks, downloads, and updates client assets using multithreaded downloads.
* **Discord Rich Presence**: Integrated Discord game activity showing server status and in-game presence.
* **Automatic Updates**: Self-updating launcher powered by Velopack.

---

## Getting Started

### Installation
1. Navigate to the **[Releases page](https://github.com/raisingkaines/Launcher/releases)**.
2. Download the latest `FreeRealmsLegacyLauncher-win-Setup.exe`.
   > **Note:** We recommend the setup installer for automatic updates, but standalone portable builds are also available.
3. Run the installer and follow the on-screen prompts.

### How to Play
1. Launch the application.
2. Add or select a server URL (e.g. `https://play.raisingkaines.com`).
3. Click **Play** to verify and download required client files.
4. Log in with your account credentials and enjoy Free Realms!

---

## Building from Source

### Prerequisites
* [.NET 9 SDK](https://dotnet.microsoft.com/download)
* Visual Studio 2022 / Rider / VS Code with C# Dev Kit

### Compilation
Clone the repository and build:
```sh
git clone https://github.com/raisingkaines/Launcher.git
cd Launcher
dotnet build
```

Packaging scripts for each platform are available in the root directory:
* `build_win-x64.bat` — Windows x64 installer & portable build
* `build_osx-arm64.sh` / `build_osx-x64.sh` — macOS builds
* `build_linux-x64.sh` / `build_linux-arm64.sh` — Linux builds

---

## Contributing

Contributions are welcome! If you find a bug or have a suggestion, feel free to open an issue or submit a pull request.

---

## License

Distributed under the GNU General Public License v3. See [`LICENSE`](LICENSE) for more information.
