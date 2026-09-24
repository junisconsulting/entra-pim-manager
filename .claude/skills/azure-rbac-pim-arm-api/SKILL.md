---
name: azure-rbac-pim-arm-api
description: Reference for PIM for Azure Resources — Azure RBAC role eligibilities activated through the Azure Resource Manager (ARM) REST API rather than Microsoft Graph. Use this skill whenever code lists, activates, deactivates or reads the settings of Azure resource roles, i.e. anything calling management.azure.com/{scope}/providers/Microsoft.Authorization/roleEligibilityScheduleInstances, roleAssignmentScheduleInstances, roleAssignmentScheduleRequests or roleManagementPolicyAssignments (China: management.chinacloudapi.cn). Also use it when handling ARM CloudError responses for PIM, the Azure Service Management user_impersonation permission, or the differences between the ARM and Graph PIM surfaces. Do not rely on memory for these endpoints — the Graph beta privilegedAccess/azureResources API is deprecated and wrong here; read this skill first.
---

# PIM for Azure Resources — ARM API

PIM has three providers. Entra roles and PIM for Groups live in Microsoft Graph (see the
`entra-pim-graph-api` skill). **Azure resource roles live in Azure Resource Manager** — same
concepts (eligibility schedule instances, assignment schedule requests, role management
policies), different host, different token audience, different casing. Verified against Microsoft
Learn (api-version `2020-10-01`) on 2026-09-04; field verification is tracked in
`docs/engineering-backlog.md`.

## When to use

- Listing a user's eligible or active Azure RBAC roles across subscriptions and management groups
- Activating or deactivating an Azure resource role (self-service)
- Reading the PIM settings ("role management policy") of an Azure role at a scope
- Narrowing an activation to management groups or subscriptions beneath the eligibility's scope
- Mapping ARM CloudError codes to user-facing messages
- Deciding which App Registration permission / MSAL scope an ARM call needs

## Host, permission, scope

| | Global | China (21Vianet) |
|---|---|---|
| Host | `https://management.azure.com` | `https://management.chinacloudapi.cn` |
| MSAL scope | `https://management.azure.com/user_impersonation` | `https://management.chinacloudapi.cn/user_impersonation` |

- App Registration permission: **Azure Service Management → Delegated → `user_impersonation`**
  (resource appId `797f4846-ba00-4fd7-ba43-dac1f8f63013`, permission id
  `41094075-9dad-400e-a0bd-54e686782033`). No Graph permission is involved; consent per tenant.
- **One audience per token.** Never mix this scope with Graph scopes in one MSAL request. In
  Entra PIM Manager the scope comes from `EntraCloudInfo.ResourceManagerScopes(cloud)`; the
  client from `IArmClientFactory`; all calls go through `PimAzureResourceService`.
- No RBAC role is needed to *list* or *activate* one's own eligibilities; reading a policy may
  require more than eligibility (unverified — see backlog).

## The six essential calls

### 1. List my eligibilities — tenant root, every scope at once

```http
GET /providers/Microsoft.Authorization/roleEligibilityScheduleInstances?api-version=2020-10-01&$filter=asTarget()
```

Tenant root (`/`) is valid and returns eligibilities at every management group, subscription,
resource group and resource in the tenant — **verified 2026-09-04** against a live tenant: one
management-group-scoped and one subscription-scoped eligibility, both in the same response, no
per-scope call needed. Per item read `properties.scope` (pass through
verbatim), `properties.roleDefinitionId`, `properties.endDateTime`, and the display data in
`properties.expandedProperties`: `scope.{displayName,type}` (`subscription`, `resourcegroup`,
`managementgroup`, or a resource type) and `roleDefinition.displayName`. Follow `nextLink`.

**`roleDefinitionId` comes back fully qualified, and the prefix is the scope it was read at** —
`/subscriptions/{sub}/providers/Microsoft.Authorization/roleDefinitions/{guid}`. The *same* built-in
role therefore reads as a different string under every subscription. Pass it through verbatim when
activating, but never use it as the role's identity: grouping or comparing by the whole string makes
"Owner on 400 subscriptions" look like 400 distinct roles. **Match on the trailing GUID** — it is
global for built-in roles and unique for custom ones. Cost a shipped-but-dead role grouping in
0.9.0, and it is the same reason the policy lookup below compares only the last segment.

**`principalId` is not always the user.** For `memberType == "Group"` it is the group's id.
Activate for the signed-in user's own object id, never for the row's principal. Verified
2026-09-04: `asTarget()` does expand group membership, and such a row comes back with
`memberType: "Group"` and `expandedProperties.principal` describing the **group**.

**Group membership is resolved from an ARM-side cache.** A membership that changed minutes ago —
notably an eligible PIM-for-Groups member who was turned into a real member — is not reflected
yet: the row is simply absent from `asTarget()` while `$filter=principalId eq '{groupId}'` at the
same scope already returns it. Observed 2026-09-04; it resolves on its own. Do not "fix" this by
enumerating the user's groups and querying per group.

### 2. List my active (activated) roles

```http
GET /providers/Microsoft.Authorization/roleAssignmentScheduleInstances?api-version=2020-10-01&$filter=asTarget()
```

Keep only `properties.assignmentType == "Activated"`; permanent (`Assigned`) rows come back too
and cannot be deactivated. `endDateTime` drives the countdown; `name` is the instance id.

### 3. Activate

```http
PUT /{scope}/providers/Microsoft.Authorization/roleAssignmentScheduleRequests/{new GUID}?api-version=2020-10-01
Content-Type: application/json

{
  "properties": {
    "principalId": "<signed-in user's object id>",
    "roleDefinitionId": "<from the eligibility>",
    "requestType": "SelfActivate",
    "justification": "<if the policy demands>",
    "ticketInfo": { "ticketNumber": "…", "ticketSystem": "…" },
    "scheduleInfo": {
      "expiration": { "type": "AfterDuration", "duration": "PT2H" }
    }
  }
}
```

- The request name is a **client-generated GUID**; the scope in the URL is the eligibility's
  scope, verbatim (leading `/`).
- `scheduleInfo.startDateTime` is optional (defaults to now) — omit it and there is no clock-skew
  retry to write. `linkedRoleEligibilityScheduleId` is optional too; ARM picks the eligibility.
- Response `201` carries `properties.status` — parse it, the HTTP status says nothing:
  `Provisioned` (active now), `PendingApproval`, or an in-flight value (`Accepted`,
  `PendingProvisioning`, `ProvisioningStarted`, `ScheduleCreated`, `PendingEvaluation`) that the
  app treats like `PendingScheduleCreation` and resolves on the next list refresh.
  `properties.scheduleInfo.expiration.endDateTime` gives the expiry when present.
- **There is no validation-only / dry-run mode.** Do not fake one.
- To activate **beneath** the eligibility's scope, see call 6.

### 4. Deactivate

Same `PUT`, at the *active assignment's* scope:

```json
{ "properties": { "principalId": "…", "roleDefinitionId": "…", "requestType": "SelfDeactivate" } }
```

Refused within five minutes of activation (`ActiveDurationTooShort`).

**ARM lets go of a deactivated role far more slowly than Graph, and its read API is not the
signal.** Measured 2026-09-22 on a subscription-scoped `Contributor`: `SelfDeactivate` answered
`Revoked` at once, the row was off `roleAssignmentScheduleInstances?$filter=asTarget()` within ~25 s
— and every `SelfActivate` for the next ~90 s still came back HTTP 400 `RoleAssignmentExists`. It
was accepted ~117 s after the deactivation. The same sequence on a Graph directory role took 12 s.
So anything that deactivates in order to activate again (there is no extend — see the
`entra-pim-graph-api` skill) needs a retry budget of minutes on ARM, and must not treat the read
API's silence as permission to proceed.

### 5. Read the policy (PIM settings) for a role at a scope

```http
GET /{scope}/providers/Microsoft.Authorization/roleManagementPolicyAssignments?api-version=2020-10-01&$filter=roleDefinitionId eq '<roleDefinitionId>'
```

Take the item whose `properties.roleDefinitionId` ends in the wanted role definition GUID (match
client-side as well — role definition ids carry the scope they were read at as a prefix, and
whether ARM honours the filter is unverified). Parse `properties.effectiveRules[]` by `id`; the
ids and fields are the same as Graph's:

| `id` | Field(s) | Meaning |
|---|---|---|
| `Expiration_EndUser_Assignment` | `maximumDuration` (ISO 8601, `XmlConvert.ToTimeSpan`) | max activation |
| `Enablement_EndUser_Assignment` | `enabledRules[]` ∈ `Justification`, `Ticketing`, `MultiFactorAuthentication` | required inputs |
| `Approval_EndUser_Assignment` | `setting.isApprovalRequired` | approval |
| `AuthenticationContext_EndUser_Assignment` | `isEnabled`, `claimValue` (e.g. `c1`) | CA authentication context |

Ignore the `_Admin_*` and `Notification_*` rules. **The same role has a different policy at every
scope** — cache per (tenant, role, scope), never per role alone.

### 6. List the scopes an eligibility can be narrowed to

```http
GET /{scope}/providers/Microsoft.Authorization/eligibleChildResources?api-version=2020-10-01
```

The portal's "Scope" tab: an eligibility held on a management group may be activated on a
management group or subscription beneath it instead, one held on a subscription on one of its
resource groups. Each item is `{ id, name, type }` with `type` in lower case as in
`expandedProperties.scope.type` (`managementgroup`, `subscription`, `resourcegroup`); follow
`nextLink`. An optional `$filter` narrows by type — the reference documents
`resourceType eq 'Subscription'` and `resourceType eq 'subscription' or resourceType eq
'resourcegroup'`, and `'managementgroup'` is accepted too (observed 2026-09-08). **The listing is
direct children only**: at a tenant root group whose estate the portal shows as dozens of
subscriptions, the management groups came back and not one subscription (observed 2026-09-08).

To offer a whole estate, walk it: read the scope, then every management group it returned,
level by level (`PimAzureResourceService.GetEligibleChildScopesAsync` does this unfiltered, in
parallel within a level, with the type check client-side). Entra PIM Manager never offers
resource groups.

To activate at the narrower scope, run the `SelfActivate` PUT of call 3 **at the child scope**
with the same body. `linkedRoleEligibilityScheduleId` (the last segment of the eligibility
instance's `properties.roleEligibilityScheduleId`) is documented optional — "the system will pick
a RoleEligibilitySchedule automatically" — and the app does not send it: for an eligibility
inherited through a group the schedule's principal is the group while `principalId` is the user,
and whether ARM accepts that pairing is unobserved. `roleDefinitionId` stays exactly as the
eligibility reported it. Several scopes are several PUTs — the URL carries one scope, there is no
multi-scope request. The resulting active row lists at the child scope; deactivation is the usual
PUT at that scope. Which role settings ARM evaluates for a narrowed request — the eligibility's
scope or the child's — is likewise unobserved; settings are per (role, scope) and not inherited.

## Errors

ARM answers with a CloudError body; `error.code` is the stable key:

```json
{ "error": { "code": "RoleAssignmentRequestPolicyValidationFailed", "message": "The following policy rules failed: [\"MfaRule\"]" } }
```

| `code` | Meaning | App behaviour |
|---|---|---|
| `RoleAssignmentRequestPolicyValidationFailed` | one code for every policy failure; the **rule name is only in the message**: `MfaRule`, `JustificationRule`, `TicketingRule`, `ExpirationRule`, `EligibilityRule` | mapped per rule name (`PimErrorMapper.MapPolicyRuleFailure`) |
| `RoleAssignmentRequestAcrsValidationFailed` | token lacks the CA authentication context — **HTTP 400**, the message carries the URL-encoded `claims=…`; never a 401 `WWW-Authenticate` challenge | acquire the ARM token *proactively* with `{"access_token":{"acrs":{"essential":true,"value":"c1"}}}` from the policy (`ArmBearerTokenHandler.ClaimsOption`) |
| `RoleAssignmentExists` | already active | Info |
| `ActiveDurationTooShort` | deactivation inside the 5-minute minimum | Validation |
| `InvalidRoleAssignmentRequestSchedule` | schedule rejected | Validation |
| `AuthorizationFailed` | no permission at that scope (also: policy read as eligible-only user) | Fatal / default policy |
| HTTP 429 | throttled, `Retry-After` honoured by Kiota's `RetryHandler` in the pipeline | Throttled |

## Differences from the Graph PIM surfaces (the traps)

1. **PascalCase everywhere**: `SelfActivate`, `SelfDeactivate`, `AfterDuration` — Graph directory
   roles use `selfActivate` + `AfterDuration`, groups `selfActivate` + `afterDuration`. Keep the
   literals per service.
2. **`PUT` with a client GUID**, not `POST`; the response echoes the request, not an instance.
3. **No `isValidationOnly`.** Hide the pre-check.
4. **`asTarget()` rows can carry a group as `principalId`.** Use the user's own oid to activate.
5. **Auth-context failure is a 400 body, not a 401 header** — the reactive claims-challenge
   handler never fires; stamp the claims before the PUT.
6. **Tenant-root listing** (`/providers/...` with `$filter=asTarget()`) replaces
   `filterByCurrentUser(on='principal')`; no `$expand` needed — `expandedProperties` is inline.
7. **Policy is per (role, scope)**, and the policy read may need more than eligibility.
8. **Deprecated look-alike:** Graph `beta/privilegedAccess/azureResources/*` returns no data after
   2026-10-28. If a snippet uses it, it is wrong.
