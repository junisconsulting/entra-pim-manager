---
name: entra-pim-graph-api
description: Reference for working with the Microsoft Entra PIM (Privileged Identity Management) Graph API for both directory roles and PIM for Groups. Use this skill whenever code interacts with PIM endpoints — listing eligible assignments, activating or deactivating roles, querying policies, building self-service activation flows, or auditing PIM activations. Also use this when handling PIM-specific error responses, navigating the casing inconsistencies between Directory Role and Group endpoints, or constructing requests with filterByCurrentUser. Critical for any PowerShell, C#, JavaScript, or other code that calls graph.microsoft.com/*/roleManagement/* or graph.microsoft.com/*/identityGovernance/privilegedAccess/group/*. Do not rely on memory for PIM Graph endpoints — read this skill first.
---

# Entra PIM Graph API

This skill provides verified endpoint references and patterns for the Microsoft Entra Privileged Identity Management (PIM) Graph API. PIM is split into two parallel API surfaces — one for directory roles and one for groups — and the two are gratuitously inconsistent. This skill captures both.

## When to use

Load this skill any time the work involves:

- Listing a user's eligible PIM role assignments (directory roles OR groups)
- Activating or deactivating PIM roles
- Reading PIM policies (max duration, required justification, MFA, approval)
- Building self-service activation UIs, scripts, or automations
- Handling PIM-specific Graph API errors
- Auditing or reporting on PIM activations
- Cleanup or governance scripts touching PIM assignments

Don't write PIM Graph code from memory. Endpoints are nontrivially inconsistent between the two surfaces, and outdated snippets in the wild — including from Microsoft's own older blog posts — are common.

## Two parallel API surfaces

PIM exposes two separate endpoint trees:

| Surface | Base path | What it manages |
|---|---|---|
| **Directory Roles PIM** | `/v1.0/roleManagement/directory/*` | Entra (formerly Azure AD) built-in and custom roles, applied at tenant or administrative-unit scope |
| **PIM for Groups** | `/v1.0/identityGovernance/privilegedAccess/group/*` | Eligible/active membership and ownership of any group (role-assignable or otherwise) |

A user activating "Global Administrator" hits the first. A user activating "member of grp-tier0-emergency" hits the second. **Both flows can coexist** for the same user, and a UI listing eligibilities must query both surfaces and merge results.

## The five essential calls

### 1. List my eligible directory roles

```http
GET /v1.0/roleManagement/directory/roleEligibilityScheduleInstances/filterByCurrentUser(on='principal')
    ?$expand=roleDefinition
```

Returns schedule instances. Map: `roleDefinitionId`, `directoryScopeId`, `endDateTime`, `roleDefinition.displayName`.

### 2. List my eligible group memberships/ownerships

```http
GET /v1.0/identityGovernance/privilegedAccess/group/eligibilityScheduleInstances/filterByCurrentUser(on='principal')
```

Returns instances with `groupId`, `accessId` (`member` or `owner`), `endDateTime`. Resolve `groupId` to a display name via a separate `/groups` batch call — `$expand=group` is unreliable here. See `references/stolperfallen.md`.

### 3. Activate a directory role

```http
POST /v1.0/roleManagement/directory/roleAssignmentScheduleRequests
Content-Type: application/json

{
  "action": "selfActivate",
  "principalId": "<my user object id>",
  "roleDefinitionId": "<from eligibility>",
  "directoryScopeId": "<from eligibility, often '/'>",
  "justification": "<required if policy demands>",
  "scheduleInfo": {
    "startDateTime": "<ISO 8601, now or future>",
    "expiration": {
      "type": "AfterDuration",
      "duration": "PT4H"
    }
  },
  "ticketInfo": {
    "ticketNumber": "<if policy demands>",
    "ticketSystem": "<if policy demands>"
  }
}
```

### 4. Activate a group membership/ownership

```http
POST /v1.0/identityGovernance/privilegedAccess/group/assignmentScheduleRequests
Content-Type: application/json

{
  "accessId": "member",
  "principalId": "<my user object id>",
  "groupId": "<from eligibility>",
  "action": "selfActivate",
  "scheduleInfo": {
    "startDateTime": "<ISO 8601, now or future>",
    "expiration": {
      "type": "afterDuration",
      "duration": "PT4H"
    }
  },
  "justification": "<required if policy demands>"
}
```

**⚠️ Note the casing**: Directory roles use `"AfterDuration"` (PascalCase). Groups use `"afterDuration"` (camelCase). This is a real, unfixed inconsistency in the API. Define constants per service — do NOT share the literal between code paths.

### 5. Read the policy for a role or group

```http
# For a directory role
GET /v1.0/policies/roleManagementPolicyAssignments
    ?$filter=scopeId eq '/' and scopeType eq 'Directory' and roleDefinitionId eq '<id>'
    &$expand=policy($expand=rules)

# For a PIM-managed group
GET /v1.0/policies/roleManagementPolicyAssignments
    ?$filter=scopeId eq '<groupId>' and scopeType eq 'Group'
    &$expand=policy($expand=rules)
```

Parse the `policy.rules` array. The rule IDs you typically care about: `Expiration_EndUser_Assignment`, `Enablement_EndUser_Assignment`, `Approval_EndUser_Assignment`. See `references/policy-rules.md` for the full schema.

## Self-deactivation

Same activation endpoints, just change `action` to `"selfDeactivate"`. No justification required. Returns immediately. Useful as a "give back privilege early" button in self-service UIs.

## Listing active (currently-activated) assignments

To show the user what they have active right now:

```http
# Directory roles currently active (including PIM-activated)
GET /v1.0/roleManagement/directory/roleAssignmentScheduleInstances/filterByCurrentUser(on='principal')
    ?$expand=roleDefinition
    &$filter=assignmentType eq 'Activated'

# Group memberships/ownerships currently active
GET /v1.0/identityGovernance/privilegedAccess/group/assignmentScheduleInstances/filterByCurrentUser(on='principal')
    ?$filter=assignmentType eq 'Activated'
```

`endDateTime` gives the expiry — use it for countdown UI.

## Required delegated scopes

For a self-service activation UI, request the union of:

```
User.Read
RoleEligibilitySchedule.Read.Directory
RoleAssignmentSchedule.ReadWrite.Directory
RoleManagementPolicy.Read.Directory
PrivilegedAccess.ReadWrite.AzureADGroup
Group.Read.All
```

All except `User.Read` require admin consent — they're classified as privileged and users cannot self-consent.

For read-only scenarios (e.g., reporting), substitute `*.Read.*` variants:
- `RoleAssignmentSchedule.Read.Directory` (instead of ReadWrite)
- `PrivilegedAccess.Read.AzureADGroup` (instead of ReadWrite)

## Common patterns

### Unified eligibility list across both surfaces

When showing the user all their eligibilities in one UI:

1. Call both list endpoints in parallel (`Task.WhenAll` in .NET).
2. Collect `groupId` values from the group eligibilities, batch them via `/groups?$select=id,displayName,isAssignableToRole&$filter=id in (...)`.
3. Merge into a unified model with a discriminator (e.g., `Kind = DirectoryRole | GroupMembership | GroupOwnership`).
4. Cache policy lookups per-resource for ~10 minutes to avoid throttling.

### Activation request flow

1. User picks an eligibility.
2. Fetch the policy for that resource (cached).
3. Render UI with max-duration slider, justification field (required iff policy demands), ticket fields (required iff policy demands).
4. POST the activation request.
5. Read `status` from response:
   - `Provisioned` → role active immediately
   - `Granted` (with future `startDateTime`) → scheduled
   - `PendingApproval` → awaiting approver
6. Refresh active-assignments list.

### Throttling & retries

PIM endpoints are throttled. Honour `Retry-After` headers on `429`. Recommend Polly with exponential backoff and jitter, capped at 3 retries. Don't retry POST activation requests on `400` — they indicate validation failures, not transient errors.

## Reference files

Load these when going deeper:

- **`references/endpoints.md`** — Full endpoint catalog including list operations, request inspection, approval endpoints, and beta-only operations
- **`references/stolperfallen.md`** — The complete list of API quirks: casing inconsistencies, `$expand` failures, scope-ID requirements, group onboarding side effects
- **`references/error-codes.md`** — Activation HTTP 400/403/409 error code catalog with user-friendly message mappings (German + English)
- **`references/policy-rules.md`** — Schema of role management policy rules with which fields to parse for each rule type

## Things you must know before writing PIM code

1. **`AfterDuration` vs `afterDuration`** — Directory: PascalCase. Groups: camelCase. Not a typo, the actual API contract.
2. **`filterByCurrentUser` is a function call**, not a filter. URL must contain `(on='principal')` literally. `principal` is the string value.
3. **`$expand=group` on PIM-for-Groups endpoints often fails silently** or returns `null` — always do a separate `/groups` lookup.
4. **`directoryScopeId` must be passed through exactly** as received from the eligibility. Don't normalize, don't hardcode `"/"`.
5. **PIM-for-Groups onboards groups implicitly** on first API call. Policy IDs change post-onboarding — never cache policy IDs across app restarts.
6. **`action` values are camelCase** for both surfaces: `selfActivate`, `selfDeactivate`, `adminAssign`. Not `SelfActivate`.
7. **Response `status` is independent of HTTP status** — HTTP 201 can return `PendingApproval`, `Granted` (future), or `Provisioned` (immediate). Always parse the body.
8. **Group PIM activation does NOT directly activate Entra roles** — it activates group membership/ownership. Role bindings flow from the group's role assignments, if any.
