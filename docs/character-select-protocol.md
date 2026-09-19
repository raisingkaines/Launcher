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

## POST /characters/launch

```
POST {WebApiUrl}/characters/launch
Authorization: Bearer {sessionId}

{ "guid": "2065" }
```

The server must **re-resolve the selection from scratch** rather than trusting the value:

1. session token -> user id
2. `guid` -> character id (rejecting a GUID whose type nibble is not the player type)
3. `SELECT ... WHERE UserId = {user} AND Id = {characterId}`

That query is the authorization check. A GUID belonging to another account resolves to
nothing and must be rejected, leaving the player at the normal in-game select screen - the
same outcome as a server that never implemented this at all.

On success the server does exactly what it already does when a player clicks a character
in game: mint a ticket for that character, stamp `LastLogin`, and hand back where to
connect.

```json
{
  "server": "127.0.0.1:20260",
  "guid": "2065",
  "ticket": "0f8c1e10-3a6c-4f1b-9a1e-9d2b7c4f0a11"
}
```

| Response | Meaning |
| --- | --- |
| `200` with the body above | Launch straight into the world as that character |
| `403` / `404` | Not this account's character - launcher falls back to a normal launch |

## POST /characters/create

Optional. Asks the server to send this account to the in-game character creator on its
next login, for the launcher's "Add a Character" tile.

```
POST /characters/create
Authorization: Bearer <sessionId>
```

```json
{ "ok": true, "hookInstalled": true }
```

`hookInstalled` reports whether the login server can actually act on the request. A server
that accepted the request but cannot honour it should return `true`/`false` honestly rather
than implying success: the launcher launches either way, and `false` simply means the player
lands on the in-game select screen.

A server that has not implemented this answers `501`. The launcher treats any failure the
same way - it launches normally - so the tile still works, it just does not reach the
creator.

After a success the launcher **launches with the server's own arguments**, not the
`Guid`/`Ticket` pair from `/characters/launch`. That is deliberate and it is the whole
reason this endpoint exists.

### Why an endpoint is needed at all

The creator cannot be reached with a launch argument. The client's full option table is
`SessionId`, `Guid`, `CasSessionId`, `Ticket` and `Server`, plus four ini file overrides,
and none of them concern character select. The client decides between the creator and the
select screen from one thing only: `CharacterSelectInfoReply.Status`, which the login
server sets to `2` when the account has no characters and `1` otherwise.

So the only way in is for the login server to answer as though the account were empty.
Launching straight into the gateway with a guid and ticket skips the login handshake
entirely, which is why the creator is unreachable that way - the request that would carry
`Status` is never sent.

How a server arranges that is its own business. Server 1 does it by writing a short-lived,
single-use flag for the account, which a patched login server consumes on the next
character-select request; the flag expires after five minutes so an abandoned launch cannot
strand the account on the creation screen.

## How the client is told which character to load

This is not a new trick: it is how the retail website did it. The old SOE browser plugin
built the client's command line from a fixed list of parameters returned by the ticket
server:

```js
paramArr = ["Server", "Guid", "Ticket", "Internationalization:Locale",
            "ShowMemberLoadingScreen", "Country", "key", "CasSessionId"];
pluginCore.launchStr += paramArr[i] + "=" + resp.launchParams[paramArr[i]];
```

The website posted the chosen `characterId` to its ticket server, got back `launchParams`
carrying that character's **`Guid`** and a **`Ticket`**, and launched the client with
them. The client connected as that character with no select screen - login, pick a face,
Play Now.

Every one of those option names is present in the client binary (`Guid=%llu`,
`ServerTicket=%s`, `CasSessionId`, `ShowMemberLoadingScreen`), so the launcher can do the
same thing:

```
FreeRealms.exe Server={server} Guid={guid} Ticket={ticket} Internationalization:Locale={locale}
```

`Guid` is the character GUID - the value the account check above resolved, never one the
launcher derived. Launching without `Guid`/`Ticket`, as the launcher does today, gives the
normal in-game select screen, which is also the correct behaviour for an account with no
characters: the player lands in character creation.

> `--character=` in the client binary is **not** this mechanism. It belongs to the command
> line the client *builds* to launch the TCG module, alongside `--post-login=tutorial` and
> `FreeRealmsTCG.dll`. It is a red herring.

## Compatibility: a server that implements none of this

Every part of this is optional, and a server that implements none of it must be no worse off
than before the feature existed. The rules the launcher follows:

**Nothing here can stop someone playing.** Every failure - a 404, a redirect to a login page,
a timeout, malformed JSON, a refused ticket, a dead network - ends in the ordinary launch with
the server's own arguments, which is exactly what happened before character select existed.

**Support is probed, never assumed.** A 200 is not enough, because servers commonly answer
unknown paths with a catch-all. The reply must be `application/json`. Checked against real
servers that do not implement it:

| Server | `GET /characters` | Read as |
| --- | --- | --- |
| Server 2 sidecar | `404 text/plain` | unsupported |
| asset host | `404 text/plain` | unsupported |
| the website | `302` to an HTML page | unsupported |

**An unsupported server is cheap.** The probe has its own 5 second timeout, well under the
client's, so a server that never answers costs five seconds once rather than stalling the
login. A definite "no" - a 404/405, or a reply that is not JSON - is remembered for the rest
of the session, so it is asked once and never again.

A timeout or a network error is deliberately **not** remembered. A blip must not turn the
feature off until the launcher restarts, because the server may be perfectly fine on the next
attempt.

**The same rule applies to every endpoint, separately.** A server can implement `/characters`
without `/characters/launch`, `/characters/create` or the portrait upload. Each is probed on
its own terms and each falls back on its own:

| Missing | What happens |
| --- | --- |
| `GET /characters` | the in-game select screen, as before |
| `POST /characters/launch` | launches normally, the player picks in game |
| `POST /characters/create` | launches normally, lands on the in-game select screen |
| `POST /characters/:guid/portrait` | no portrait; nothing else is affected |

**An acknowledgement has to mean something.** The portrait upload requires a JSON `{"ok":true}`
before it records the file as sent. A catch-all answering 200 would otherwise look like success
and the portrait would never be retried.

**Portraits never block a login.** The upload runs in the background off the character list, so
the player is never waiting on it, and it stops after the first refusal rather than working
down the whole list against a server that will not take any of them.

## Implementing it on a server

1. Add the two endpoints, authenticated by the session your `/login` already issues.
2. Resolve portraits server-side, checking every keying your store has used.
3. Have `/characters/launch` mint a ticket the same way your login server does when a
   player picks a character in game, and return it with the gateway address.

No change to the game client or to your login server's packet handling is needed - the
client has supported this command line since retail.
