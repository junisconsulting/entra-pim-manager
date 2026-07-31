# PIM Graph API — Policy Rules Schema

When you query `/policies/roleManagementPolicies/{id}?$expand=rules` or use the `$expand=policy($expand=rules)` pattern on a policy assignment, you get back a `rules[]` collection. This file documents how to parse the rules relevant for self-service activation.

## Rule object shape

Each rule has:

```json
{
  "@odata.type": "#microsoft.graph.unifiedRoleManagementPolicy<XxxRule>",
  "id": "Enablement_EndUser_Assignment",
  "target": {
    "caller": "EndUser",
    "operations": ["all"],
    "level": "Assignment",
    "inheritableSettings": [],
    "enforcedSettings": []
  },
  // rule-type-specific fields
}
```

Rules are identified by their `id` (a stable string like `"Enablement_EndUser_Assignment"`) and by their `@odata.type` (which determines which other fields are present).

## Rules relevant for self-service activation

### `Expiration_EndUser_Assignment`

**OData type**: `unifiedRoleManagementPolicyExpirationRule`

Determines the maximum duration a user can request when self-activating.

```json
{
  "@odata.type": "#microsoft.graph.unifiedRoleManagementPolicyExpirationRule",
  "id": "Expiration_EndUser_Assignment",
  "isExpirationRequired": true,
  "maximumDuration": "PT8H"
}
```

**Parse**: `maximumDuration` is an ISO 8601 duration. `"PT8H"` = 8 hours, `"PT30M"` = 30 minutes, `"P1D"` = 1 day, `"PT24H"` = 24 hours.

**Use**: Set the upper bound of the duration slider in the activation dialog.

In C#:
```csharp
var maxDuration = System.Xml.XmlConvert.ToTimeSpan(rule["maximumDuration"].ToString());
```

### `Enablement_EndUser_Assignment`

**OData type**: `unifiedRoleManagementPolicyEnablementRule`

Determines what the user must provide when activating.

```json
{
  "@odata.type": "#microsoft.graph.unifiedRoleManagementPolicyEnablementRule",
  "id": "Enablement_EndUser_Assignment",
  "enabledRules": [
    "Justification",
    "MultiFactorAuthentication",
    "Ticketing"
  ]
}
```

**Parse**: `enabledRules` is an array. Check for these string values:

| Value | What it requires |
|---|---|
| `"Justification"` | User must provide non-empty justification text |
| `"Ticketing"` | User must provide `ticketInfo.ticketNumber` and `ticketInfo.ticketSystem` |
| `"MultiFactorAuthentication"` | Token must have MFA claim; if not, expect MFA challenge on POST |

**Use**: Show/hide UI fields, set validation rules.

### `Approval_EndUser_Assignment`

**OData type**: `unifiedRoleManagementPolicyApprovalRule`

Determines whether activation requires approver action.

```json
{
  "@odata.type": "#microsoft.graph.unifiedRoleManagementPolicyApprovalRule",
  "id": "Approval_EndUser_Assignment",
  "setting": {
    "isApprovalRequired": true,
    "isApprovalRequiredForExtension": false,
    "isRequestorJustificationRequired": true,
    "approvalMode": "SingleStage",
    "approvalStages": [
      {
        "approvalStageTimeOutInDays": 1,
        "isApproverJustificationRequired": true,
        "escalationTimeInMinutes": 0,
        "primaryApprovers": [
          {
            "@odata.type": "#microsoft.graph.groupMembers",
            "groupId": "<approver-group-id>"
          }
        ]
      }
    ]
  }
}
```

**Parse**: 
- `setting.isApprovalRequired` — boolean, the main field to check
- `setting.approvalStages[].primaryApprovers` — list of who can approve (groups or users)

**Use**: If `isApprovalRequired = true`, show warning in dialog: "This activation will be sent for approval." After submission, response `status` will be `PendingApproval` instead of `Provisioned`.

### `AuthenticationContext_EndUser_Assignment`

**OData type**: `unifiedRoleManagementPolicyAuthenticationContextRule`

Determines whether a Conditional Access authentication context is required.

```json
{
  "@odata.type": "#microsoft.graph.unifiedRoleManagementPolicyAuthenticationContextRule",
  "id": "AuthenticationContext_EndUser_Assignment",
  "isEnabled": true,
  "claimValue": "c1"
}
```

**Parse**: If `isEnabled = true`, the token must have the auth context claim `c1` (or whatever `claimValue` is). If not, the activation will trigger a claims challenge.

**Use**: Inform the user "This activation may prompt for additional verification." The actual claim challenge is handled by the auth provider (see `msal-dotnet-desktop-wam/references/claims-challenge.md`).

---

## Rules less relevant for v1 (admin/info)

These exist but aren't typically needed for end-user activation UI:

| Rule ID | Purpose |
|---|---|
| `Expiration_Admin_Eligibility` | Max duration for admin-assigned eligibility (for admin UI) |
| `Expiration_Admin_Assignment` | Max duration for admin-assigned active assignment |
| `Enablement_Admin_Assignment` | Requirements for admin assignment (different from self-activation) |
| `Notification_Admin_Admin_Eligibility` | Who gets notified of admin actions |
| `Notification_Requestor_*` | Who gets notified on request events |
| `Notification_Approver_*` | Who gets notified on approval events |

Notification rules can be safely ignored for v1 — they affect mail/Teams notifications but not the activation flow itself.

---

## Practical parsing pattern (C#)

```csharp
public ActivationPolicy ParsePolicy(IEnumerable<UnifiedRoleManagementPolicyRule> rules)
{
    var policy = new ActivationPolicy();

    foreach (var rule in rules)
    {
        switch (rule.Id)
        {
            case "Expiration_EndUser_Assignment":
                if (rule is UnifiedRoleManagementPolicyExpirationRule exp)
                {
                    policy.MaximumDuration = exp.MaximumDuration ?? TimeSpan.FromHours(8);
                }
                break;

            case "Enablement_EndUser_Assignment":
                if (rule is UnifiedRoleManagementPolicyEnablementRule en)
                {
                    policy.RequiresJustification = en.EnabledRules?.Contains("Justification") ?? false;
                    policy.RequiresTicketInfo = en.EnabledRules?.Contains("Ticketing") ?? false;
                    policy.RequiresMfa = en.EnabledRules?.Contains("MultiFactorAuthentication") ?? false;
                }
                break;

            case "Approval_EndUser_Assignment":
                if (rule is UnifiedRoleManagementPolicyApprovalRule ap)
                {
                    policy.RequiresApproval = ap.Setting?.IsApprovalRequired ?? false;
                }
                break;

            case "AuthenticationContext_EndUser_Assignment":
                if (rule is UnifiedRoleManagementPolicyAuthenticationContextRule ac)
                {
                    policy.RequiresAuthContext = ac.IsEnabled ?? false;
                    policy.AuthContextClaim = ac.ClaimValue;
                }
                break;
        }
    }

    return policy;
}
```

---

## Sensible defaults if a rule is missing

| Field | Default |
|---|---|
| `MaximumDuration` | 8 hours (Microsoft's default) |
| `RequiresJustification` | `true` (safer to ask than not) |
| `RequiresTicketInfo` | `false` |
| `RequiresMfa` | `false` (token will fail if needed; let API tell us) |
| `RequiresApproval` | `false` |
| `RequiresAuthContext` | `false` |

If your code defaults to "requires nothing", users will get confusing 400 errors instead of clear UI. Default to "asks for justification" — worst case, user types something unnecessary.

---

## Caching

Policies change infrequently (manual admin action). Cache for 5–15 minutes in memory keyed by `(resourceType, resourceId)`. Invalidate on:
- User-initiated refresh
- Policy-related error (e.g., `MaximumDurationExceeded`) — indicates stale cache

Don't persist policy cache across app restarts — it's cheap to refetch and avoids stale-bug class issues.
