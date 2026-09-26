#!/bin/bash
# Sets the launcher up on a Mac, in one double click.
#
# Three things stand between someone downloading this and playing, and none of them are
# obvious. This does all three and says what it is doing.
#
#   1. macOS quarantines anything a browser downloads, and refuses to open a quarantined
#      app that is not signed with a paid Apple certificate. The message it shows says the
#      app is damaged, which sends people to the Trash instead of to the fix.
#   2. The app has to live somewhere sensible, which people are used to doing by hand.
#   3. The game itself needs Wine, which is a separate install and easy to get wrong -
#      the client is 32-bit, and plain Wine on macOS usually cannot run it.
#
# A .command file is a plain script, so Gatekeeper lets it through with a right click and
# Open even when it will not open an unsigned app at all. That is the whole reason this
# exists rather than a note telling people to type xattr commands.
set -u

APP="FreeRealmsLegacyLauncher.app"
HERE="$(cd "$(dirname "$0")" && pwd)"

bold=$'\033[1m'; dim=$'\033[2m'; green=$'\033[32m'; yellow=$'\033[33m'; off=$'\033[0m'

say()  { printf '%s\n' "$*"; }
step() { printf '\n%s==>%s %s\n' "$bold" "$off" "$*"; }
ok()   { printf '    %s.%s %s\n' "$green" "$off" "$*"; }
warn() { printf '    %s!%s %s\n' "$yellow" "$off" "$*"; }

trap 'printf "\nSomething went wrong above. Nothing was left half-installed - you can run this again.\n"; read -r -p "Press Return to close. " _' ERR

say "${bold}Free Realms Legacy${off}"
say "${dim}Setting up the launcher.${off}"

if [ ! -d "$HERE/$APP" ]; then
    say ""
    warn "$APP is not next to this script."
    say "    Keep Install.command in the same folder as the app and run it again."
    read -r -p "Press Return to close. " _
    exit 1
fi

# --- 1. where it goes --------------------------------------------------------
step "Installing the app"

if [ -w /Applications ]; then
    DEST="/Applications"
else
    # No admin rights, or a managed Mac. A per-user Applications folder works the same
    # way and needs no password.
    DEST="$HOME/Applications"
    mkdir -p "$DEST"
    warn "/Applications is not writable, so installing to $DEST instead."
fi

if [ -d "$DEST/$APP" ]; then
    say "    Replacing the copy already in $DEST."
    rm -rf "$DEST/$APP"
fi

# ditto rather than cp, so signatures and extended attributes survive.
ditto "$HERE/$APP" "$DEST/$APP"
ok "Installed to $DEST"

# --- 2. the thing that makes macOS call it damaged ---------------------------
step "Clearing the download quarantine"

xattr -cr "$DEST/$APP" 2>/dev/null
chmod +x "$DEST/$APP/Contents/MacOS/Launcher" 2>/dev/null

if xattr -p com.apple.quarantine "$DEST/$APP" >/dev/null 2>&1; then
    warn "The quarantine flag is still set. Opening it may still warn once."
else
    ok "Cleared - macOS will open it normally"
fi

# --- 3. Wine, for the game rather than the launcher --------------------------
step "Checking for Wine"

WINE=""

for candidate in \
    "$(command -v wine 2>/dev/null)" \
    "/usr/local/bin/wine" \
    "/opt/homebrew/bin/wine" \
    "$HOME/Library/Application Support/com.isaacmarovitz.Whisky/Libraries/Wine/bin/wine" \
    "/Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin/wine"
do
    if [ -n "$candidate" ] && [ -x "$candidate" ]; then
        WINE="$candidate"
        break
    fi
done

if [ -n "$WINE" ]; then
    ok "Found Wine at $WINE"
else
    say "    The launcher itself does not need Wine. The game does - it is a 32-bit"
    say "    Windows program, and macOS dropped 32-bit support, so it needs a Wine"
    say "    built to handle that. Whisky is free and does."
    say ""

    if command -v brew >/dev/null 2>&1; then
        read -r -p "    Install Whisky now with Homebrew? [y/N] " reply

        case "$reply" in
            [yY]*)
                say ""
                if brew install --cask whisky; then
                    ok "Whisky installed"
                else
                    warn "That did not finish. You can install it later from whisky.app"
                fi
                ;;
            *)
                say "    Skipped. Install it later from https://whisky.app when you want to play."
                ;;
        esac
    else
        say "    Install Whisky from https://whisky.app, or CrossOver if you have it."
        say "    ${dim}(Homebrew is not installed, so this cannot do it for you.)${off}"
    fi
fi

# --- done --------------------------------------------------------------------
step "Done"
say "    Free Realms Legacy is in $DEST and in Launchpad."
say ""

read -r -p "Open it now? [Y/n] " reply

case "$reply" in
    [nN]*) say "    Fine - open it from Launchpad whenever you like." ;;
    *)     open "$DEST/$APP" ;;
esac

say ""
read -r -p "Press Return to close this window. " _
