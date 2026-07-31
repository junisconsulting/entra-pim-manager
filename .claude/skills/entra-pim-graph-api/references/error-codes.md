# PIM Graph API — Error Codes & User-Friendly Mappings

Activation requests can fail in many specific ways. The HTTP status alone is insufficient — always parse `response.error.code` and `response.error.message`.

This file catalogues the known codes with recommended user-facing messages (German + English).

## Response shape

```json
{
  "error": {
    "code": "RoleAssignmentExists",
    "message": "The role assignment already exists.",
    "innerError": {
      "code": "PimRoleAssignmentInstanceExists",
      ...
    }
  }
}
```

Use both `error.code` AND `error.innerError.code` — the outer is generic, the inner is specific.

---

## HTTP 400 — Validation errors

### `RoleAssignmentExists` / `RoleAssignmentInstanceAlreadyExists`
**Cause**: User tried to activate a role that's already active for them.
**DE**: "Diese Rolle ist bereits aktiv."
**EN**: "This role is already active."
**UI behaviour**: Show as info, not error. Refresh the active-assignments list.

### `JustificationRuleViolated` / `JustificationRequired`
**Cause**: Policy requires justification; field was empty or too short.
**DE**: "Eine Begründung ist erforderlich. Bitte beschreibe den Grund der Aktivierung."
**EN**: "A justification is required. Please describe why you're activating this role."
**UI behaviour**: Inline validation on the justification field, dialog stays open.

### `TicketingRuleViolated` / `TicketInfoRequired`
**Cause**: Policy requires ticket info; ticket number or system missing.
**DE**: "Eine Ticket-Referenz ist erforderlich. Bitte gib die Ticket-Nummer und das System an."
**EN**: "A ticket reference is required. Please provide the ticket number and system."
**UI behaviour**: Inline validation on ticket fields.

### `MfaRuleViolated` / `MfaRuleNotSatisfied` / `MfaRequired`
**Cause**: Policy requires MFA challenge before activation; current token doesn't have an MFA claim.
**DE**: "Diese Aktivierung erfordert eine MFA-Bestätigung. Bitte erneut anmelden."
**EN**: "This activation requires MFA verification. Please re-authenticate."
**UI behaviour**: Trigger MSAL `AcquireTokenInteractive` with `claims` parameter to step up. Retry activation after.

### `MaximumDurationExceeded` / `ScheduleExpirationRuleViolated`
**Cause**: Requested duration > policy-allowed maximum.
**DE**: "Die gewünschte Dauer überschreitet das erlaubte Maximum von <X>."
**EN**: "Requested duration exceeds the allowed maximum of <X>."
**UI behaviour**: Should ideally never reach the user — UI should cap the slider at the policy max. If it does happen, show error and reset slider.

### `InvalidScope` / `ScopeNotAllowed`
**Cause**: `directoryScopeId` doesn't match the eligibility's scope.
**DE**: "Ungültiger Berechtigungsbereich."
**EN**: "Invalid scope for this activation."
**UI behaviour**: Internal bug — log full request body to diagnose. Don't show raw error.

### `StartTimeInPast` / `InvalidStartDateTime`
**Cause**: `scheduleInfo.startDateTime` is in the past (often clock skew between client and Microsoft).
**DE**: "Startzeitpunkt liegt in der Vergangenheit. System-Uhr prüfen."
**EN**: "Start time is in the past. Check system clock."
**UI behaviour**: Auto-retry with `startDateTime = now + 30 seconds`.

### `EligibilityNotFound` / `RoleAssignmentDoesNotExist`
**Cause**: User tried to activate something they're not eligible for. Often happens when:
- Eligibility just expired
- Eligibility was revoked between UI load and activation click
- Wrong `principalId` (e.g., admin acting on behalf of another user)
**DE**: "Die Berechtigung ist nicht mehr verfügbar. Bitte die Liste aktualisieren."
**EN**: "Eligibility no longer available. Please refresh the list."
**UI behaviour**: Auto-refresh eligibility list.

---

## HTTP 401 — Authentication / Claims challenge

### `Unauthenticated` / `InvalidAuthenticationToken`
**Cause**: Token expired or invalid.
**Action**: MSAL silent token acquisition; if that fails, interactive.

### Claims challenge (with `WWW-Authenticate` header)
**Cause**: Conditional Access requires additional claims (MFA, compliant device, auth strength).
**Action**: Parse `claims` from `WWW-Authenticate: Bearer ... claims="<base64url>"`, base64url-decode, pass to MSAL via `.WithClaims()`, retry the request. See `msal-dotnet-desktop-wam/references/claims-challenge.md`.
**UI behaviour**: This should be invisible to the user — auto-handled in the auth provider.

---

## HTTP 403 — Authorization

### `InsufficientPermissions` / `Authorization_RequestDenied`
**Cause**: Token doesn't have the required scope. E.g., trying activation with only `*.Read` scopes.
**DE**: "Berechtigung fehlt. Bitte Administrator kontaktieren."
**EN**: "Missing permission. Please contact your administrator."
**UI behaviour**: This is a deployment bug, not a user issue. Log the requested scopes vs. token scopes.

### `RoleAssignmentApprovalRequired` (sometimes returned as 403, sometimes 201+status)
**Cause**: Policy requires approval; either treat as success-pending or refuse depending on tenant config.
**DE**: "Diese Aktivierung erfordert eine Genehmigung. Anfrage wurde gesendet."
**EN**: "This activation requires approval. Request submitted."
**UI behaviour**: Treat as `PendingApproval`, not as a hard error.

---

## HTTP 404

### `ResourceNotFound`
**Cause**: Wrong `roleDefinitionId` or `groupId`.
**DE**: "Ressource nicht gefunden."
**EN**: "Resource not found."
**UI behaviour**: Refresh eligibility list — the role/group may have been deleted.

---

## HTTP 409 — Conflict

### `RoleAssignmentAlreadyExists` (sometimes 409, sometimes 400)
Same as `RoleAssignmentExists` above.

### `ConcurrentActivationInProgress`
**Cause**: Another activation request for the same role is already pending.
**DE**: "Eine andere Aktivierungsanfrage läuft bereits."
**EN**: "Another activation request is in progress."
**UI behaviour**: Wait 5s, refresh requests list, show pending request status.

---

## HTTP 429 — Throttling

### `TooManyRequests`
Respect the `Retry-After` header. Polly handles this if configured (see `endpoints.md` throttling section).

---

## HTTP 500/503 — Service issues

### `InternalServerError` / `ServiceUnavailable`
Transient. Retry with exponential backoff (3 attempts). If persistent, surface to user:
**DE**: "Microsoft-Dienst aktuell nicht erreichbar. Bitte später erneut versuchen."
**EN**: "Microsoft service currently unavailable. Please try again later."

---

## Mapping pattern (C#)

```csharp
public static class PimErrorMapper
{
    public static UserFacingError Map(GraphServiceException ex, Language lang = Language.De)
    {
        var code = ex.Error?.Code ?? "";
        var innerCode = ex.Error?.InnerError?.AdditionalData?
            .GetValueOrDefault("code")?.ToString() ?? "";

        return (code, innerCode) switch
        {
            (_, "PimRoleAssignmentInstanceExists") or
            ("RoleAssignmentExists", _) =>
                Info(lang, "Diese Rolle ist bereits aktiv.",
                          "This role is already active."),

            ("JustificationRuleViolated", _) =>
                ValidationError("justification", lang,
                    "Eine Begründung ist erforderlich.",
                    "A justification is required."),

            ("TicketingRuleViolated", _) =>
                ValidationError("ticket", lang,
                    "Eine Ticket-Referenz ist erforderlich.",
                    "A ticket reference is required."),

            ("MfaRuleViolated", _) or ("MfaRequired", _) =>
                StepUpRequired(lang,
                    "MFA-Bestätigung erforderlich.",
                    "MFA verification required."),

            ("MaximumDurationExceeded", _) =>
                ValidationError("duration", lang,
                    "Dauer überschreitet das erlaubte Maximum.",
                    "Duration exceeds maximum allowed."),

            ("EligibilityNotFound", _) =>
                RefreshList(lang,
                    "Berechtigung nicht mehr verfügbar.",
                    "Eligibility no longer available."),

            _ when ex.ResponseStatusCode == 429 =>
                Throttled(lang,
                    "Zu viele Anfragen. Bitte kurz warten.",
                    "Too many requests. Please wait."),

            _ => GenericError(lang, ex.Message)
        };
    }
}
```

---

## What NOT to show users

Never show:
- Raw stack traces
- Internal correlation IDs (log them, don't display)
- Microsoft's raw English error messages (often confusing, e.g. "The principal does not have the required permissions" when the actual issue is something else)
- The HTTP status code as a number (unless explicitly debug mode)

DO log everything to Serilog at DEBUG/INFO level for diagnostics.
