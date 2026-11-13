#requires -Version 7

<#
.SYNOPSIS
    Makes every PIM activation ask for a fresh sign-in.

.DESCRIPTION
    Automates docs/fresh-sign-in.md for Microsoft Entra roles, PIM for Groups and Azure resource
    roles. Interactive: it asks which areas to set up, signs in, lets you create or pick the
    authentication context and the Conditional Access policy, lets you pick the roles, groups and
    Azure scopes, and shows one overview of every change. Nothing is changed before you confirm it.

    -WhatIf shows the same overview and one "What if:" line per change, and changes nothing in the
    tenant.
    Run it again at any time to cover new roles, groups or subscriptions; what is already set, and
    every role that already requires a different context, stays as it is.

    Rights the signed-in admin needs: Conditional Access Administrator for the context and the
    policy; Privileged Role Administrator for Entra roles and role-assignable groups; Groups
    Administrator or the group's owner for other groups; Owner or User Access Administrator on the
    scope for Azure roles. Missing modules are installed for the current user after you agree.

    Needs PowerShell 7: Microsoft.Graph and Az.Accounts load conflicting identity libraries that
    Windows PowerShell 5.1 cannot keep apart in one session (Az issue #27378).

.PARAMETER TenantId
    Tenant to sign in to. Without it, the sign-in uses the account's home tenant.

.EXAMPLE
    ./setup-cap-and-authcontext-for-pim.ps1 -WhatIf

.EXAMPLE
    ./setup-cap-and-authcontext-for-pim.ps1 -TenantId 00000000-0000-0000-0000-000000000001
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$TenantId = ""
)

$ErrorActionPreference = "Stop"

$contextRuleId = "AuthenticationContext_EndUser_Assignment"
$enablementRuleId = "Enablement_EndUser_Assignment"
$phishingResistantMfa = "00000000-0000-0000-0000-000000000004"
$multifactorAuthentication = "00000000-0000-0000-0000-000000000002"

function Write-Step([string]$Title, [string]$Text) {
    Write-Host ""
    Write-Host $Title -ForegroundColor Cyan
    if ($Text) {
        Write-Host $Text -ForegroundColor DarkGray
    }
}

function Read-YesNo([string]$Prompt, [bool]$Default) {
    $hint = if ($Default) { "[Y/n]" } else { "[y/N]" }
    $answer = Read-Host "$Prompt $hint"
    if (-not $answer) {
        return $Default
    }

    return $answer -match "^(y|yes)$"
}

# Option 1 is the default and, wherever this is used, the recommendation.
function Read-Menu([string]$Prompt, [string[]]$Options) {
    Write-Host $Prompt
    for ($i = 0; $i -lt $Options.Count; $i++) {
        Write-Host "  [$($i + 1)] $($Options[$i])"
    }

    while ($true) {
        $answer = Read-Host "Choice [1]"
        if (-not $answer) {
            return 1
        }

        $number = 0
        if ([int]::TryParse($answer, [ref]$number) -and $number -ge 1 -and $number -le $Options.Count) {
            return $number
        }
    }
}

function Read-Text([string]$Prompt, [string]$Default) {
    $answer = "$(Read-Host "$Prompt [$Default]")".Trim()
    if ($answer) {
        return $answer
    }

    return $Default
}

function Select-Rows($Rows, [string]$Noun) {
    if (-not $Rows) {
        Write-Host "No $Noun found." -ForegroundColor DarkGray
        return
    }

    $choice = Read-Menu "$(@($Rows).Count) $Noun found." @("All $Noun (recommended)", "Select in a grid window")
    if ($choice -eq 1) {
        return $Rows
    }

    if (-not (Get-Command Out-GridView -ErrorAction SilentlyContinue)) {
        throw "Selecting needs Out-GridView, which exists only on the Windows desktop. Run the script there, or choose 'All'."
    }

    return $Rows | Out-GridView -Title "Select $Noun (Ctrl+click for several), then OK" -PassThru
}

function Install-ModuleIfMissing([string]$Name) {
    if (Get-Module -ListAvailable -Name $Name) {
        return $true
    }

    if (-not (Read-YesNo "The PowerShell module $Name is missing. Install it for the current user from the PowerShell Gallery?" $true)) {
        return $false
    }

    # -Force only skips the "untrusted repository" prompt; the admin has just agreed to the install.
    Install-Module -Name $Name -Scope CurrentUser -Repository PSGallery -Force -WhatIf:$false -Confirm:$false
    return $true
}

# Graph puts the whole HTTP response into ErrorDetails, headers first (one of them JSON too);
# Invoke-Arm throws plain text.
function Get-ErrorText($ErrorRecord) {
    $details = "$($ErrorRecord.ErrorDetails.Message)"
    $start = $details.IndexOf('{"error"')
    if ($start -ge 0) {
        try {
            $graphError = ($details.Substring($start) | ConvertFrom-Json).error
            return "$($graphError.code): $($graphError.message)"
        }
        catch {
            # Not JSON after all: fall through to the exception's own message.
        }
    }

    return $ErrorRecord.Exception.Message
}

function Invoke-Graph([string]$Method, [string]$Uri, $Body) {
    return Invoke-MgGraphRequest -Method $Method -Uri $Uri -Body $Body -OutputType PSObject
}

function Get-GraphAll([string]$Uri) {
    while ($Uri) {
        $page = Invoke-Graph GET $Uri
        foreach ($item in $page.value) {
            $item
        }

        $Uri = $page.'@odata.nextLink'
    }
}

# Search-and-pick for users (by UPN) or groups (by name); an empty search ends it.
function Find-Principals([string]$Kind) {
    $picked = [ordered]@{}
    $field = if ($Kind -eq "users") { "userPrincipalName" } else { "displayName" }
    while ($true) {
        $text = Read-Host "Search $Kind by the start of the $field (Enter when done)"
        if (-not $text) {
            return $picked
        }

        $filter = [uri]::EscapeDataString("startsWith($field,'$($text.Replace("'", "''"))')")
        $hits = @((Invoke-Graph GET "v1.0/$($Kind)?`$filter=$filter&`$select=id,$field&`$top=20").value)
        if (-not $hits) {
            Write-Host "  Nothing found." -ForegroundColor DarkGray
            continue
        }

        for ($i = 0; $i -lt $hits.Count; $i++) {
            Write-Host "  [$($i + 1)] $($hits[$i].$field)"
        }

        foreach ($entry in ((Read-Host "Numbers to add, e.g. 1,3 (Enter for none)") -split "[,\s]+" | Where-Object { $_ })) {
            $number = 0
            if ([int]::TryParse($entry, [ref]$number) -and $number -ge 1 -and $number -le $hits.Count) {
                $picked[$hits[$number - 1].id] = $hits[$number - 1].$field
            }
        }

        if ($picked.Count) {
            Write-Host "  Selected: $($picked.Values -join ', ')" -ForegroundColor Green
        }
    }
}

# Invoke-AzRestMethod reports HTTP errors in StatusCode instead of throwing. Writes are guarded by
# this script's own ShouldProcess, so the cmdlet's own -WhatIf/-Confirm handling is switched off.
function Invoke-Arm([string]$Method, [string]$Target, $Body) {
    $request = @{ Method = $Method }
    if ($Target.StartsWith("https://")) {
        $request.Uri = $Target
    }
    else {
        $request.Path = $Target
    }

    if ($Body) {
        $request.Payload = ConvertTo-Json -InputObject $Body -Depth 10 -Compress
    }

    $response = Invoke-AzRestMethod @request -WhatIf:$false -Confirm:$false
    if ($response.StatusCode -ge 400) {
        $armError = try { ($response.Content | ConvertFrom-Json).error } catch { $null }
        if ($armError) {
            throw "$($armError.code): $($armError.message)"
        }

        throw "HTTP $($response.StatusCode) from $Target"
    }

    if ($response.Content) {
        return $response.Content | ConvertFrom-Json
    }
}

function Get-ArmAll([string]$Path) {
    while ($Path) {
        $page = Invoke-Arm GET $Path
        foreach ($item in $page.value) {
            $item
        }

        $Path = $page.nextLink
    }
}

# One row per PIM policy, the same for all three areas. Flat strings, so the grid window can show
# every column and the overview can say what happens to each policy.
function New-PolicyRow([string]$Name, [string]$Scope, [int]$Eligible, [string]$PolicyId, $Rules, [string]$ContextId) {
    $contextRule = $Rules | Where-Object { $_.id -eq $contextRuleId } | Select-Object -First 1
    $enablementRule = $Rules | Where-Object { $_.id -eq $enablementRuleId } | Select-Object -First 1
    $onActivation = @($enablementRule.enabledRules | Where-Object { $_ })
    $current = if ($contextRule.isEnabled) { "$($contextRule.claimValue)" } else { "" }

    if ($current -and $current -ne $ContextId) {
        $action = "Unchanged"
    }
    elseif ($current -and $onActivation -notcontains "MultiFactorAuthentication") {
        $action = "Already set"
    }
    else {
        $action = "Set"
    }

    [pscustomobject]@{
        Name         = $Name
        Scope        = $Scope
        Eligible     = $Eligible
        Requires     = $current
        OnActivation = $onActivation -join ", "
        Action       = $action
        PolicyId     = $PolicyId
    }
}

function Test-PimMfa($Row) {
    return ($Row.OnActivation -split ", ") -contains "MultiFactorAuthentication"
}

# What PIM still asks on activation once the context replaces its own MFA check, as the portal
# does: justification and ticket stay. A string[] keeps one rule a JSON array, and plain strings:
# Microsoft.Graph.Authentication before 2.40 cannot serialize the wrapped ones a pipeline returns.
function Get-RemainingRules($Row) {
    return , [string[]](($Row.OnActivation -split ", ") -ne "MultiFactorAuthentication" -ne "")
}

function Set-GraphPolicy($Row, [string]$ContextId) {
    $rules = "v1.0/policies/roleManagementPolicies/$($Row.PolicyId)/rules"
    $target = @{ caller = "EndUser"; operations = @("All"); level = "Assignment"; inheritableSettings = @(); enforcedSettings = @() }

    # The context first: if the second call fails, the role asks for more than before, never less.
    Invoke-Graph PATCH "$rules/$contextRuleId" @{
        "@odata.type" = "#microsoft.graph.unifiedRoleManagementPolicyAuthenticationContextRule"
        id            = $contextRuleId
        isEnabled     = $true
        claimValue    = $ContextId
        target        = $target
    } | Out-Null

    if (Test-PimMfa $Row) {
        Invoke-Graph PATCH "$rules/$enablementRuleId" @{
            "@odata.type" = "#microsoft.graph.unifiedRoleManagementPolicyEnablementRule"
            id            = $enablementRuleId
            enabledRules  = Get-RemainingRules $Row
            target        = $target
        } | Out-Null
    }
}

function Set-ArmPolicy($Row, [string]$ContextId) {
    $target = @{ caller = "EndUser"; operations = @("All"); level = "Assignment"; targetObjects = $null; inheritableSettings = $null; enforcedSettings = $null }
    $rules = @(@{
            id         = $contextRuleId
            ruleType   = "RoleManagementPolicyAuthenticationContextRule"
            isEnabled  = $true
            claimValue = $ContextId
            target     = $target
        })

    if (Test-PimMfa $Row) {
        $rules += @{
            id           = $enablementRuleId
            ruleType     = "RoleManagementPolicyEnablementRule"
            enabledRules = Get-RemainingRules $Row
            target       = $target
        }
    }

    Invoke-Arm PATCH "$($Row.PolicyId)?api-version=2020-10-01" @{ properties = @{ rules = $rules } } | Out-Null
}

function Write-AreaOverview([string]$Title, $Rows) {
    $set = @($Rows | Where-Object { $_.Action -eq "Set" })
    $already = @($Rows | Where-Object { $_.Action -eq "Already set" })
    $kept = @($Rows | Where-Object { $_.Action -eq "Unchanged" })
    Write-Host ""
    Write-Host "$($Title): $($set.Count) to set, $($already.Count) already set, $($kept.Count) unchanged" -ForegroundColor White
    foreach ($row in $set) {
        $mfa = if (Test-PimMfa $row) { "; PIM's own MFA check is switched off" } else { "" }
        Write-Host "  set        $($row.Name) [$($row.Scope)], $($row.Eligible) eligible$mfa"
    }

    foreach ($row in $already) {
        Write-Host "  already    $($row.Name) [$($row.Scope)]" -ForegroundColor DarkGray
    }

    foreach ($row in $kept) {
        Write-Host "  unchanged  $($row.Name) [$($row.Scope)], requires $($row.Requires) already" -ForegroundColor Yellow
    }
}

$warnings = @()
$roleRows = @()
$groupRows = @()
$azureRows = @()

Write-Step "Fresh sign-in for every PIM activation" ("This script creates or reuses an authentication context and a Conditional " +
    "Access policy that asks for a full sign-in every time, then makes PIM require that context when a role is activated. " +
    "Nothing changes before you confirm the overview at the end.")

# 1. Areas. Asked before signing in, so the sign-in requests only the permissions they need.
Write-Step "1. Which PIM areas should ask for a fresh sign-in?"
$doRoles = Read-YesNo "Set up Microsoft Entra roles?" $true
$doGroups = Read-YesNo "Set up PIM for Groups?" $true
$doAzure = Read-YesNo "Set up Azure resource roles?" $true

# 2. Sign-in.
Write-Step "2. Sign-in" "A sign-in window opens for Microsoft Graph, and one more for Azure if you chose Azure roles."
if (-not (Install-ModuleIfMissing "Microsoft.Graph.Authentication")) {
    throw "The module Microsoft.Graph.Authentication is required."
}

if ($doAzure -and -not (Install-ModuleIfMissing "Az.Accounts")) {
    Write-Host "Skipping Azure resource roles: the module Az.Accounts is not installed." -ForegroundColor Yellow
    $doAzure = $false
}

# Least privilege per area. Policy.Read.All next to the write scope is a documented Graph quirk of
# Conditional Access writes; User.Read covers the tenant's name.
$scopes = @("User.Read", "Policy.Read.All", "Policy.ReadWrite.ConditionalAccess", "User.ReadBasic.All", "Group.ReadBasic.All")
if ($doRoles) {
    $scopes += "RoleManagement.Read.Directory", "RoleManagementPolicy.ReadWrite.Directory"
}

if ($doGroups) {
    $scopes += "PrivilegedEligibilitySchedule.Read.AzureADGroup", "RoleManagementPolicy.ReadWrite.AzureADGroup"
}

# Process scope: the privileged token is gone when this window closes.
$connect = @{ Scopes = $scopes; NoWelcome = $true; ContextScope = "Process" }
if ($TenantId) {
    $connect.TenantId = $TenantId
}

Connect-MgGraph @connect
$graphContext = Get-MgContext
$TenantId = $graphContext.TenantId
$tenantName = @((Invoke-Graph GET 'v1.0/organization?$select=displayName').value)[0].displayName

if ($doAzure) {
    # Az 12+ asks for a default subscription after sign-in; this script reads the subscriptions itself.
    try {
        Update-AzConfig -LoginExperienceV2 Off -Scope Process -WhatIf:$false -Confirm:$false | Out-Null
    }
    catch {
        # Az.Accounts before 3.0 has neither this setting nor the question it switches off.
    }

    # Process scope, like the Graph sign-in.
    Connect-AzAccount -Tenant $TenantId -Scope Process -WhatIf:$false -Confirm:$false | Out-Null
}

Write-Host "Tenant: $tenantName ($TenantId), signed in as $($graphContext.Account)" -ForegroundColor Green

# 3. Authentication context.
Write-Step "3. Authentication context" ("A label PIM attaches to an activation. On its own it asks for nothing; " +
    "the Conditional Access policy in step 4 decides what it requires.")
$contexts = @(Get-GraphAll "v1.0/identity/conditionalAccess/authenticationContextClassReferences")
$choice = 1
if ($contexts) {
    $choice = Read-Menu "Create a new authentication context, or use one that exists?" @("Create a new one", "Use an existing one")
}

if ($choice -eq 1) {
    # Graph accepts c1 to c25, although the portal mentions up to c99.
    $used = @($contexts | ForEach-Object { $_.id })
    # [string]: Microsoft.Graph.Authentication before 2.40 cannot serialize a string a pipeline wrapped.
    $contextId = [string](1..25 | ForEach-Object { "c$_" } | Where-Object { $used -notcontains $_ } | Select-Object -First 1)
    if (-not $contextId) {
        throw "All 25 authentication contexts (c1 to c25) are in use. Run again and pick an existing one."
    }

    $contextName = Read-Text "Name of the new authentication context" "PIM activation: fresh sign-in"
    $contextAction = "create"
}
else {
    $labels = @($contexts | ForEach-Object { "$($_.id)  $($_.displayName)$(if (-not $_.isAvailable) { '  (not published)' })" })
    $picked = $contexts[(Read-Menu "Which one?" $labels) - 1]
    $contextId = $picked.id
    $contextName = $picked.displayName
    # PIM only offers published contexts.
    $contextAction = if ($picked.isAvailable) { "none" } else { "publish" }
}

# 4. Conditional Access policy.
Write-Step "4. Conditional Access policy" ("Requires a full sign-in every time an app asks for context $contextId, " +
    "which is what a PIM activation does.")
$caPolicies = @(Get-GraphAll "v1.0/identity/conditionalAccess/policies" |
        Where-Object { @($_.conditions.applications.includeAuthenticationContextClassReferences) -contains $contextId })
$choice = 1
if ($caPolicies) {
    $choice = Read-Menu "Create a new policy, or use one that already requires context $($contextId)?" @("Create a new one", "Use an existing one")
}

$emergency = [ordered]@{}
$targets = [ordered]@{}
if ($choice -eq 1) {
    $policyAction = "create"
    $policyName = Read-Text "Name of the new policy" "PIM activation: fresh sign-in every time"

    if ((Read-Menu "Who does it apply to? It only takes effect when someone activates a PIM role." @("All users (recommended)", "Selected groups")) -eq 2) {
        Write-Host "Pick the groups of the users who are eligible, not the PIM groups or roles themselves:" -ForegroundColor Yellow
        Write-Host "nobody holds those yet at the moment of activation, so the policy would never apply." -ForegroundColor Yellow
        $targets = Find-Principals "groups"
        if (-not $targets.Count) {
            Write-Host "No group picked: the policy applies to all users." -ForegroundColor Yellow
        }
        else {
            $warnings += "The policy applies only to the groups you picked: eligible users outside them will activate with no check at all once PIM's own MFA check is off."
        }
    }

    Write-Host "Emergency access accounts are excluded, so a broken policy can never lock everyone out."
    while ($true) {
        $emergency = Find-Principals "users"
        if ($emergency.Count -or (Read-YesNo "No emergency access account is excluded. Continue without one?" $false)) {
            break
        }
    }

    $strengths = @("Phishing-resistant MFA: passkey, Windows Hello for Business, certificate (recommended)", "Any multifactor authentication")
    $grant = if ((Read-Menu "What does the fresh sign-in require?" $strengths) -eq 1) { $phishingResistantMfa } else { $multifactorAuthentication }
    $who = if ($targets.Count) { "the groups $($targets.Values -join ', ')" } else { "all users" }
    $how = if ($grant -eq $phishingResistantMfa) { "phishing-resistant MFA" } else { "multifactor authentication" }
    $policyDetails = "requires $how, sign-in frequency every time"
}
else {
    $labels = @($caPolicies | ForEach-Object { "$($_.displayName)  ($($_.state))" })
    $caPolicy = $caPolicies[(Read-Menu "Which one?" $labels) - 1]
    $policyName = $caPolicy.displayName
    # Off or report-only would let PIM's context pass without asking anyone.
    $policyAction = if ($caPolicy.state -eq "enabled") { "none" } else { "enable" }

    $users = $caPolicy.conditions.users
    $allUsers = @($users.includeUsers) -contains "All"
    $who = if ($allUsers) { "all users" } else { "only selected users, groups or roles" }
    $excludesMore = [bool]($users.excludeGroups -or $users.excludeRoles)
    if (-not $allUsers -or $excludesMore) {
        $warnings += "Policy '$policyName' does not cover every user: eligible users outside it, or in its excluded groups or roles, will activate with no check at all once PIM's own MFA check is off. A policy that targets roles or PIM groups never applies at activation."
    }

    # PIM's own MFA check is switched off later, so a policy that lets anyone through without MFA
    # would make activation weaker than before. "mfa OR compliantDevice" does not require MFA either.
    $strength = $caPolicy.grantControls.authenticationStrength
    $controls = @($caPolicy.grantControls.builtInControls) + @($strength | Where-Object { $_ })
    if (-not (($strength -or $controls -contains "mfa") -and ($caPolicy.grantControls.operator -eq "AND" -or $controls.Count -eq 1))) {
        throw "Policy '$policyName' does not require multifactor authentication for everyone it applies to, and PIM's own MFA check would be switched off. Require MFA or an authentication strength in it, or run the script again and create a new policy. Nothing has been changed."
    }

    $how = if ($strength) { $strength.displayName } else { @($caPolicy.grantControls.builtInControls) -join ", " }
    $frequency = $caPolicy.sessionControls.signInFrequency
    $everyTime = $frequency.isEnabled -and $frequency.frequencyInterval -eq "everyTime"
    $policyDetails = "requires $(if ($how) { $how } else { 'nothing' }), sign-in frequency $(if ($everyTime) { 'every time' } else { 'not every time' })"
    if (-not $everyTime) {
        $warnings += "Policy '$policyName' does not set sign-in frequency to 'Every time', so it does not force a fresh sign-in. This script leaves its settings as they are."
    }

    foreach ($id in @($caPolicy.conditions.users.excludeUsers | Where-Object { $_ -match "^[0-9a-f-]{36}$" })) {
        try {
            $emergency[$id] = (Invoke-Graph GET "v1.0/users/$($id)?`$select=userPrincipalName").userPrincipalName
        }
        catch {
            $emergency[$id] = $id
        }
    }
}

# 5. PIM settings, per chosen area.
if ($doRoles) {
    Write-Step "5a. Microsoft Entra roles" "Recommended: all roles, so a role that becomes eligible later is covered too."
    $roleNames = @{}
    try {
        foreach ($definition in Get-GraphAll 'v1.0/roleManagement/directory/roleDefinitions?$select=id,displayName') {
            $roleNames[$definition.id] = $definition.displayName
        }

        $roleEligibilities = @(Get-GraphAll 'v1.0/roleManagement/directory/roleEligibilitySchedules?$select=principalId,roleDefinitionId')
        # scopeType DirectoryRole, not Directory: only its policy ids are documented for updates.
        $assignments = @(Get-GraphAll "v1.0/policies/roleManagementPolicyAssignments?`$filter=scopeId eq '/' and scopeType eq 'DirectoryRole'&`$expand=policy(`$expand=rules)")
    }
    catch {
        $warnings += "Entra roles could not be read and are left out: $(Get-ErrorText $_)"
        $roleEligibilities = @()
        $assignments = @()
    }

    $eligibleCount = @{}
    foreach ($eligibility in $roleEligibilities) {
        $eligibleCount[$eligibility.roleDefinitionId] += 1
    }

    $rows = foreach ($assignment in $assignments) {
        $name = $roleNames[$assignment.roleDefinitionId]
        if (-not $name) {
            $name = $assignment.roleDefinitionId
        }

        New-PolicyRow $name "Directory" ([int]$eligibleCount[$assignment.roleDefinitionId]) $assignment.policyId $assignment.policy.rules $contextId
    }

    $roleRows = @(Select-Rows @($rows | Sort-Object -Property @{ Expression = "Eligible"; Descending = $true }, "Name") "Entra roles")
}

if ($doGroups) {
    Write-Step "5b. PIM for Groups" ("Only groups that already have eligible members or owners are offered: changing a group's " +
        "PIM settings would bring it under PIM for good. Reading every group can take a few minutes.")
    # PIM for Groups cannot govern dynamic or on-premises synced groups, so they are not read.
    $groups = @(Get-GraphAll 'v1.0/groups?$select=id,displayName,groupTypes,onPremisesSyncEnabled&$top=999' |
            Where-Object { @($_.groupTypes) -notcontains "DynamicMembership" -and -not $_.onPremisesSyncEnabled })
    $groupNames = @{}
    $groupEligibilities = @{}
    $unreadable = @()
    $queue = [System.Collections.Generic.Queue[object]]::new()
    foreach ($group in $groups) {
        $groupNames[$group.id] = $group.displayName
        $queue.Enqueue($group)
    }

    # There is no list of the groups in PIM, so every group's eligibilities are read, 20 per $batch.
    # The SDK does not retry throttled requests inside a batch; they go back into the queue.
    $read = 0
    while ($queue.Count) {
        $chunk = @()
        while ($queue.Count -and $chunk.Count -lt 20) {
            $chunk += $queue.Dequeue()
        }

        $requests = @(for ($i = 0; $i -lt $chunk.Count; $i++) {
                $filter = [uri]::EscapeDataString("groupId eq '$($chunk[$i].id)'")
                @{ id = "$i"; method = "GET"; url = "/identityGovernance/privilegedAccess/group/eligibilitySchedules?`$filter=$filter&`$select=accessId,principalId" }
            })
        $wait = 0
        foreach ($response in (Invoke-Graph POST 'v1.0/$batch' @{ requests = $requests }).responses) {
            $group = $chunk[[int]$response.id]
            if ($response.status -eq 429) {
                $queue.Enqueue($group)
                $wait = [Math]::Max($wait, [int]("0" + $response.headers.'Retry-After'))
                if (-not $wait) {
                    $wait = 10
                }

                continue
            }

            $read++
            if ($response.status -ne 200) {
                $unreadable += "$($group.displayName): $($response.body.error.message)"
            }
            elseif ($response.body.value) {
                # A batch answer holds the first page only.
                $groupEligibilities[$group.id] = @($response.body.value) + @(Get-GraphAll $response.body.'@odata.nextLink')
            }
        }

        Write-Progress -Activity "Reading PIM for Groups eligibilities" -Status "$read of $($groups.Count) groups" -PercentComplete (100 * $read / [Math]::Max(1, $groups.Count))
        if ($wait) {
            Start-Sleep -Seconds $wait
        }
    }

    Write-Progress -Activity "Reading PIM for Groups eligibilities" -Completed
    if ($unreadable) {
        $warnings += "$($unreadable.Count) groups could not be read and are left out, e.g. $($unreadable[0])"
    }

    $found = foreach ($groupId in $groupEligibilities.Keys) {
        [pscustomobject]@{
            Name    = $groupNames[$groupId]
            Members = @($groupEligibilities[$groupId] | Where-Object { $_.accessId -eq "member" }).Count
            Owners  = @($groupEligibilities[$groupId] | Where-Object { $_.accessId -eq "owner" }).Count
            Id      = $groupId
        }
    }

    # A group is picked as a whole: its member and its owner settings both get the context.
    $rows = foreach ($group in @(Select-Rows @($found | Sort-Object Name) "groups with eligibilities")) {
        try {
            foreach ($assignment in Get-GraphAll "v1.0/policies/roleManagementPolicyAssignments?`$filter=scopeId eq '$($group.Id)' and scopeType eq 'Group'&`$expand=policy(`$expand=rules)") {
                $eligible = @($groupEligibilities[$group.Id] | Where-Object { $_.accessId -eq $assignment.roleDefinitionId }).Count
                New-PolicyRow $group.Name $assignment.roleDefinitionId $eligible $assignment.policyId $assignment.policy.rules $contextId
            }
        }
        catch {
            $warnings += "PIM settings of group '$($group.Name)' could not be read and are left out: $(Get-ErrorText $_)"
        }
    }

    $groupRows = @($rows | Sort-Object Name, Scope)
}

if ($doAzure) {
    Write-Step "5c. Azure resource roles" ("Each role has its own PIM settings on every scope it is eligible at; " +
        "only the role-and-scope pairs that have eligibilities are offered.")
    $subscriptions = @(Get-AzSubscription -TenantId $TenantId -WarningAction SilentlyContinue |
            Where-Object { $_.State -eq "Enabled" } |
            ForEach-Object { [pscustomobject]@{ Name = $_.Name; Id = $_.Id } })
    if (-not $subscriptions) {
        $warnings += "No Azure subscription is visible to this account; a Global Administrator sees none until they elevate access."
    }

    # One eligibility on a management group shows up under every subscription below it.
    $pairs = @{}
    $seen = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($subscription in @(Select-Rows $subscriptions "subscriptions")) {
        try {
            $schedules = Get-ArmAll "/subscriptions/$($subscription.Id)/providers/Microsoft.Authorization/roleEligibilitySchedules?api-version=2020-10-01"
        }
        catch {
            $warnings += "Eligibilities of subscription '$($subscription.Name)' could not be read and are left out: $(Get-ErrorText $_)"
            continue
        }

        foreach ($schedule in $schedules) {
            if (-not $seen.Add($schedule.id)) {
                continue
            }

            $properties = $schedule.properties
            $roleGuid = ($properties.roleDefinitionId -split "/")[-1]
            $key = "$($properties.scope.ToLowerInvariant())|$roleGuid"
            if (-not $pairs.ContainsKey($key)) {
                $pairs[$key] = [pscustomobject]@{
                    Scope      = $properties.scope.TrimEnd("/")
                    ScopeName  = "$($properties.expandedProperties.scope.type) $($properties.expandedProperties.scope.displayName)"
                    RoleGuid   = $roleGuid
                    RoleName   = $properties.expandedProperties.roleDefinition.displayName
                    Principals = [System.Collections.Generic.List[string]]::new()
                }
            }

            $pairs[$key].Principals.Add($properties.principalId)
        }
    }

    # Settings are not inherited: a resource group has its own policy, separate from its subscription's.
    # The policy assignment links a role to its policy; whether ARM honours the filter is unverified,
    # so the role is matched by its GUID as well (as PimAzureResourceService does).
    $rows = foreach ($pair in $pairs.Values) {
        $filter = [uri]::EscapeDataString("roleDefinitionId eq '$($pair.Scope)/providers/Microsoft.Authorization/roleDefinitions/$($pair.RoleGuid)'")
        try {
            $assignment = @(Get-ArmAll "$($pair.Scope)/providers/Microsoft.Authorization/roleManagementPolicyAssignments?api-version=2020-10-01&`$filter=$filter") |
                Where-Object { ($_.properties.roleDefinitionId -split "/")[-1] -eq $pair.RoleGuid } |
                Select-Object -First 1
        }
        catch {
            $warnings += "PIM settings of '$($pair.RoleName)' on $($pair.ScopeName) could not be read and are left out: $(Get-ErrorText $_)"
            continue
        }

        if (-not $assignment) {
            $warnings += "No PIM settings found for '$($pair.RoleName)' on $($pair.ScopeName); left out."
            continue
        }

        New-PolicyRow $pair.RoleName $pair.ScopeName $pair.Principals.Count $assignment.properties.policyId $assignment.properties.effectiveRules $contextId
    }

    $azureRows = @(Select-Rows @($rows | Sort-Object Scope, Name) "Azure role-and-scope pairs")
}

# An excluded emergency account that is also eligible activates without any check at all.
# shortcut: direct eligibilities only; one held through a group is not found. Upgrade with the
# account's transitiveMemberOf (needs GroupMember.Read.All) if a tenant relies on that.
foreach ($id in $emergency.Keys) {
    $eligibleFor = @()
    if ($doRoles) {
        $eligibleFor += @($roleEligibilities | Where-Object { $_.principalId -eq $id } | ForEach-Object { "Entra role $($roleNames[$_.roleDefinitionId])" })
    }

    if ($doGroups) {
        foreach ($groupId in $groupEligibilities.Keys) {
            if (@($groupEligibilities[$groupId] | Where-Object { $_.principalId -eq $id }).Count) {
                $eligibleFor += "group $($groupNames[$groupId])"
            }
        }
    }

    if ($doAzure) {
        $eligibleFor += @($pairs.Values | Where-Object { $_.Principals -contains $id } | ForEach-Object { "$($_.RoleName) on $($_.ScopeName)" })
    }

    if ($eligibleFor) {
        $warnings += "Emergency account $($emergency[$id]) is eligible for $($eligibleFor -join ', '). It is excluded from the policy, so these activations will ask for nothing at all. Emergency accounts should hold their roles permanently instead."
    }
}

if (-not ($doRoles -or $doGroups -or $doAzure)) {
    $warnings += "No PIM area was chosen: PIM does not require context $contextId anywhere yet."
}

# 6. Overview.
Write-Step "6. Overview" "Nothing has been changed yet."
Write-Host "Tenant:   $tenantName ($TenantId)"
switch ($contextAction) {
    "create" { Write-Host "Context:  $contextId '$contextName' - create and publish" }
    "publish" { Write-Host "Context:  $contextId '$contextName' - exists, publish it" }
    default { Write-Host "Context:  $contextId '$contextName' - exists, unchanged" }
}

switch ($policyAction) {
    "create" { Write-Host "Policy:   '$policyName' - create, switched on" }
    "enable" { Write-Host "Policy:   '$policyName' - exists, switch it on (it is '$($caPolicy.state)')" }
    default { Write-Host "Policy:   '$policyName' - exists and on, unchanged" }
}

$excluded = if ($emergency.Count) { $emergency.Values -join ", " } else { "nobody" }
if ($excludesMore) {
    $excluded += ", plus the groups or roles the policy excludes"
}

Write-Host "          applies to $who, excluding $excluded"
Write-Host "          $policyDetails"

if ($doRoles) {
    Write-AreaOverview "Entra roles" $roleRows
}

if ($doGroups) {
    Write-AreaOverview "PIM for Groups" $groupRows
}

if ($doAzure) {
    Write-AreaOverview "Azure resource roles" $azureRows
}

Write-Host ""
foreach ($warning in $warnings) {
    Write-Host "! $warning" -ForegroundColor Yellow
}

if (-not $WhatIfPreference -and -not (Read-YesNo "Apply these changes?" $false)) {
    Write-Host "Nothing was changed."
    return
}

# 7. Apply. Context and policy come first: PIM must not require a context that no policy enforces.
$changes = @()
try {
    $contextUri = "v1.0/identity/conditionalAccess/authenticationContextClassReferences/$contextId"
    if ($contextAction -eq "create" -and $PSCmdlet.ShouldProcess("authentication context $contextId '$contextName'", "Create and publish")) {
        Invoke-Graph PATCH $contextUri @{
            displayName = $contextName
            description = "Required by PIM on role activation. A Conditional Access policy asks for a full sign-in every time."
            isAvailable = $true
        } | Out-Null
        $changes += "Authentication context $contextId '$contextName' created and published"
    }

    if ($contextAction -eq "publish" -and $PSCmdlet.ShouldProcess("authentication context $contextId '$contextName'", "Publish")) {
        Invoke-Graph PATCH $contextUri @{ isAvailable = $true } | Out-Null
        $changes += "Authentication context $contextId '$contextName' published"
    }

    if ($policyAction -eq "create" -and $PSCmdlet.ShouldProcess("Conditional Access policy '$policyName'", "Create, switched on")) {
        $users = @{ excludeUsers = [string[]]@($emergency.Keys) }
        if ($targets.Count) {
            $users.includeGroups = [string[]]@($targets.Keys)
        }
        else {
            $users.includeUsers = @("All")
        }

        $created = Invoke-Graph POST "v1.0/identity/conditionalAccess/policies" @{
            displayName     = $policyName
            state           = "enabled"
            conditions      = @{
                clientAppTypes = @("all")
                applications   = @{ includeAuthenticationContextClassReferences = @($contextId) }
                users          = $users
            }
            grantControls   = @{ operator = "OR"; authenticationStrength = @{ id = $grant } }
            sessionControls = @{ signInFrequency = @{ isEnabled = $true; frequencyInterval = "everyTime"; authenticationType = "primaryAndSecondaryAuthentication" } }
        }
        $changes += "Conditional Access policy '$policyName' created and switched on (id $($created.id))"
    }

    if ($policyAction -eq "enable" -and $PSCmdlet.ShouldProcess("Conditional Access policy '$policyName'", "Switch on")) {
        Invoke-Graph PATCH "v1.0/identity/conditionalAccess/policies/$($caPolicy.id)" @{ state = "enabled" } | Out-Null
        $changes += "Conditional Access policy '$policyName' switched on (id $($caPolicy.id), was '$($caPolicy.state)')"
    }
}
catch {
    Write-Host "Stopped before any PIM setting was changed: $(Get-ErrorText $_)" -ForegroundColor Red
    Write-Host "The context and the policy need Conditional Access Administrator or Security Administrator." -ForegroundColor Red
    foreach ($change in $changes) {
        Write-Host "  done: $change"
    }

    exit 1
}

# A context or policy step declined under -Confirm: the context would not be enforced.
if (-not $WhatIfPreference -and $changes.Count -lt @(@($contextAction, $policyAction) -ne "none").Count) {
    Write-Host "The context or the policy was not changed as planned, so no PIM setting was changed." -ForegroundColor Yellow
    return
}

$work = @(
    $roleRows | Where-Object { $_.Action -eq "Set" } | ForEach-Object { @{ Row = $_; Area = "Entra role"; Arm = $false; Needs = "Privileged Role Administrator" } }
    $groupRows | Where-Object { $_.Action -eq "Set" } | ForEach-Object { @{ Row = $_; Area = "Group"; Arm = $false; Needs = "Privileged Role Administrator, Groups Administrator or the group's owner" } }
    $azureRows | Where-Object { $_.Action -eq "Set" } | ForEach-Object { @{ Row = $_; Area = "Azure role"; Arm = $true; Needs = "Owner or User Access Administrator on the scope" } }
)

$failures = @()
for ($i = 0; $i -lt $work.Count; $i++) {
    $item = $work[$i]
    $label = "$($item.Area) '$($item.Row.Name)' [$($item.Row.Scope)]"
    Write-Progress -Activity "Changing PIM settings" -Status $label -PercentComplete (100 * $i / $work.Count)
    $mfa = if (Test-PimMfa $item.Row) { ", switch PIM's own MFA check off" } else { "" }
    if (-not $PSCmdlet.ShouldProcess($label, "Require authentication context $contextId on activation$mfa")) {
        continue
    }

    try {
        if ($item.Arm) {
            Set-ArmPolicy $item.Row $contextId
        }
        else {
            Set-GraphPolicy $item.Row $contextId
        }

        $changes += "$label requires $contextId (policy $($item.Row.PolicyId))"
    }
    catch {
        $failures += "$label - $(Get-ErrorText $_) - needs $($item.Needs)"
    }
}

Write-Progress -Activity "Changing PIM settings" -Completed

if ($WhatIfPreference) {
    return
}

Write-Step "Done" "Each change can be undone in the Entra admin center or the Azure portal."
foreach ($change in $changes) {
    Write-Host "  $change"
}

if ($failures) {
    Write-Host ""
    Write-Host "Failed, $($failures.Count). Run the script again to finish them; a role may already require $contextId with PIM's own MFA check still on:" -ForegroundColor Yellow
    foreach ($failure in $failures) {
        Write-Host "  $failure" -ForegroundColor Yellow
    }
}
