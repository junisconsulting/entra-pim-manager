---
name: release
description: Cut an Entra PIM Manager release — preconditions, release notes, the manual test gate, pushing the version tag, and verifying the published assets. Use when asked to release, cut a version, publish a build, or when a release run failed and needs interpreting. Also covers building a Velopack package locally.
---

# Release

A release is a deliberate act: pushing a `v*` tag triggers `.github/workflows/release.yml`, which
builds the Velopack package on `windows-latest` and creates the GitHub release from it. Everything
below exists because the expensive failure mode is not a red build — it is a **green release that
installed clients can never update to**.

## 1. Preconditions — check, do not assume

1. **`main` is green** and the change you are releasing is merged.
2. **Release notes exist** at `packaging/release-notes/{version}.md` — without the leading `v`.
   The workflow throws *before* building if the file is missing. This file is used three times: the
   GitHub release body, the Velopack installer welcome screen, and the in-app update prompt — which
   is why they can never drift apart. Write it before tagging, not after.
3. **The version is not already released.** `gh release list` — tags are not reusable.
4. **Run the `verify` skill** (build, tests, coverage gate).

## 2. Manual test gate — the part CI cannot do

CI proves `Core` behaves. It proves nothing about the WAM broker, the tray, toasts, or the
per-user installer, because those need a real Windows session and a real tenant.

Work through `.claude/manual-test-checklist.md` on Windows before tagging. Treat it exactly like an
integration-test gate: **no explicit pass, no release.** If the session is on Linux or the user has
not confirmed the checklist, say so and stop — do not tag "because CI is green".

## 3. Cut the release

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
- creates the GitHub release with an explicit asset list.

## 4. Verify the published release

The workflow already asserts the asset list, but confirm it on the release page:

| Asset | Why it matters |
| --- | --- |
| `releases.win.json` | **The update feed.** `GithubSource` reads this |
| `RELEASES` | Legacy feed companion — also read by installed clients |
| `Entra-PIM-Manager-win-Setup.exe` | The installer humans download |
| `Entra-PIM-Manager-win-Portable.zip` | Portable variant |
| `Entra-PIM-Manager-{version}-full.nupkg` | Full package the updater pulls |
| `Entra-PIM-Manager-{version}-delta.nupkg` | Optional — absent on the first release after a gap |

**A release with only the `.exe` attached is the worst outcome**: it looks complete, humans can
install it, and every already-installed client silently never sees the update. If the feed files are
missing, fix and re-release rather than leaving it.

Then confirm the release body matches `packaging/release-notes/{version}.md`.

## 5. Interpreting failures

- **`Tag 'x' is not vMAJOR.MINOR.PATCH.`** — the tag format. No suffixes, no `-rc1`; the workflow
  accepts three numeric segments only.
- **`Missing release notes: packaging/release-notes/X.Y.Z.md`** — created the file with a `v`
  prefix, or forgot it. Fix, delete the tag, re-tag.
- **`vpk download github` failed** — the `Fetch previous releases` step is `continue-on-error` on
  purpose. Expected on the first-ever release; the build proceeds without a delta package.
- **No delta package in the output** — harmless; the full package still updates clients.
- **`vpk` version mismatch** — `build.ps1` pins the CLI to the `Velopack` package version read from
  the `.csproj`, uninstalling first because `dotnet tool update` refuses to downgrade. If you bumped
  the Velopack library, the pin follows automatically. Do not hand-install a different `vpk`.
- **Build fails only in the release job** — it runs `dotnet build -c Release -warnaserror` against
  the whole repo including the Avalonia app; `verify` on Linux covers the same ground, so this
  usually means the tag points at a commit older than your fix.

## 6. Building a package locally (optional)

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
- **Local builds are unsigned.** Signing needs `signtool` on Windows. Never promote an unsigned
  artifact to a release; that is what the release workflow is for.

## Out of scope

- Releasing without the manual test checklist.
- Re-pointing or force-pushing an existing tag — cut a new patch version instead.
- Publishing prereleases: the workflow's tag regex does not accept them, by design.
