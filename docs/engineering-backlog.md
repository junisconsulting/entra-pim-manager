# Engineering Backlog

Known gaps and deferred work, each with an evidence pointer and what would make the fix safe.
Entries are added by the `retro` skill (see `.claude/skills/retro/`) when a session surfaces a
defect that is real but out of scope for the change at hand. This is not a feature roadmap — the
v1 out-of-scope list lives in `CONTRIBUTING.md`.

---

## Azure role folding is unverified at the scale it was built for

**Evidence:** `ShellViewModel.BuildGroupRows` folds an Azure role held on more than one scope
into one `AzureRoleGroup` node ("Owner · 12 scopes"). The keying was wrong until 0.9.0 — ARM
returns `roleDefinitionId` scope-prefixed, so every scope produced a distinct key and the folding
never fired at all; `RoleDefinitionKey` now compares the trailing GUID only, the same thing
`GetPolicyAsync` already did. That fix is covered by unit tests, but it has only been exercised
against tenants holding a handful of Azure eligibilities. The case it exists for — an
infrastructure team eligible on every subscription of a large estate, several hundred rows — has
not been run against a real tenant. Deliberately deferred past 0.9.0 by the maintainer
(2026-09-06): the tenant that would show it is not available yet, and a defect here is a
follow-up patch, not a release blocker.

**Addressed in 0.9.0, so do not re-diagnose it:** the policy prefetch used to warm one policy per
(role, scope) with no bound — several hundred ARM `roleManagementPolicyAssignments` GETs per
refresh on an estate this size — and `PolicyService.DefaultAfterReadFailure` logged each one's
expected `AuthorizationFailed` as a warning. The prefetch is now capped at
`ShellViewModel.PolicyPrefetchLimit` (pinned rows first, the rest lazily behind the row's spinner),
and that expected code logs at Debug. What is still unverified is the folding itself.

**Why it matters:** two failure modes look alike from a screenshot. If the keying regresses, the
section shows hundreds of flat rows instead of a handful of nodes — the exact problem the folding
was built to solve. If it over-folds, two genuinely different roles collapse into one node and a
user activates at a scope they did not mean to. Note also that the list has **no virtualization
anywhere** (every level is a plain `ItemsControl`, and the search hides rows via `IsVisible`
rather than removing them), so the first expansion of a several-hundred-row section is where any
lag would appear.

**What makes the fix safe:** run `.claude/manual-test-checklist.md` §1d and §3 against a tenant
with an Azure role held on many scopes — one node per role with the right scope count, the scopes
correct inside it, activation hitting the intended scope (verified in the portal), and a role on
a single scope still rendered as a plain row. Watch the first expansion for lag before reaching
for virtualization: the section most likely to go flat into the hundreds is **ADMINISTRATIVE
UNITS**, not Azure, because folding is gated on `Kind == AzureResourceRole` and AU-scoped
directory roles get none. The cheaper fix there is the existing node pattern applied to AU roles,
not a virtualizing rewrite of the list.

---

## Releases are not code-signed — no signing exists anywhere in the pipeline

**Evidence:** `.github/workflows/release.yml` contains no signing step, no certificate secret and
never passes `-SignParams`; `packaging/velopack/build.ps1` only *warns* "Building UNSIGNED". Every
published release to date (0.4.x, 0.5.0) ships unsigned binaries — confirmed in the field
2026-08-14: `Get-AuthenticodeSignature` on the installed 0.5.0 stub and app exe returns
`NotSigned`.

**Why it matters:** managed environments block unknown unsigned executables — observed as a Setup
that "partially succeeds" (files written, launch denied at `CreateProcess`) or an app that
silently never starts. Field evidence 2026-08-14 (Defender events 1121/1122): the ASR rule
"Use advanced protection against ransomware" (`C1DB55AB-C21A-4637-BB3F-A12568109D35`, block mode)
denied a minutes-old unsigned release as "untrusted or unsigned", while the "prevalence, age, or
trusted list" rule fired in audit; a ~12-hour-old unsigned release ran on the same machine
because its hash had accrued cloud reputation — no IT action in between. Consequence: every
fresh release is dead on arrival at such customers for roughly a day, and auto-update stages a
blocked binary that kills the installed app. Signing fixes this durably: the blocking rule's
trigger is literally "untrusted and unsigned", and reputation then attaches to the constant
publisher instead of each release's day-old hash.

**What makes the fix safe:** a junis code-signing certificate (OV/EV) provisioned as a CI secret,
`build.ps1 -SignParams` wired into the release workflow on `windows-latest`, and one release
verified with `Get-AuthenticodeSignature` = `Valid` on Setup.exe, Update.exe, the stub and the app
exe. Then app-control customers can replace per-version hash rules with a single publisher rule.
Until that lands, the docs must say releases are unsigned (they do, as of 2026-08-14) — not imply
the opposite.

---

## Velopack Desktop shortcut suppression is unverified

**Evidence:** `packaging/velopack/build.ps1:97` passes `--shortcuts StartMenuRoot` and its comment
claims this suppresses the Desktop shortcut. Velopack 1.2 marks the `Velopack.Windows.Shortcuts`
runtime API `[Obsolete]` with: *"Desktop and StartMenuRoot shortcuts are now created and removed
automatically when your app is installed / uninstalled."* The flag is therefore likely ignored and
a Desktop shortcut may appear despite the comment.

**Why it matters:** the app is tray-first; an unwanted Desktop icon is a visible defect, and the
comment currently documents behaviour nobody has confirmed.

**What makes the fix safe:** a real installer run on Windows that observes what shortcuts actually
get created. Only then decide between removing the misleading comment, dropping the flag, or
actively removing the Desktop shortcut at first run.

**Do not** simply delete the `CS0618` suppression in
`src/Entra-PIM-Manager.App.Avalonia/Services/ShortcutService.cs` — it is deliberate. The runtime
`Shortcuts` API is the only way to let a user opt out, and it preserves the **AppUserModelId** that
toast notifications depend on; a hand-rolled `.lnk` would lose it.

---

## `Entra-PIM-Manager.slnx` is unusable with SDK 8.0.x

**Evidence:** `dotnet build Entra-PIM-Manager.slnx` on SDK 8.0.421 fails with
`MSB4068: The element <Solution> is unrecognized`. The `.slnx` solution format requires SDK 9.0.2xx
or newer.

**Why it matters:** every local build and the `verify` skill work around it by building the three
`.csproj` files individually. CI is unaffected (it runs `dotnet restore` / `dotnet build` without a
solution argument), so the breakage is invisible there.

**What makes the fix safe:** either pin the repo to an SDK that understands `.slnx` via a
`global.json` (and confirm CI's `setup-dotnet` still resolves), or add a classic `.sln` alongside.
Both are decisions about the supported toolchain, not cleanups — do not change this as a side
effect of another task.

---

## `.claude/manual-test-checklist.md` describes a UI that no longer exists

**Evidence:** the checklist still asks the tester to verify a `ConfigurationWindow` at first start
(§5) and a tray-menu **"Sign out"** / **"Eligible Roles…"** / **"Active Assignments…"** entry (§1,
§4). None of them exist: configuration moved into the Settings slide-in, and `App.axaml:22-38`
defines the menu as Open / Refresh / Settings… / Start with Windows / Exit. The paths in §5 and §7
also predate the 0.4.0 move to `%LocalAppData%\junis\Entra-PIM-Manager\`.

**Why it matters:** the checklist is the release gate — the `release` skill refuses to tag without a
signed-off run. A gate that asks for things that cannot be found trains the tester to wave items
through, which is worse than having no gate.

**What makes the fix safe:** a full pass on Windows against the current UI, rewriting each item to
what is actually on screen. Do this as its own change with the app in front of you, not as a
side effect of a feature — guessing at the wording is how the drift happened in the first place.
Section 1b (sovereign cloud, added in 0.4.2) is current and should be kept as-is.

---

## A device-code account gives no usable signal while its tenant consent is missing

**Evidence:** `MsalAuthService.AcquireForDeviceCodeAccountAsync`
(`src/Entra-PIM-Manager.Core/Auth/MsalAuthService.cs`) renews strictly silently and deliberately
has no interactive fallback — the comment there explains why. Field evidence 2026-09-03/04, while
testing the 0.8.0 scope addition: one device-code enrollment logged
`MsalUiRequiredException / AADSTS65001 "The user or administrator has not consented"` once per
refresh tick for over eight hours (1020 warnings, 22:00:44 → 06:27:49) — the whole window between
the update and the admin consent for the new scope. MSAL answered most of those from its throttle
cache (`MsalThrottledUiRequiredException`). Granting the consent ended it: the refresh token stays
valid, consent is evaluated at token issuance, so silent renewal resumes on its own. The broker
path (`AcquireForAccountAsync`, same file) needs no consent window at all — it catches
`MsalUiRequiredException` and goes interactive.

**Why it matters:** every scope change invalidates the cached grant, so *all* device-code
enrollments start failing at once on the first start after such an update and stay dark until an
admin consents in each tenant — a window that is normal, but indistinguishable from a broken
install. The tenant group shows the generic "Couldn't load eligibilities for this tenant"
(`PimErrorMapper.DescribeFetchFailure` has no arm for `AADSTS65001`), which names neither the
cause nor that waiting for consent is the cure, so the honest reading of the UI is "it is
broken". Broker accounts hide the same window behind an interactive prompt. The genuinely
unrecoverable case is a refresh token that is gone — then only a fresh device-code enrollment
helps, and the app equally never says so.

**What makes the fix safe:** the recovery must not silently become a broker sign-in — that would
defeat the reason the account uses device code (federated IdP, see the comment in
`AcquireForDeviceCodeAccountAsync`). The minimum honest fix is diagnostic, not automatic: map
`AADSTS65001` in `DescribeFetchFailure` to "this tenant has not consented to the app's current
permissions — an admin must grant consent; the account recovers on its own afterwards", and verify
it on a device-code enrollment in a tenant where consent was deliberately withheld. A re-enrollment button
on the failing row is the larger follow-up; it needs the device-code UI flow driven from the
account list, which does not exist yet.

**Status 2026-09-04 (0.9.0):** the diagnostic arm is in (`DescribeFetchFailure` maps
`AADSTS65001`; unit-tested). Still open: the field verification on a withheld-consent tenant, and
the re-enrollment button.

---

## PIM for Azure Resources: assumptions not yet verified against a tenant

**Evidence:** the ARM surface (`PimAzureResourceService`, 0.9.0) was written from the Microsoft
Learn reference on a Linux host without an Azure test tenant. What the unit tests pin is the
contract as documented; these points need one run against a real tenant (the manual checklist
§0/§2/§3/§6 covers them). **Observed 2026-09-04 and no longer open:** the tenant-root `asTarget()`
listing does return management-group *and* subscription scopes in one response, and it does expand
group membership (`memberType: "Group"`, `principalId` = the group). Activation of a
group-inherited subscription role with the user's own oid worked end to end. Still open: whether ARM honours
`$filter=roleDefinitionId eq '…'` on `roleManagementPolicyAssignments` (the service also matches
client-side, so a silently ignored filter only costs payload); whether an eligible-only user may
read that policy at all (if not: `AuthorizationFailed` → default limits, ARM enforces the real
rules on the request); a `SelfActivate` PUT without `scheduleInfo.startDateTime`; the `status`
value a fresh 201 carries; the exact HTTP 400 body of `RoleAssignmentRequestAcrsValidationFailed`;
the consent-prompt behaviour of an already-enrolled broker account in a tenant without the
permission; `management.chinacloudapi.cn` end to end.

**Why it matters:** each of these is a place where the docs and the service could disagree
without any unit test noticing — the symptom would be an empty Azure list, a default-only
activation form, or a 400 on activation.

**What makes the fix safe:** run the checklist against a tenant with one eligible Azure role and
record the observed status values and bodies as fixtures. Anything that differs is a one-line
change in `PimAzureResourceService` plus the fixture.

---

## Narrowed Azure activation: ARM behaviour taken from the docs, not yet observed

**Evidence:** 0.10.0 lets an Azure role held on a management group be activated on management
groups and subscriptions beneath it (`ActivationPanelViewModel`, scope picker fed by
`PimAzureResourceService.GetEligibleChildScopesAsync`, one `SelfActivate` PUT per ticked scope
with the same body as a whole-scope activation). Written on a Linux host from the Microsoft
Learn reference; the unit tests pin the documented contract. What the docs do not settle, each
a small change once a tenant shows the answer:

- **Settled 2026-09-08: `eligibleChildResources` returns direct children only.** At Tenant Root
  Group the management groups came back and none of the subscriptions beneath them, while
  `$filter=resourceType eq 'managementgroup'` was accepted. The app therefore walks the
  hierarchy — one unfiltered read per management group, level by level, in parallel within a
  level — and keeps the type check client-side. Still assumed: that an unfiltered read at a
  management group lists nothing but management groups and subscriptions (anything else is
  dropped, so the cost would be payload, not a wrong scope).
- **Which role settings ARM evaluates.** Role settings are per (role, scope) and not inherited.
  The panel uses the policy at the eligibility's scope (maximum duration, justification, ticket,
  approval, authentication context). If ARM evaluates the policy at the chosen child scope
  instead, a request can be refused for a limit the form never showed —
  `RoleAssignmentRequestPolicyValidationFailed` / `ExpirationRule` for a shorter maximum, or
  HTTP 400 `RoleAssignmentRequestAcrsValidationFailed` when the child demands an authentication
  context the management group does not, in which case every retry fails the same way. The fix
  for the latter is a reactive retry with the claims from the 400 body (the message carries
  them URL-encoded), which would also rescue eligible-only users whose policy read comes back
  `AuthorizationFailed`; a per-scope policy read before submit only helps users allowed to read it.
- **The `roleDefinitionId` prefix at the child scope.** The request passes the eligibility's
  management-group-prefixed id verbatim, the only valid form for a custom role defined there. If
  ARM insists on a child-scope prefix for built-in roles, the error will name it.
- **Whether ARM needs `linkedRoleEligibilityScheduleId` beneath the eligibility's scope.** The
  reference calls it optional ("the system will pick a RoleEligibilitySchedule automatically")
  and the app does not send it: for an eligibility inherited through a group the schedule's
  principal is the group while `principalId` is the user, and whether ARM accepts that pairing
  is unobserved. If a narrowed PUT fails with an eligibility-not-found style error, add it back
  from the last segment of the instance's `properties.roleEligibilityScheduleId`.
- **How the activated row reads back.** `roleAssignmentScheduleInstances?$filter=asTarget()`
  should list it at the child scope. `ShellViewModel.PendingMatchKey` compares Azure rows by
  definition GUID and case-insensitive scope, so the pending placeholder is replaced and the
  eligibility row dimmed whatever prefix or casing the real row carries.

**Why it matters:** the first point decides whether the feature works at all on a nested estate;
the second decides whether a narrowed activation can be refused for a limit the form never showed.

**What makes the fix safe:** run `.claude/manual-test-checklist.md` §3 (narrowed activation) on a
tenant with an eligibility on a management group that has subscriptions under a child management
group. Record the child-scopes response and the PUT response as fixtures next to
`arm-eligible-child-scopes.json`. Then turn the "unobserved" notes in the `azure-rbac-pim-arm-api`
skill (call 6) into observed facts.

---

## No proactive MFA step-up for ARM's MfaRule

**Evidence:** an Azure role whose settings require MFA (without an authentication context) is
rejected with `RoleAssignmentRequestPolicyValidationFailed ["MfaRule"]` when the ARM token was
issued without MFA; `PimErrorMapper` maps it to StepUpRequired and the user re-authenticates and
retries. This is parity with the Graph path, which handles the equivalent `MfaRuleViolated` the
same way.

**Why it matters:** one extra round trip and a confusing message for users whose sign-in did not
involve MFA.

**What makes the fix safe:** find out which claims request satisfies MfaRule on ARM (an `acrs`
value from a Conditional Access authentication context is the documented mechanism; a bare
`amr: mfa` claims request is not) before wiring anything — and keep it on the ARM token only.

---

## No per-tenant opt-out for the Azure surface

**Evidence:** `EligibilityAggregator.FetchAzureSafeAsync` backs the ARM surface off for an hour
after a failed token acquisition; a broker account in a tenant that never consents to Azure
Service Management still sees one WAM consent prompt per app start. Chosen deliberately for
0.9.0 (the 0.8.0 precedent accepted the same for a Graph scope).

**Why it matters:** a tenant that uses PIM only for directory roles has no Azure roles to show
and no reason to consent.

**What makes the fix safe:** a boolean on `TenantAppRegistration` (default on), read by the
aggregator before it touches ARM, validated like the other fields — only if a tenant actually
asks; until then the backoff is the answer.

## Tooltips inside the Settings account row never fire

**Evidence:** the account row's content `Grid` (`TrayPopupWindow.axaml`, inside the
`SelectAccountCommand` button) carries `IsHitTestVisible="False"` so the row click reaches the
button. Every `ToolTip.Tip` declared inside it — the tenant GUID on the tenant line, the
explanation on the "Device code" badge — therefore never receives a pointer and never shows.
Noticed while adding the per-account alias in 0.9.0; left untouched to keep that diff surgical.

**Why it matters:** the device-code badge is the only place the app explains why such an account
cannot satisfy a Conditional Access authentication context, and that explanation is unreachable.

**What makes the fix safe:** move both tooltips onto the enclosing `Button` (which is hit-testable)
and check the row still selects the account on click and still starts a drag from the handle — the
button's pointer capture is what the drag handle exists to work around.

## The same Azure role at the same scope can appear twice

**Evidence:** observed 2026-09-04 — an account eligible for `Owner` on a management group both
directly and through a group gets two rows from ARM's `asTarget()` listing, identical in the UI
(`memberType` is `Direct` on one and `Group` on the other, and the app carries neither). Listed as
out of scope when 0.9.0 was planned; now seen in a real tenant.

**Why it matters:** the rows are indistinguishable, so the user cannot tell why they hold the role
twice or which assignment expires when. It is cosmetic beyond that: `MarkActiveEligibilities`
(`ShellViewModel.cs`) keys on (kind, resourceId, scopeId, oid, tenantId), so activating either row
marks **both** active and disables both — there is no double-activation trap, and the activation
form is identical because the policy is per (role, scope).

**What makes the fix safe:** carry `memberType` and, for `Group`, the group's display name from
`expandedProperties.principal` into `PimEligibility`, and show it as a source line ("via
grp-az-…"). That keeps both rows, which is the honest representation — a de-duplication would
silently drop the fact that two independent assignments exist. Only worth it once a customer sees
it outside a test.

## Administrative units are named by object id, not display name

**Evidence:** added in 0.9.0 — `PimEligibility.IsAdministrativeUnitScoped` splits AU-scoped
directory roles into their own section, and the row reads `Administrative unit:
{3f6b1c9e-…}`. Before this, an AU-scoped role was indistinguishable from the tenant-wide role of
the same name; the id is a strict improvement, but it is not a name.

**Why it matters:** an admin with roles on several units has to map GUIDs by hand, and the search
box can only match the id. The information is there — Graph exposes `directoryScope` as a
navigation property on `unifiedRoleEligibilityScheduleInstance` — so this is about permission, not
plumbing.

**What makes the fix safe:** establish first whether `$expand=directoryScope` (or a separate
`/directory/administrativeUnits/{id}` read) is covered by the scopes already consented to. If it
needs `AdministrativeUnit.Read.All`, that is a **new delegated permission and therefore a decision,
not a detail** — it requires admin consent in every tenant again, and 0.9.0 already spends that
budget on Azure Service Management. The `entra-pim-graph-api` skill warns that `$expand` fails
silently on some PIM surfaces, so verify against a live tenant before relying on it, and keep the
id as the fallback the way `TenantLabelFormatter` falls back to the tenant GUID.
