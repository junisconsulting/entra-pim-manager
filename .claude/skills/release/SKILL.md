---
name: release
description: Cut an Entra PIM Manager release — preconditions, release notes, choosing the version number (major/minor/patch rules), the manual test gate, pushing the version tag, and verifying the published assets. Use when asked to release, cut a version, decide a version number, publish a build, or when a release run failed and needs interpreting. Also covers building a Velopack package locally.
---

# Release

A release is a deliberate act: pushing a `v*` tag triggers `.github/workflows/release.yml`, which
builds the Velopack package on `windows-latest` and creates the GitHub release from it. Everything
below exists because the expensive failure mode is not a red build — it is a **green release that
installed clients can never update to**.

## 1. Preconditions — check, do not assume

1. **`main` is green** and the change you are releasing is merged.
2. **Release notes exist** at `packaging/release-notes/{version}.md` — without the leading `v`.
   The workflow throws *before* building if the file is missing. This file is used four times: the
   GitHub release body, the Velopack installer welcome screen, the in-app update prompt, and — since
   0.9.0 — the what's-new window, which reads it as an embedded resource (`ReleaseNotes.{version}.md`,
   see the `EmbeddedResource` item in the app `.csproj`). That is why they can never drift apart, and
   why the file has to exist **before** `vpk pack`: a build packed without it shows no window at all.
   Write it before tagging, not after.
3. **The version is not already released.** `gh release list` — tags are not reusable.
4. **Run the `verify` skill** (build, tests, coverage gate).
5. **A non-empty `JunisHostedClientId`** in `src/Entra-PIM-Manager.App.Avalonia/appsettings.json`
   — and the commit that puts it on `main` — needs the junis-hosted gate first: the registration
   exists and is operated as in `SECURITY.md` ("The junis-hosted App Registration"), that section is
   published, the consent screen shows the verified publisher "junis GmbH", the consent page
   (`EntraPimManagerOptions.JunisConsentPage`) is live from the Pages workflow, its `github-pages`
   environment requires a reviewer, it is registered on the app as its Web redirect URI, and
   checklist §1e has passed in test tenants. The same commit removes the "available from the
   release that ships the junis client id" notes from README.md, both user guides,
   `docs/app-registration-setup.md` and `docs/unattended-deployment.md`, and publishes the id in
   SECURITY.md. Why: `docs/adr/0002-junis-hosted-app-registration.md`.

## 2. Manual test gate — the part CI cannot do

CI proves `Core` behaves. It proves nothing about the WAM broker, the tray, toasts, or the
per-user installer, because those need a real Windows session and a real tenant.

Work through `.claude/manual-test-checklist.md` on Windows before tagging. Treat it exactly like an
integration-test gate: **no explicit pass, no release.** If the session is on Linux or the user has
not confirmed the checklist, say so and stop — do not tag "because CI is green".

## 3. Choose the version — rules, not gut feeling

Semantic versioning, translated for an end-user identity tool (there is no public API; the
"contract" is what admins and users must do or notice). Judge the **whole diff since the last
release** and take the **highest** rule that applies:

| Bump | Rule | Examples |
| --- | --- | --- |
| **MAJOR** (1.x.y → 2.0.0) | The update demands human action outside the app before it fully works again | New or changed Graph scope (admin consent in *every* tenant), config change without automatic migration, raised minimum OS, changed install model |
| **MINOR** (x.4.y → x.5.0) | Users get a new capability or a changed workflow — no action required | New settings section or diagnostic panel, additional cloud, new activation option, reworked UI flow |
| **PATCH** (x.y.2 → x.y.3) | Same capabilities, just working better | Bug fixes, resilience/performance, dependency bumps, corrected texts or logs |

Two questions decide almost every case: *"Must an admin or user **do** anything after this
update?"* → MAJOR. *"Will they **notice** something new?"* → MINOR. Neither → PATCH.

Edge rules:

- **Mixed releases take the highest bump.** One feature plus five fixes is a MINOR.
- **Security fixes are a PATCH** and ship promptly — unless the fix itself demands action, which
  makes it MAJOR like any other action-required change.
- **Pre-1.0:** the same rules apply to MINOR/PATCH, but `1.0.0` is never reached by counting — it
  is the deliberate "production-ready" declaration. An action-required change during 0.x bumps
  MINOR, with the required action as the *first line* of the release notes.
- The `-local.<timestamp>` versions from the `verify` skill are mechanical (always next PATCH) and
  never released; the decision above happens only here, at tag time.

## 4. Cut the release

```bash
git tag v0.4.2
git push origin v0.4.2
```

That is the whole trigger. The workflow does the rest:

- derives the version from the tag (rejects anything that is not `vMAJOR.MINOR.PATCH`),
- requires the release-notes file,
- builds and tests,
- pulls the previous release feed so Velopack can generate a **delta** package (a few hundred KB
  instead of a ~65 MB full download),
- packs via `packaging/velopack/build.ps1`,
- wraps that Setup.exe into the Intune package via `packaging/intune/build.ps1`,
- creates the GitHub release with an explicit asset list.

## 5. Verify the published release

The workflow already asserts the asset list, but confirm it on the release page:

| Asset | Why it matters |
| --- | --- |
| `releases.win.json` | **The update feed.** `GithubSource` reads this |
| `RELEASES` | Legacy feed companion — also read by installed clients |
| `Entra-PIM-Manager-win-Setup.exe` | The installer humans download |
| `Entra-PIM-Manager-win-Portable.zip` | Portable variant |
| `Entra-PIM-Manager-win-Setup.intunewin` | Intune Win32 app (Setup.exe plus the endpoint scripts) admins upload into their tenant. Checking that it exists is the whole per-release gate; the Intune end-to-end run is checklist 5e, done once |
| `Entra-PIM-Manager-{version}-full.nupkg` | Full package the updater pulls |
| `Entra-PIM-Manager-{version}-delta.nupkg` | Optional — absent on the first release after a gap |

**A release with only the `.exe` attached is the worst outcome**: it looks complete, humans can
install it, and every already-installed client silently never sees the update. If the feed files are
missing, fix and re-release rather than leaving it.

Then confirm the release body matches `packaging/release-notes/{version}.md`.

## 6. Interpreting failures

- **`Tag 'x' is not vMAJOR.MINOR.PATCH.`** — the tag format. No suffixes, no `-rc1`; the workflow
  accepts three numeric segments only.
- **`Missing release notes: packaging/release-notes/X.Y.Z.md`** — created the file with a `v`
  prefix, or forgot it. Fix, delete the tag, re-tag.
- **`vpk download github` failed** — the `Fetch previous releases` step is `continue-on-error` on
  purpose. Expected on the first-ever release; the build proceeds without a delta package. On any
  later release, read the step log: `403 (rate limit exceeded)` means the call went out without
  `--token` and hit the anonymous per-IP API quota of the shared runner — seen on v0.13.0, fixed by
  passing `github.token`. The release is still valid; clients just download the full package.
- **No delta package in the output** — harmless; the full package still updates clients.
- **`IntuneWinAppUtil.exe does not match the pinned SHA256`** — the packer downloaded from
  Microsoft's repo is not the reviewed build. Do not just paste the new hash: check the upstream
  tag first, and if Microsoft published a new version, bump version and hash together in
  `packaging/intune/build.ps1`.
- **`vpk` version mismatch** — `build.ps1` pins the CLI to the `Velopack` package version read from
  the `.csproj`, uninstalling first because `dotnet tool update` refuses to downgrade. If you bumped
  the Velopack library, the pin follows automatically. Do not hand-install a different `vpk`.
- **Build fails only in the release job** — it runs `dotnet build -c Release -warnaserror` against
  the whole repo including the Avalonia app; `verify` on Linux covers the same ground, so this
  usually means the tag points at a commit older than your fix.

**Signing** ([ADR 0003](../../../docs/adr/0003-code-signing.md)). The job runs in the GitHub
environment `release` (admits `v*` tags and `main`): secrets `AZURE_CLIENT_ID` (the managed identity)
and `AZURE_TENANT_ID` (junis), variables `SIGNING_ENDPOINT`, `SIGNING_ACCOUNT`, `SIGNING_PROFILE`.
The repo uses immutable OIDC subjects, so the identity's federated credential subject is
`repo:junisconsulting@159626871/entra-pim-manager@1254988551:environment:release`. Before a release
after any change on the Azure or GitHub side, run the workflow by hand on `main`: a dry run builds,
signs and verifies, and publishes nothing. With `keep_setup` it keeps the signed Setup.exe
(`0.0.1-dryrun.N`) for one day; test it in Windows Sandbox, not over a real install, and delete the
artifact once downloaded (`gh api -X DELETE repos/junisconsulting/entra-pim-manager/actions/artifacts/<id>`).

- **`azure/login` fails with `AADSTS700213` / `AADSTS70021`** — no federated credential matches
  the token: compare the subject above, including the IDs, with the credential.
- **`No token for Artifact Signing.`** — the login worked, but the token request failed; read the
  `az` error above it.
- **Signing fails with 403 / `SignerSign()`** — the identity lacks *Artifact Signing Certificate
  Profile Signer* on the profile, or `SIGNING_ENDPOINT` does not match the account's region.
- **`Verify signatures` fails** — vpk packed something unsigned; the step lists the files. Never
  bypass the step: a release is signed or it does not ship.

## 7. Building a package locally (optional)

On Windows:

```powershell
pwsh ./packaging/velopack/build.ps1 -Version 0.4.2
```

On Linux, `vpk` cross-compiles with the `[win]` OS directive — useful for inspecting the package
format without a Windows machine. The command and the shell-quoting trap are documented in
`packaging/velopack/README.md`.

Two traps:

- **`-p:Version=` is not optional.** The repo carries no `<Version>` property, so without the flag
  the assembly silently gets `1.0.0` — the app footer and Explorer's Details tab then disagree with
  the release. `--packVersion` must match it.
- **Local builds are unsigned; only the release workflow signs** (Artifact Signing, see "Signing"
  in section 6). Every release up to 0.13.0 shipped unsigned (field-confirmed 2026-08-14), and
  app-control environments (AppLocker/WDAC/EDR) block such builds unless the customer allows each
  version's hashes. A local Setup.exe stays in that category.

## Out of scope

- Releasing without the manual test checklist.
- Re-pointing or force-pushing an existing tag — cut a new patch version instead.
- Publishing prereleases: the workflow's tag regex does not accept them, by design.
