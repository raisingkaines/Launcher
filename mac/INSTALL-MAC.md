# Installing on macOS

## The whole thing

Unzip the download, then **right click `Install.command` and choose Open**. Click Open
again when macOS asks.

It puts the launcher in Applications, clears the download quarantine that makes macOS call
it damaged, and offers to set up Wine for the game.

Right click rather than double click: that is what lets the script run the first time. A
double click will be refused, for the same reason the app itself would be.

## If you already downloaded it and macOS says it is damaged

It is not damaged. macOS marks anything downloaded in a browser as quarantined, and
refuses to open quarantined apps that are not signed with a paid Apple developer
certificate. The message is the same one it shows for genuinely corrupt files, which is
unhelpful, but the file is fine - the checksum in SHA256SUMS.txt will confirm it.

Drag the app to Applications, then run this once:

```sh
xattr -cr /Applications/FreeRealmsLegacyLauncher.app
```

Then open it normally. You only need to do this once per install.

## Why Install.command is needed at all

Apple lets an app open without any of this once it is signed with a Developer ID
certificate and notarised, which costs $99 a year. Until then macOS blocks it, and for an
app whose signature it cannot check the block has no "Open Anyway" button - only the
misleading claim that the file is damaged.

Quarantine is set by whatever downloads the file, not by the file itself: browsers set it,
`curl` does not. So if you would rather skip the script entirely, downloading in Terminal
avoids the flag being set in the first place:

```sh
curl -L -o frl.zip <the release download url>
unzip -q frl.zip -d /Applications
open /Applications/FreeRealmsLegacyLauncher.app
```

## Which Macs this runs on

All of them. The app is Intel (x86_64), and Apple Silicon Macs run it through Rosetta 2.
If a Mac with Apple Silicon has never run an Intel app before, macOS offers to install
Rosetta the first time and takes a few seconds over it.

Requires macOS 11 Big Sur or later.

## Playing the game itself

The launcher runs natively. The game client is a 32-bit Windows program, and macOS
dropped 32-bit support, so it needs Wine built to handle that - Whisky is free, and
CrossOver also works. The launcher checks when you press Play and says what is missing.
