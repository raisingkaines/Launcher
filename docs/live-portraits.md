# Active character headshot uploads

The launcher now watches the native headshot output throughout each launched game
session, including the normal in-game character selection path. Previously the
one-shot sync was reachable only when the optional launcher character list worked,
and ran before that session's new headshot was written.

The session records the existing cache before starting the process, then checks
for newly created or changed files every five seconds. It uploads stable 70×70 PNG
bytes through the existing authenticated character-portrait endpoint. It never
queues unchanged images from earlier accounts or sessions. The server continues
to verify character ownership. The upload destination and session credential are
fixed for the launched session.

Uploads are serialized and identical image bytes are coalesced. A file rewritten
during an upload is reconsidered on the next scan. Only successful JSON acknowledgments
mark an image sent; failures back off from five to sixty seconds. All cache metadata
is observed, while actual reads/uploads use rotating batches of eight so older or
invalid cache entries cannot starve the current character. Process exit or failed
startup cancels the session. Native directory spelling is discovered on case-sensitive
filesystems, including directories created after launch.

This uploads actual native headshots; it does not manufacture images or generate
the separate 180×330 full-body portrait. The coordinated Legacy Gateway change
requests native `Ui.RequestHeadshot` after login and changes to base appearance,
profile or equipment. Both server and updated launcher are needed for that flow.
The launcher must stay running. A request already accepted by the remote server
cannot be revoked by client cancellation; this does not claim strict ordering
between competing launcher sessions or the separate native portrait store.

Validation: eleven deterministic file/upload tests cover delayed directory creation,
old cache exclusion, changed/identical bytes, retries, an in-flight rewrite, serialized
scans, invalid names and sizes, cancellation, a new login, caches over128 files,
rotating recovery and native directory case. Release build passes. Tests ran on
Windows; no Mac/Wine launch or rendered game acceptance is implied. Source changes
must be included in a launcher release before other players receive them.
