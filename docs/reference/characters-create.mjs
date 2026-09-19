// Reference implementation for POST /characters/create - see
// docs/character-select-protocol.md.
//
// This is the half that runs wherever your /login lives. It authenticates the launcher's
// session, then raises a request the login server consumes on that account's next
// character-select - see CharacterCreateRequests.cs for the other half.
//
// Adapt the two lookups marked TODO to your own database. Everything else is the contract.

import express from "express";
import fs from "node:fs";
import path from "node:path";

/**
 * @param app      your express app
 * @param options  flagDirectory: the login server's character-create folder, which it
 *                 reads relative to its own working directory. The web process and the
 *                 login server have to agree on this path, and on nothing else.
 */
export function registerCharacterCreateRoute(app, { flagDirectory, findUserBySession, log = () => {} }) {
    if (!flagDirectory) {
        // Not configured means not supported, and the launcher handles a 404 by simply
        // launching normally. Leaving the route unregistered is a valid way to opt out.
        return;
    }

    app.post("/characters/create", async (req, res) => {
        // The launcher sends only the session it was given at login. The account is
        // whatever that resolves to; nothing the caller claims is trusted.
        const header = String(req.get("authorization") ?? "");
        const match = /^Bearer\s+(.+)$/i.exec(header.trim());
        const session = (match ? match[1] : "").trim();

        if (!session) {
            return res.status(401).json({ error: "invalid or missing session" });
        }

        // TODO: your own lookup. Must return null for an unknown or expired session.
        const user = await findUserBySession(session);

        if (!user) {
            return res.status(401).json({ error: "invalid or missing session" });
        }

        // This becomes a path segment, so prove it is a plain number rather than trusting
        // it, even though it came from your own database.
        const userId = String(user.Id ?? "").trim();

        if (!/^[0-9]+$/.test(userId)) {
            log("ERROR", "character-create-bad-user-id", { userId });

            return res.status(500).json({ error: "unexpected account id" });
        }

        try {
            fs.mkdirSync(flagDirectory, { recursive: true });

            // The modification time is the whole signal: the login server ignores anything
            // older than five minutes and deletes it as it reads it, so this is single use
            // and cannot strand an account on the creation screen.
            fs.writeFileSync(path.join(flagDirectory, userId + ".flag"), "");
        } catch (err) {
            log("ERROR", "character-create-flag-failed", { userId, error: String(err && err.message) });

            return res.status(500).json({ error: "failed to flag the account for character creation" });
        }

        log("INFO", "character-create-flag", { userId });

        // The launcher then starts the game with your normal login arguments - not the
        // Guid/Ticket pair from /characters/launch, which skips the login handshake and so
        // can never reach the creator.
        return res.json({ ok: true });
    });
}
