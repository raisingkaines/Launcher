# Character select protocol

An optional capability any Free Realms server can implement. When a server supports it,
the launcher shows the account's characters with their in-game headshots after login, and
picking one takes the player straight into the world as that character.

Servers that do not implement it keep working exactly as they do today: the launcher
detects their absence and launches the client normally, into the in-game select screen.

## Why this is not in the server manifest

`servermanifest.xml` looks like the obvious place to advertise a capability, but it is a
trap in both directions:

- Its schema is a strict `<xs:all>` with no `<xs:any>`. A launcher validates against the
  copy of the XSD it shipped with, so a manifest carrying a new element **fails validation
  on every older launcher** and the server becomes unreachable for those players.
- The launcher refuses any manifest whose `version` is higher than the one it knows, so
  bumping the version locks out older launchers just as hard.

Either change forces every player to update before any server can advertise anything. So
capability is discovered by **probing the web API** instead. An old launcher simply never
asks; a server that has not implemented it answers 404 and the launcher moves on.

## Discovery

After a successful login, the launcher issues:

```
GET {WebApiUrl}/characters
Authorization: Bearer {sessionId}
```

| Response | Launcher behaviour |
| --- | --- |
| `200` with the body below | Show the character picker |
| `404` / `405` / any error | Launch normally, no picker, no error shown to the player |

The probe result may be cached for the lifetime of the launcher session.

## GET /characters

Returns the characters belonging to the **account the session token authenticates**. The
launcher sends no account identifier and none is accepted.

```json
{
  "characters": [
    {
      "guid": "2065",
      "name": "Roger Braverock",
      "portraitUrl": "https://example.com/portraits/2065/headshot.png",
      "lastLogin": "2026-09-18T21:16:35Z"
    }
  ]
}
```

- `guid` — the player GUID as a decimal string, computed by the server. In Sanctuary this
  is `(characterId << 4) | 1`. It is opaque to the launcher, which never derives, parses
  or computes it.
- `name` — display name.
- `portraitUrl` — absolute URL, resolved by the server, or `null` when the character has
  no portrait yet. **The launcher must never construct this path.** Portrait stores are
  not consistently keyed: on an existing Sanctuary deployment some folders are named by
  player GUID and others by raw character id, and `Images/1` is ambiguous between
  character id 1 and the GUID of character id 0. Only the server can resolve it safely.
- `lastLogin` — optional, used to sort most-recent first.

An account with no characters returns an empty array. The launcher then launches normally,
so the player lands in character creation.

## POST /characters/select

```
POST {WebApiUrl}/characters/select
Authorization: Bearer {sessionId}

{ "guid": "2065" }
```

The server must **re-resolve the selection from scratch** rather than trusting the value:

1. session token → user id
2. `guid` → character id (rejecting a GUID whose type nibble is not the player type)
3. `SELECT ... WHERE UserId = {user} AND Id = {characterId}`

That query is the authorization check. A GUID belonging to another account resolves to
nothing and must be rejected, leaving the player at the normal in-game select screen -
the same outcome as a server that never implemented this at all.

On success the server records the pending selection **against the session**, holding the
already-resolved character, so the login server never re-reads a client-supplied value.

| Response | Meaning |
| --- | --- |
| `204` | Selection accepted; the client will be taken straight into the world |
| `403` / `404` | Not this account's character - launcher falls back to a normal launch |

## Skipping the in-game select screen

The launcher cannot do this on its own. The client's select screen is Scaleform UI driven
by the client, and there is no command line argument that bypasses it - `--character=` in
the binary belongs to the command line the client *builds* to launch the TCG module, not
to one it accepts.

So the server does it. When a session has a pending selection, the login server performs
the work `CharacterLoginRequestHandler` normally does on the player's click - mint a
ticket, stamp `LastLogin`, reply with the gateway address - without waiting for the
request.

> **Unverified.** Whether the client acts on that reply while sitting on the select
> screen, or ignores a packet it did not ask for, has not yet been tested. Everything
> above is implementable regardless; this last step is what makes it seamless rather than
> just a nicer picker.

## Implementing it on a server

1. Add the two endpoints, authenticated by the session your `/login` already issues.
2. Resolve portraits server-side, checking every keying your store has used.
3. Record pending selections against the session.
4. Have the login server honour a pending selection.

Steps 1-2 alone give the player a picker. Steps 3-4 are what remove the in-game screen.
