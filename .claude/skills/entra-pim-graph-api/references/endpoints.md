# PIM Graph API — Endpoint Catalog

Full catalog of endpoints. The SKILL.md covers the essentials; this file goes deeper.

## Conventions

- Base URL: `https://graph.microsoft.com/v1.0` unless marked `(beta)`.
- All endpoints require `Authorization: Bearer <token>` with delegated permissions.
- All POST/PATCH bodies are `Content-Type: application/json`.

---

## Directory Roles PIM

Base path: `/roleManagement/directory/`

### Eligibility (who CAN activate)

#### List all eligibility schedules for current user
```
GET /roleManagement/directory/roleEligibilityScheduleInstances/filterByCurrentUser(on='principal')
    ?$expand=roleDefinition
```

`filterByCurrentUser(on='principal')` returns eligibilities **where the current user is the principal**. Other valid `on` values: `'subject'` (alias for principal in this context).

#### Get a specific eligibility schedule
```
GET /roleManagement/directory/roleEligibilitySchedules/{id}
```

Schedules represent the long-lived policy ("Alice is eligible for User Admin"). Instances represent specific time slices. For UI listing, use **instances**, not schedules.

#### List all eligibility requests (audit trail)
```
GET /roleManagement/directory/roleEligibilityScheduleRequests/filterByCurrentUser(on='principal')
```

Returns the request history — when eligibility was granted, by whom, with what justification.

### Active assignments

#### List active assignments for current user
```
GET /roleManagement/directory/roleAssignmentScheduleInstances/filterByCurrentUser(on='principal')
    ?$expand=roleDefinition
    &$filter=assignmentType eq 'Activated'
```

`assignmentType` values:
- `Activated` — PIM-just-activated, time-limited
- `Assigned` — permanent active assignment (not via PIM activation)

For a self-service UI, filter to `Activated` to show only what the user can deactivate.

#### List my activation request history
```
GET /roleManagement/directory/roleAssignmentScheduleRequests/filterByCurrentUser(on='principal')
```

Returns all activation/deactivation requests the user has made — useful for audit/history view.

### Activation

#### Activate a role
```
POST /roleManagement/directory/roleAssignmentScheduleRequests
```

Body (selfActivate):
```json
{
  "action": "selfActivate",
  "principalId": "<my user oid>",
  "roleDefinitionId": "<role def id>",
  "directoryScopeId": "<scope, e.g. '/' or '/administrativeUnits/{auId}'>",
  "justification": "<text>",
  "scheduleInfo": {
    "startDateTime": "2026-05-22T10:00:00Z",
    "expiration": {
      "type": "AfterDuration",
      "duration": "PT4H"
    }
  },
  "ticketInfo": {
    "ticketNumber": "INC0012345",
    "ticketSystem": "ServiceNow"
  }
}
```

Response (HTTP 201):
```json
{
  "id": "<requestId>",
  "status": "Provisioned | Granted | PendingApproval | ...",
  "createdDateTime": "...",
  "completedDateTime": "...",
  ...
}
```

#### Deactivate a role early
Same endpoint, `action: "selfDeactivate"`. `justification` not required.

#### Other action values (for reference, not for self-service UIs)

- `adminAssign` — admin grants active assignment to someone
- `adminUpdate` — modify an existing assignment
- `adminRemove` — admin revokes
- `adminExtend` — extend expiring assignment
- `adminRenew` — renew expired assignment
- `selfExtend` — user requests extension of their own active assignment
- `selfRenew` — user renews their own expired assignment

`selfExtend` / `selfRenew` are useful for "near-expiry" UX in v1.1+.

### Policies

#### Find policy assignments for a role
```
GET /policies/roleManagementPolicyAssignments
    ?$filter=scopeId eq '/' and scopeType eq 'Directory' and roleDefinitionId eq '<id>'
    &$expand=policy($expand=rules)
```

Returns the policy assignment + the policy itself with all its rules expanded. Parse `policy.rules` for activation constraints. See `policy-rules.md`.

#### Get a specific policy directly
```
GET /policies/roleManagementPolicies/{id}?$expand=rules
```

Useful if you already have the policy ID from a previous lookup.

---

## PIM for Groups

Base path: `/identityGovernance/privilegedAccess/group/`

### Eligibility

#### List my eligible group memberships/ownerships
```
GET /identityGovernance/privilegedAccess/group/eligibilityScheduleInstances/filterByCurrentUser(on='principal')
```

Returns instances with `groupId`, `accessId` (`member`/`owner`), `endDateTime`. **Do NOT use `$expand=group`** — unreliable. Batch-resolve group display names separately:

```
GET /groups?$filter=id in ('id1','id2','id3')&$select=id,displayName,isAssignableToRole
```

Or for larger sets, use `/$batch` with multiple `/groups/{id}` GETs.

#### My eligibility request history
```
GET /identityGovernance/privilegedAccess/group/eligibilityScheduleRequests/filterByCurrentUser(on='principal')
```

### Active assignments

#### List active group memberships/ownerships
```
GET /identityGovernance/privilegedAccess/group/assignmentScheduleInstances/filterByCurrentUser(on='principal')
    ?$filter=assignmentType eq 'Activated'
```

### Activation

#### Activate group membership/ownership
```
POST /identityGovernance/privilegedAccess/group/assignmentScheduleRequests
```

Body:
```json
{
  "accessId": "member",
  "principalId": "<my user oid>",
  "groupId": "<group id>",
  "action": "selfActivate",
  "scheduleInfo": {
    "startDateTime": "2026-05-22T10:00:00Z",
    "expiration": {
      "type": "afterDuration",
      "duration": "PT4H"
    }
  },
  "justification": "<text>"
}
```

> Note `"afterDuration"` (camelCase) — DIFFERENT from directory roles. There's no `ticketInfo` field at v1.0; if the policy demands ticketing for groups, MS surfaces this through a different mechanism (currently inconsistent across the API).

#### Deactivate
Same endpoint, `action: "selfDeactivate"`.

### Policies for groups

Same `/policies/roleManagementPolicyAssignments` endpoint, but filter by group scope:

```
GET /policies/roleManagementPolicyAssignments
    ?$filter=scopeId eq '<groupId>' and scopeType eq 'Group'
    &$expand=policy($expand=rules)
```

**Important**: A group's policy doesn't exist until the group is onboarded to PIM (happens implicitly on first eligibility/assignment API call). If you get an empty result, check whether the group is actually PIM-managed.

---

## Approval (beta-only as of writing)

If policies require approval, requests with `status: PendingApproval` show up in approver queues:

```
GET /identityGovernance/privilegedAccess/group/assignmentScheduleRequests?$filter=status eq 'PendingApproval'
GET /roleManagement/directory/roleAssignmentScheduleRequests?$filter=status eq 'PendingApproval'
```

Approving/denying uses the approvals API:
```
POST /identityGovernance/appConsent/appConsentRequests/{id}/userConsentRequests/{id}/approval/stages/{id}
```

(Out of scope for v1 — approver UX is v1.1+.)

---

## Batch operations

Microsoft Graph supports `$batch` for combining up to 20 requests. Useful for:
- Resolving multiple `groupId` → display name in one round trip
- Fetching policies for multiple resources

```http
POST /v1.0/$batch
Content-Type: application/json

{
  "requests": [
    { "id": "1", "method": "GET", "url": "/groups/<id1>?$select=id,displayName,isAssignableToRole" },
    { "id": "2", "method": "GET", "url": "/groups/<id2>?$select=id,displayName,isAssignableToRole" }
  ]
}
```

---

## Pagination

List endpoints return up to 100 items by default. Use `@odata.nextLink` for more. In practice, individual users rarely have >100 eligibilities, but reporting scripts must handle pagination.
