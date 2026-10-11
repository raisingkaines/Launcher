# Launcher artifact preparation — 2026-10-11

`portrait-release-artifacts.yml` builds only on a push to
`fix/legacy-live-portraits-20261011`. It cannot create a release or a tag and has
only `contents: read` permission. The operator reviews downloaded artifacts and
performs any publication separately. A branch push is still an external action;
adding this file locally does not dispatch a build.

The three jobs use the existing build scripts and channels:

| Target | Runner | Channel | Runtime |
| --- | --- | --- | --- |
| Windows x64 | windows-2025 | win | framework dependent; installer provisions .NET 10 desktop |
| Mac Intel | macos-15-intel | osx | self-contained |
| Mac Apple Silicon | macos-15 | osx-arm64 | self-contained |

Velopack CLI is pinned to 1.2.0, matching the app dependency. Optional Developer
ID signing uses the existing secret names. Without a signing identity, the
existing Mac helper requests an ad-hoc signature. This is not notarization or a
claim that a downloaded app will open without a Gatekeeper prompt. Mac jobs
verify the resulting app signature and disk image. The custom universal-binary
and manual ZIP scripts are not used by this workflow.

Each job runs the download, Wine diagnostics and portrait console test suites.
The portrait suite is intentionally required: the workflow's base commit alone
does not contain the pending portrait implementation. It must be integrated
before pushing the build branch. No renderer, native game, Wine compatibility,
install, or end-to-end update success is inferred from these tests.

`tools/verify-release-artifacts.py` rejects a wrong package/version/channel,
missing artifact, extra channel feed, or a package hash/size mismatch. A fresh
job must produce one full package. It writes `build-receipt.json` with the exact
source revision, target, channel and SHA256/size of every output. The workflow
uploads each target separately to avoid filename collisions; the operator must
review all three receipts before combining or publishing assets.

## Source and version audit

Read-only remote checks on 2026-10-11 found public `main` and tag `v1.1.5` at
`d2b48fc2bd2b5820b9b945454e004760b28da4a9`. That is a separate initial commit
with no merge base with the reviewed source history ending at
`9ef5f691a239bb1b54dcfd3cc8d29e083128a259`. Public `main` also has no `.github`
directory. A new feature branch preserves both histories; replacing `main` or
force-pushing it is not required. The push trigger avoids the default-branch
requirement of a `workflow_dispatch`-only workflow.

The public release API and update feed showed only 1.1.5, package ID
`FreeRealmsLegacyLauncher`, channel `win`, and Windows artifacts. The workflow
currently stages candidate 1.1.6. Authenticated draft metadata was not available
to this audit without accessing stored credentials and was not queried. Check
draft/tag availability before publication. Separately, the old manual Mac ZIP
helper defaults to 1.3.0; whether that build was distributed is unverified here.
If existing Mac installs report 1.3.0, choose a version above it (for example
1.3.1) before building. Changing only a GitHub release title does not change the
package version.

When publishing, bind the release tag to the exact reviewed source revision from
the artifact receipts. The older `build-all.yml` release command lacks an
explicit target, so it must not be used to create a new release tag against the
unrelated default branch. `release.ps1` is Windows-only and publishes unless
`-Draft` is supplied; this artifact workflow does not call it. Keep the `win`
channel and package ID for existing Windows update compatibility; do not rename
the x64 channel to `win-x64` without an explicit migration.

The new launcher reads its installed version/channel from Velopack. Portable
builds use the downloads page rather than self-updating. The assembly's default
version is separate from the package version; its informational version includes
the source revision for diagnostics.

Validation at preparation: actionlint 1.7.12 accepted both the new workflow and
the corrected existing secret condition; 10/10 artifact verifier tests passed.
Actual hosted Windows/Mac builds and installed update behavior remain pending.

References: [GitHub workflow rules](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax),
[runner labels](https://docs.github.com/en/actions/reference/runners/github-hosted-runners),
[Velopack channels](https://docs.velopack.io/integrating/switching-channels),
[Mac packaging requirement](https://docs.velopack.io/packaging/cross-compiling),
and [public launcher release](https://github.com/raisingkaines/Launcher/releases/tag/v1.1.5).
