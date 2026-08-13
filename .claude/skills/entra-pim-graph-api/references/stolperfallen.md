# PIM Graph API — Stolperfallen (Quirks & Gotchas)

The PIM API has accumulated inconsistencies over its lifetime. This is the authoritative list of things that surprise people — refer here when something fails in a way that doesn't make sense.

## Casing inconsistencies

### `AfterDuration` vs `afterDuration`

The `scheduleInfo.expiration.type` enum value is cased differently between the two surfaces:

| Surface | Value |
|---|---|
| Directory Roles | `"AfterDuration"` (PascalCase) |
| PIM for Groups | `"afterDuration"` (camelCase) |

Other values: `"AfterDateTime"` / `"afterDateTime"` follow the same split. `"NoExpiration"` / `"noExpiration"` only valid for admin assignments, not self-activation.

**Fix in code**: Define them as constants per service. Don't share a single `ExpirationType` enum across both layers.

### Action values are always camelCase

For BOTH surfaces:
- `selfActivate`, `selfDeactivate`
- `adminAssign`, `adminUpdate`, `adminRemove`, `adminExtend`, `adminRenew`
- `selfExtend`, `selfRenew`

Not `SelfActivate`, not `Self_Activate`. Microsoft's older PowerShell SDK normalized this, leading to confusion when calling the raw API.

### `accessId` is lowercase
`"member"`, `"owner"`. Not `"Member"`, not `"MEMBER"`.

---

## `filterByCurrentUser` is a function call

It's not an OData filter — it's a bound function. The URL must contain `(on='principal')` literally:

```
✅ /roleEligibilityScheduleInstances/filterByCurrentUser(on='principal')
❌ /roleEligibilityScheduleInstances?filterByCurrentUser=principal
❌ /roleEligibilityScheduleInstances/filterByCurrentUser
```

`principal` is the literal string value of the `on` parameter. The other documented value is `'subject'` which is functionally the same in this context. Quotes around `'principal'` are part of the URL — single quotes, not double.

---

## `$expand=group` is broken for PIM-for-Groups

When listing PIM-for-Groups eligibility or assignment instances, you would expect:

```
GET .../group/eligibilityScheduleInstances/filterByCurrentUser(on='principal')?$expand=group
```

…to return the group's properties expanded. **It doesn't, consistently.** Microsoft has acknowledged this. Response often comes back with `group: null` or simply missing the field.

**Workaround**: Collect all `groupId` values from the response, do a separate batch lookup:

```
GET /groups?$filter=id in ('id1','id2','id3')&$select=id,displayName,isAssignableToRole
```

Or via `/$batch` for larger sets.

`$expand=roleDefinition` on the directory side **does** work reliably.

---

## `directoryScopeId` must be passed through exactly

The eligibility response gives you a `directoryScopeId` that can be:
- `"/"` — tenant scope
- `"/administrativeUnits/{auId}"` — restricted to an AU
- `"/applications/{appId}"` — restricted to an app (for App Admin scope)

When you POST the activation request, this MUST match exactly what was in the eligibility. Don't normalize, don't trim, don't hardcode `"/"`.

Hard-coding `"/"` while the eligibility was AU-scoped will fail with HTTP 400 `InvalidScope`.

---

## PIM-for-Groups onboards groups implicitly

A group "becomes PIM-managed" only when someone first creates an eligibility/assignment for it via the API. Before that, the group exists as a regular Entra group but isn't visible in PIM endpoints.

Implications:
- If you GET policies for a group that isn't yet PIM-onboarded, you may get an empty result or a default-template policy ID.
- After onboarding, the **policy IDs change**. If you cached a policy ID from a pre-onboarded state, it's stale.
- **Never cache policy IDs across application restarts**. Cache the policy *content* if needed, but always re-resolve the assignment ID.

---

## Activation `status` is independent of HTTP status

The HTTP response for a successful POST is `201 Created`, but the `status` field in the body can be:

| Status | Meaning |
|---|---|
| `Provisioned` | Role is active right now |
| `Granted` | Scheduled — `startDateTime` is in the future |
| `PendingApproval` | Waiting for an approver |
| `PendingScheduleCreation` | Initial state, will transition |
| `Denied` | Approver rejected |
| `Failed` | Provisioning error |
| `Revoked` | Was active, now ended |

A UI that only checks HTTP status will incorrectly tell the user "activation successful" for a `PendingApproval`. **Always parse the body.**

---

## `principalId` is the user OID, not the UPN

`principalId` must be the user's Entra object ID (GUID). Not the UPN, not the email, not the on-prem SID.

Get it from `/me` or from MSAL's `AuthenticationResult.Account.HomeAccountId.ObjectId`.

---

## Group-via-Role eligibility shows the GROUP as principal

When a user is eligible for a role *because they're eligible for membership of a role-assignable group*, the directory-role eligibility endpoint shows the GROUP as the principal, not the user.

Example:
- Bob is eligible for membership of `grp-tier1-admins`
- `grp-tier1-admins` is permanently assigned the "User Administrator" role
- Bob's directory role eligibilities (`filterByCurrentUser(on='principal')`) → **does NOT show User Administrator**
- Bob's group eligibilities → shows `grp-tier1-admins` membership

For a UI listing "everything Bob can activate", you need to query BOTH surfaces and merge. Don't try to traverse from group eligibilities to derived role bindings — that's brittle and policy-dependent.

For role-assignable groups, flag them in UI with a warning: "Activating this group will grant `<role name>` via the group's role assignment." Compute by fetching the group's own role assignments separately.

---

## Group PIM activation does NOT activate the role

Activating membership of a role-assignable group:
1. Makes the user a member of the group (time-limited)
2. The role assignment to the group is **independent** — it must itself be either permanent (`Assigned`) or PIM-activated separately

If the group has an `Eligible` role assignment (not `Assigned`), activating group membership alone won't grant the role. The role activation is a separate step on the group as principal.

In practice, most topologies use `Assigned` for the group-to-role binding and `Eligible` for the user-to-group binding. Verify per-group.

---

## `ticketInfo` only exists for directory roles

The PIM-for-Groups v1.0 activation request body has NO `ticketInfo` field. If the group's PIM policy requires ticketing, that's surfaced via a different mechanism that's inconsistently exposed across the API.

For v1: in PIM-for-Groups activation, embed any ticket reference in the `justification` text as a workaround. Document this clearly in UI.

---

## HTTP 400 doesn't tell you which rule failed in the field

A `400 Bad Request` on activation might be:
- Justification too short or missing
- Ticket info missing
- MFA not satisfied
- Duration exceeds max
- Approval required (returned as 400 in some tenants instead of 201+PendingApproval)

The response body's `error.code` is the only reliable discriminator. See `error-codes.md`.

---

## Throttling: respect `Retry-After`

PIM endpoints throttle aggressively, especially `roleEligibilityScheduleInstances` and policy lookups. On 429, the response includes `Retry-After: <seconds>`. Respect it. Polly's policy:

```csharp
.WaitAndRetryAsync(3, (retry, ctx) =>
{
    if (ctx.TryGetValue("Retry-After", out var ra) && int.TryParse(ra.ToString(), out var s))
        return TimeSpan.FromSeconds(s);
    return TimeSpan.FromSeconds(Math.Pow(2, retry));
});
```

Don't blanket retry POSTs — activation requests should fail-fast on 400/403/409 and only retry on 429/503.

---

## Schedules vs. Schedule Instances vs. Schedule Requests

PIM has three related concepts that confuse people:

| Concept | Endpoint suffix | What it represents |
|---|---|---|
| **Schedule Request** | `...Requests` | A request that was made (audit trail, history) |
| **Schedule** | `...Schedules` | The current "rule" — policy view |
| **Schedule Instance** | `...ScheduleInstances` | A materialized time-slice of a schedule |

**For UI listing of "what can I activate"**: use `...EligibilityScheduleInstances`.
**For UI listing of "what is currently active"**: use `...AssignmentScheduleInstances`.
**For audit/history**: use `...Requests`.
**Avoid**: `...Schedules` unless you specifically need the policy-level view.

---

## SDK version notes

- **Microsoft.Graph C# SDK v5.x**: First-class support for PIM types via `UnifiedRoleAssignmentScheduleRequest` etc. Use this.
- **Microsoft.Graph v4.x**: PIM types named differently, many endpoints under `.Beta` only. Migration is non-trivial.
- **PowerShell `Microsoft.Graph.Identity.Governance`**: Works, but `New-MgRoleManagementDirectoryRoleAssignmentScheduleRequest` is cumbersome. Direct REST via `Invoke-MgGraphRequest` is often cleaner.
