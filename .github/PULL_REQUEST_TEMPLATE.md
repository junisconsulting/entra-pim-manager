## Summary

What this PR does and why. One paragraph is usually enough; link related issues with `Closes #N`.

## Type of change

- [ ] `feat` — new feature
- [ ] `fix` — bug fix
- [ ] `refactor` — code change that neither fixes a bug nor adds a feature
- [ ] `docs` — documentation only
- [ ] `test` — adding or correcting tests
- [ ] `chore` — build, tooling, dependencies

## Test plan

How you verified this change. Be specific:

- [ ] `dotnet build -c Release -warnaserror` passes
- [ ] `dotnet test` passes
- [ ] Manual verification (describe the steps and the tenant setup, if applicable)

## Checklist

- [ ] Code follows the [contributing conventions](../CONTRIBUTING.md) (language, layering, async, nullable, security)
- [ ] No tokens, justifications, or PII in logs added by this change
- [ ] No hardcoded tenant or client IDs
- [ ] Tests added/updated for new behavior in `Entra-PIM-Manager.Core`
- [ ] Coverage gate on `Entra-PIM-Manager.Core` still ≥ 70 % lines

## Screenshots / recordings

For UI changes — before/after screenshots or a short clip.
