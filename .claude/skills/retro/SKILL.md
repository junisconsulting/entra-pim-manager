---
name: retro
description: End-of-session retrospective for Entra PIM Manager — sweep the session for learnings and persist each one into its canonical place (skill, CLAUDE.md, engineering backlog, or memory). Use when asked for a retro ("was haben wir gelernt", "retro", "persist learnings"), after sessions with surprises or first-attempt failures, or when the CLAUDE.md Learning Loop rule triggers.
---

# Session Retro

Goal: nothing learned in this session evaporates. Sweep, then persist — each learning into exactly
ONE canonical place. If nothing qualifies, say so and change nothing; forced learnings are noise.

## 1. Sweep the session for four signals

1. **Docs lied or had gaps** — a skill, CLAUDE.md section, or doc claimed X; reality was Y.
2. **First attempt failed non-obviously** — something needed debugging that the next session would
   hit again (SDK/tooling quirks, StyleCop rules, Graph casing, MSAL broker behaviour).
3. **Environment facts not derivable from the repo** — SDK layout on the dev host, tenant state,
   portal behaviour, app-registration settings.
4. **Repeated manual sequences** — the same multi-step dance performed twice or more (candidate for
   a skill extension or a permissions allowlist entry).

## 2. Place each learning (decision rules)

| Learning is… | Goes to | Why |
| --- | --- | --- |
| An always-true rule or decision criterion, expressible in 1–4 lines | `CLAUDE.md` | Loaded every session — but expensive context; keep it short |
| A correction/extension to a procedure | The matching skill in `.claude/skills/*/SKILL.md`, edited in place | Skills are the procedure's single source of truth |
| A PIM Graph or MSAL/WAM fact that changes how code must be written | `entra-pim-graph-api` / `msal-dotnet-desktop-wam` (SKILL.md or their `references/`) | These two are the standing authority against obsolete training data |
| A code defect or deferred refactor | `docs/engineering-backlog.md` (with evidence pointer + what makes the fix safe) | Repo-visible, ordered, reviewable |
| A convention that binds contributors, not just Claude | `CONTRIBUTING.md` | Humans read that file; they do not read CLAUDE.md |
| User preference / dev-host specifics invisible in the repo | Auto-memory (`~/.claude/projects/<project>/memory/` + `MEMORY.md` index) | Persists across sessions — but is Claude-local and per-developer, NOT shared via git |
| Volatile state that will change soon (a running process, a pending PR, today's version) | **Nowhere permanent** | Stale "facts" are worse than none |

Repo artifacts are shared truth (git, visible to every session and every contributor); memory is
personal. When in doubt between the two, prefer the repo artifact — and when a fact moves into the
repo, **delete it from memory** rather than leaving two copies to drift apart.

Everything tracked is English and world-readable (public MIT repo): no tenant IDs, no internal
hostnames, no customer names. Host-specific paths belong in memory, not in the repo.

## 3. Persistence rules

- **Update, don't duplicate**: search for an existing section/entry covering the topic first; correct
  it instead of appending a second version.
- **Smallest diff**: a learning is one correction, not a rewrite of the artifact around it.
- **Delete falsified content**: if the session proved a documented claim wrong, removing/correcting
  it IS the learning. A wrong doc is worse than no doc.
- **One canonical place**: if two artifacts need to know, one gets the content, the other a pointer.

## 4. Verify

- If CLAUDE.md or docs were edited: every backticked path still resolves (`test -e` spot check).
- If a skill was edited: its frontmatter `description` still matches what the skill now does.
- If a `ponytail:` shortcut was left in the code this session: run `/ponytail-debt` so it lands in
  the ledger instead of rotting.
- Report the persisted learnings as a short list (artifact → one-line change). "No learnings this
  session" is a valid, honest result.
