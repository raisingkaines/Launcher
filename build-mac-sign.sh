# Sourced by the macOS build scripts. Sets SIGN_ARGS for vpk.
#
# Signing decides what the person downloading this actually sees.
#
# With an Apple Developer ID and notarisation, the app opens on a double click. Without
# one macOS blocks it - but there are two different blocks, and which one you get matters.
# An app whose signature does not check out is called "damaged" and offers no way past. A
# validly signed one is merely unidentified, and can still be opened.
#
# So even with no certificate, signing ad-hoc is worth doing: "-" is codesign's ad-hoc
# identity, and it seals the bundle against its own contents. That is the difference
# between a dead end and a dialog someone can get through.

SIGN_ARGS=()

if [ -n "${MACOS_SIGN_IDENTITY:-}" ]; then
    echo "Signing with the Developer ID identity."

    SIGN_ARGS+=(--signAppIdentity "$MACOS_SIGN_IDENTITY")

    if [ -n "${MACOS_INSTALL_IDENTITY:-}" ]; then
        SIGN_ARGS+=(--signInstallIdentity "$MACOS_INSTALL_IDENTITY")
    fi

    if [ -n "${MACOS_NOTARY_PROFILE:-}" ]; then
        echo "Notarising - this takes a few minutes while Apple checks the build."

        SIGN_ARGS+=(--notaryProfile "$MACOS_NOTARY_PROFILE")
    else
        echo "No notary profile set, so macOS will still warn on first open."
    fi
else
    echo "No Developer ID set - signing ad-hoc so the bundle is at least valid."

    SIGN_ARGS+=(--signAppIdentity "-")
fi
