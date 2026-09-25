# Installing on Linux

Pick the one for your distribution. Any of them installs the launcher itself with no
further setup; the game client needs Wine, which the last section covers.

| Your distribution | File | Install with |
|---|---|---|
| Debian, Ubuntu, Mint, Pop!_OS, ChromeOS | `freerealmslegacy_<version>_amd64.deb` | `sudo apt install ./freerealmslegacy_*.deb` |
| Fedora, RHEL, Rocky, Alma, openSUSE | `freerealmslegacy-<version>-1.x86_64.rpm` | `sudo dnf install ./freerealmslegacy-*.rpm` |
| Arch, Manjaro, EndeavourOS | `freerealmslegacy-<version>-1-x86_64.pkg.tar.xz` | `sudo pacman -U ./freerealmslegacy-*.pkg.tar.xz` |
| Anything else | `FreeRealmsLegacyLauncher-<version>-linux-x86_64.tar.gz` | unpack, then `./install.sh` |
| Prefer not to install | `FreeRealmsLegacyLauncher.AppImage` | `chmod +x` it and run it |

ARM machines — a Raspberry Pi, an ARM Chromebook, Asahi on Apple hardware — use the
`arm64` / `aarch64` files instead. They are the same packages built for that architecture.

## The tarball, if your distribution is not listed

Gentoo, Void, Slackware, Solus, NixOS and anything else: the tarball runs from wherever
you unpack it.

```sh
tar xf FreeRealmsLegacyLauncher-*-linux-x86_64.tar.gz
cd FreeRealmsLegacyLauncher-*
./Launcher
```

`./install.sh` adds a menu entry and a `freerealmslegacy` command. It installs into
`~/.local`, so it needs no root and touches nothing outside your home directory.
`./uninstall.sh` reverses it and leaves your settings and downloaded game files alone.

## The AppImage

One file, no install, runs on essentially any distribution from the last decade. Useful on
a machine you do not administer, or to try it before installing. It does not update itself
through your package manager, which the packages above do.

Some minimal systems lack FUSE, which AppImages need. If it refuses to start, either
install `fuse` or extract it instead:

```sh
./FreeRealmsLegacyLauncher.AppImage --appimage-extract
./squashfs-root/AppRun
```

## Wine, for the game itself

The launcher is a native Linux program and needs nothing extra. The game client is a
32-bit Windows program, so it runs through Wine - and 32-bit support is a separate
install on most distributions:

```sh
# Debian, Ubuntu, ChromeOS
sudo dpkg --add-architecture i386
sudo apt update && sudo apt install wine wine32

# Fedora
sudo dnf install wine wine-core.i686

# Arch (needs the multilib repository enabled in /etc/pacman.conf)
sudo pacman -S wine

# openSUSE
sudo zypper install wine
```

On a Chromebook, run these inside the Linux container.

The launcher checks for Wine when you press Play and says what is missing, so none of this
has to be done before installing.

## What is not here

**Flatpak and Snap.** Both need to be built on Linux with their own toolchains, and the
AppImage already covers the "works anywhere without installing" case they would fill.

**A signed repository.** The packages are not signed, so your package manager will say so
when installing a local file. Checksums are published alongside the release; verify with
`sha256sum -c SHA256SUMS.txt` if you want to confirm the download.
