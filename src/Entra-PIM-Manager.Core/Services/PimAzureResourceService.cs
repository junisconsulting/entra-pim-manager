namespace EntraPimManager.Core.Services;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using EntraPimManager.Core.Arm;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.ErrorHandling;
using EntraPimManager.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Reads and activates PIM for Azure Resources through the Azure Resource Manager
/// <c>Microsoft.Authorization/*Schedule*</c> API (api-version 2020-10-01). Eligible
/// and active rows are listed once at tenant root with <c>$filter=asTarget()</c>,
/// which covers every management group, subscription, resource group and resource.
/// </summary>
/// <remarks>
/// Differences from the Graph surfaces that matter here: request bodies are
/// PascalCase (<c>SelfActivate</c>, <c>AfterDuration</c>), a schedule request is
/// a <c>PUT</c> under a client-generated GUID, there is no validation-only mode,
/// and an unsatisfied authentication context comes back as HTTP 400 — never as a
/// 401 claims challenge.
/// </remarks>
public sealed class PimAzureResourceService : IPimAzureResourceService
{
    private const string ApiVersion = "2020-10-01";
    private const string Provider = "providers/Microsoft.Authorization";
    private const string UnknownRoleName = "(unknown role)";

    // Azure allows six levels of management groups below the root. The bound exists
    // so a cycle ARM would never return still cannot loop forever.
    private const int MaxHierarchyDepth = 8;
    private const int ChildReadConcurrency = 6;

    // Same rule ids as the Graph policy surface.
    private const string ExpirationRuleId = "Expiration_EndUser_Assignment";
    private const string EnablementRuleId = "Enablement_EndUser_Assignment";
    private const string ApprovalRuleId = "Approval_EndUser_Assignment";
    private const string AuthContextRuleId = "AuthenticationContext_EndUser_Assignment";

    private static readonly JsonSerializerOptions BodyOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _arm;
    private readonly string _principalObjectId;
    private readonly ILogger<PimAzureResourceService> _logger;

    // Bounds the fan-out of the hierarchy walk: a level with dozens of management
    // groups must not open dozens of connections and invite a 429 at once.
    private readonly SemaphoreSlim _childReads = new(ChildReadConcurrency, ChildReadConcurrency);

    /// <param name="arm">ARM client from <see cref="IArmClientFactory"/>, base address set to the cloud's host.</param>
    /// <param name="principalObjectId">
    /// The signed-in user's object id. Activation is always requested for this
    /// principal — <c>asTarget()</c> also returns rows inherited through a group,
    /// whose <c>principalId</c> is the group's, not the user's.
    /// </param>
    /// <param name="logger">Logger.</param>
    public PimAzureResourceService(HttpClient arm, string principalObjectId, ILogger<PimAzureResourceService> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalObjectId);
        _arm = arm;
        _principalObjectId = principalObjectId;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PimEligibility>> GetEligibleAzureRolesAsync(CancellationToken ct = default)
    {
        var items = await GetPagedAsync(
                $"{Provider}/roleEligibilityScheduleInstances?api-version={ApiVersion}&$filter=asTarget()", ct)
            .ConfigureAwait(false);
        return items.Select(ToEligibility).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActiveAssignment>> GetActiveAzureRolesAsync(CancellationToken ct = default)
    {
        var items = await GetPagedAsync(
                $"{Provider}/roleAssignmentScheduleInstances?api-version={ApiVersion}&$filter=asTarget()", ct)
            .ConfigureAwait(false);

        // Permanent ("Assigned") rows come back too; only activations can be deactivated.
        return items
            .Where(item => string.Equals(Text(Child(item, "properties"), "assignmentType"), "Activated", StringComparison.OrdinalIgnoreCase))
            .Select(ToActiveAssignment)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EligibleChildScope>> GetEligibleChildScopesAsync(string scopeId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);

        // eligibleChildResources answers with direct children only — observed
        // 2026-09-08: at a tenant root group the management groups came back, none of
        // the subscriptions beneath them. So the hierarchy is walked level by level,
        // the management groups of a level read in parallel under a small cap. No type
        // filter: at a management group the direct children are management groups and
        // subscriptions, and the type check in the read keeps anything else out.
        //
        // The read at the eligibility's own scope decides whether there is a picker at
        // all, so its failure is the caller's. One management group further down that
        // refuses is logged and skipped — losing a subtree beats losing the estate.
        var found = await ReadChildScopesAsync(scopeId, parentName: null, ct).ConfigureAwait(false);
        var level = found.Where(scope => scope.IsManagementGroup).ToList();
        for (var depth = 1; level.Count > 0 && depth < MaxHierarchyDepth; depth++)
        {
            var reads = level.Select(parent => ReadChildScopesSafeAsync(parent.Id, parent.Name, ct));
            var children = (await Task.WhenAll(reads).ConfigureAwait(false)).SelectMany(page => page).ToList();
            found.AddRange(children);
            level = children.Where(scope => scope.IsManagementGroup).ToList();
        }

        // Smallest reach first: subscriptions, grouped under their management group —
        // the flat picker's stand-in for a tree — and only then the management groups,
        // each of which grants everything beneath it. The picker offers them in this
        // order so the cheap answer is the one in reach.
        return found
            .Where(scope => scope.IsSubscription)
            .OrderBy(scope => scope.ParentName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(scope => scope.Name, StringComparer.OrdinalIgnoreCase)
            .Concat(found
                .Where(scope => scope.IsManagementGroup)
                .OrderBy(scope => scope.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ActivationResult> ActivateAsync(ActivationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Eligibility.Kind != PimResourceKind.AzureResourceRole)
        {
            throw new ArgumentException("Expected an Azure-resource-role eligibility.", nameof(request));
        }

        if (request.IsValidationOnly)
        {
            // ARM has no dry-run; the UI hides the pre-check for this kind.
            throw new NotSupportedException("Azure Resource Manager has no validation-only mode.");
        }

        // A narrowed activation runs at the chosen management group or subscription
        // with the very same body: ARM resolves the eligibility above it by itself —
        // linkedRoleEligibilityScheduleId is documented optional and deliberately not
        // sent. For an eligibility inherited through a group the schedule's principal
        // is the group while principalId is the user, and whether ARM accepts that
        // pairing is unobserved.
        var scope = request.TargetScope?.Id ?? request.Eligibility.ScopeId;
        var body = new
        {
            properties = new
            {
                principalId = request.Eligibility.PrincipalId,
                roleDefinitionId = request.Eligibility.ResourceId,
                requestType = "SelfActivate",
                justification = request.Justification,
                ticketInfo = request.Ticket is null
                    ? null
                    : new { ticketNumber = request.Ticket.TicketNumber, ticketSystem = request.Ticket.TicketSystem },

                // No startDateTime: ARM defaults it to the request time, which
                // sidesteps the clock-skew retry the Graph path needs.
                scheduleInfo = new
                {
                    expiration = new { type = "AfterDuration", duration = XmlConvert.ToString(request.Duration) },
                },
            },
        };

        string? claims = null;
        if (request.AuthContextClaim is { } acrs)
        {
            // The policy demands a Conditional Access authentication context;
            // the token for this PUT must carry the acrs claim or ARM rejects
            // it with HTTP 400. Never log the acrs value itself.
            _logger.LogInformation(
                "Activation requires authentication-context step-up; acquiring token with claims");
            claims = ClaimsChallengeParser.BuildAuthContextClaims(acrs);
        }

        try
        {
            var (requestName, properties) = await PutScheduleRequestAsync(scope, body, claims, ct)
                .ConfigureAwait(false);
            var schedule = Child(properties, "scheduleInfo");
            var start = Time(schedule, "startDateTime") ?? DateTimeOffset.UtcNow;
            return new ActivationResult(
                RequestId: requestName,
                Status: ActivationStatusParser.Parse(Text(properties, "status")),
                StartDateTime: start,
                EndDateTime: Time(Child(schedule, "expiration"), "endDateTime") ?? start + request.Duration,
                Error: null);
        }
        catch (ArmRequestException error)
        {
            return Failure(error);
        }
    }

    /// <inheritdoc />
    public async Task<ActivationResult> DeactivateAsync(ActiveAssignment assignment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        if (assignment.Kind != PimResourceKind.AzureResourceRole)
        {
            throw new ArgumentException("Expected an Azure-resource-role assignment.", nameof(assignment));
        }

        var body = new
        {
            properties = new
            {
                principalId = assignment.PrincipalId,
                roleDefinitionId = assignment.ResourceId,
                requestType = "SelfDeactivate",
            },
        };

        try
        {
            var (requestName, properties) = await PutScheduleRequestAsync(assignment.ScopeId, body, claimsJson: null, ct)
                .ConfigureAwait(false);
            return new ActivationResult(
                RequestId: requestName,
                Status: ActivationStatusParser.Parse(Text(properties, "status")),
                StartDateTime: null,
                EndDateTime: null,
                Error: null);
        }
        catch (ArmRequestException error)
        {
            return Failure(error);
        }
    }

    /// <inheritdoc />
    public async Task<ActivationPolicy> GetPolicyAsync(string scopeId, string roleDefinitionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleDefinitionId);

        var filter = Uri.EscapeDataString($"roleDefinitionId eq '{roleDefinitionId}'");
        var items = await GetPagedAsync(
                $"{scopeId}/{Provider}/roleManagementPolicyAssignments?api-version={ApiVersion}&$filter={filter}", ct)
            .ConfigureAwait(false);

        // Match client-side as well: whether ARM honours the filter or ignores
        // it, another role's settings must never win. Role definition ids carry
        // the scope they were read at as a prefix, so compare the trailing GUID.
        var wanted = LastSegment(roleDefinitionId);
        var assignment = items.FirstOrDefault(item =>
            string.Equals(LastSegment(Text(Child(item, "properties"), "roleDefinitionId")), wanted, StringComparison.OrdinalIgnoreCase));

        return ParsePolicyRules(Child(Child(assignment, "properties"), "effectiveRules"));
    }

    private static JsonElement? Child(JsonElement? parent, string name)
        => parent is { ValueKind: JsonValueKind.Object } element && element.TryGetProperty(name, out var child)
            ? child
            : null;

    private static string? Text(JsonElement? parent, string name)
        => Child(parent, name) is { ValueKind: JsonValueKind.String } element ? element.GetString() : null;

    private static bool Flag(JsonElement? parent, string name)
        => Child(parent, name) is { ValueKind: JsonValueKind.True };

    private static DateTimeOffset? Time(JsonElement? parent, string name)
        => Child(parent, name) is { ValueKind: JsonValueKind.String } element && element.TryGetDateTimeOffset(out var value)
            ? value
            : null;

    private static string? LastSegment(string? resourceId)
        => resourceId?.TrimEnd('/').Split('/').LastOrDefault();

    /// <summary>
    /// "Subscription: Pay-As-You-Go", "Resource group: rg-prod", "Management group: MG-1",
    /// "Resource: vm-01" — what a bare role name like "Contributor" is useless without.
    /// </summary>
    private static string? BuildScopeLabel(JsonElement? scope, string scopeId)
    {
        var name = Text(scope, "displayName") ?? LastSegment(scopeId);
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        return $"{EligibleChildScope.KindLabelFor(Text(scope, "type"))}: {name}";
    }

    private static ActivationPolicy ParsePolicyRules(JsonElement? rules)
    {
        // Start from defaults; only the *_EndUser_Assignment rules are relevant.
        var policy = new ActivationPolicy();
        if (rules is not { ValueKind: JsonValueKind.Array } array)
        {
            return policy;
        }

        foreach (var rule in array.EnumerateArray())
        {
            switch (Text(rule, "id"))
            {
                case ExpirationRuleId:
                {
                    var maximum = Text(rule, "maximumDuration");
                    policy = policy with
                    {
                        MaximumDuration = maximum is null ? policy.MaximumDuration : XmlConvert.ToTimeSpan(maximum),
                    };
                    break;
                }

                case EnablementRuleId:
                {
                    var enabled = Child(rule, "enabledRules") is { ValueKind: JsonValueKind.Array } list
                        ? list.EnumerateArray().Select(entry => entry.GetString()).ToList()
                        : [];
                    policy = policy with
                    {
                        RequiresJustification = enabled.Contains("Justification"),
                        RequiresTicketInfo = enabled.Contains("Ticketing"),
                        RequiresMfa = enabled.Contains("MultiFactorAuthentication"),
                    };
                    break;
                }

                case ApprovalRuleId:
                    policy = policy with { RequiresApproval = Flag(Child(rule, "setting"), "isApprovalRequired") };
                    break;

                case AuthContextRuleId:
                {
                    var claim = Text(rule, "claimValue");
                    policy = policy with
                    {
                        RequiresAuthContext = Flag(rule, "isEnabled"),
                        AuthContextClaim = string.IsNullOrEmpty(claim) ? null : claim,
                    };
                    break;
                }

                default:
                    break;
            }
        }

        return policy;
    }

    /// <summary>Throws <see cref="ArmRequestException"/> for a non-success response, parsing the CloudError body when there is one.</summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var code = string.Empty;
        var detail = string.Empty;
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(text);
            var error = Child(document.RootElement, "error");
            code = Text(error, "code") ?? string.Empty;
            detail = Text(error, "message") ?? string.Empty;
        }
        catch (JsonException)
        {
            // Not a CloudError body (a proxy page, an empty 5xx) — the status code alone must do.
        }

        throw new ArmRequestException((int)response.StatusCode, code, detail);
    }

    private PimEligibility ToEligibility(JsonElement item)
    {
        var properties = Child(item, "properties");
        var expanded = Child(properties, "expandedProperties");
        var scopeId = Text(properties, "scope") ?? string.Empty;
        return new PimEligibility(
            Kind: PimResourceKind.AzureResourceRole,
            DisplayName: Text(Child(expanded, "roleDefinition"), "displayName") ?? UnknownRoleName,
            ResourceId: Text(properties, "roleDefinitionId") ?? string.Empty,
            ScopeId: scopeId,
            PrincipalId: _principalObjectId,
            EndDateTime: Time(properties, "endDateTime"),
            IsRoleAssignableGroup: false,
            ScopeLabel: BuildScopeLabel(Child(expanded, "scope"), scopeId));
    }

    private ActiveAssignment ToActiveAssignment(JsonElement item)
    {
        var properties = Child(item, "properties");
        var expanded = Child(properties, "expandedProperties");
        var scopeId = Text(properties, "scope") ?? string.Empty;
        return new ActiveAssignment(
            Kind: PimResourceKind.AzureResourceRole,
            DisplayName: Text(Child(expanded, "roleDefinition"), "displayName") ?? UnknownRoleName,
            ResourceId: Text(properties, "roleDefinitionId") ?? string.Empty,
            ScopeId: scopeId,
            PrincipalId: _principalObjectId,
            StartDateTime: Time(properties, "startDateTime"),
            EndDateTime: Time(properties, "endDateTime"),
            AssignmentScheduleId: Text(item, "name") ?? string.Empty,
            ScopeLabel: BuildScopeLabel(Child(expanded, "scope"), scopeId));
    }

    /// <summary>
    /// The direct children of one management group, or an empty list when ARM refuses
    /// it — a scope the user may not read is not a reason to have no picker.
    /// </summary>
    private async Task<List<EligibleChildScope>> ReadChildScopesSafeAsync(string scopeId, string? parentName, CancellationToken ct)
    {
        try
        {
            return await ReadChildScopesAsync(scopeId, parentName, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Reading the children of {Scope} failed; skipping that subtree", scopeId);
            return [];
        }
    }

    /// <summary>The direct children of one scope, all pages, resource groups and resources dropped.</summary>
    private async Task<List<EligibleChildScope>> ReadChildScopesAsync(string scopeId, string? parentName, CancellationToken ct)
    {
        List<JsonElement> items;
        await _childReads.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            items = await GetPagedAsync(
                    $"{scopeId}/{Provider}/eligibleChildResources?api-version={ApiVersion}", ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _childReads.Release();
        }

        return items
            .Select(item => new EligibleChildScope(
                Text(item, "id") ?? string.Empty,
                Text(item, "name") ?? LastSegment(Text(item, "id")) ?? string.Empty,
                Text(item, "type") ?? string.Empty,
                parentName))
            .Where(scope => scope.Id.Length > 0 && (scope.IsManagementGroup || scope.IsSubscription))
            .ToList();
    }

    /// <summary>Follows <c>nextLink</c> until the collection is exhausted.</summary>
    private async Task<List<JsonElement>> GetPagedAsync(string url, CancellationToken ct)
    {
        var items = new List<JsonElement>();
        var next = url;
        while (next is not null)
        {
            using var response = await _arm.GetAsync(next, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (Child(document.RootElement, "value") is { ValueKind: JsonValueKind.Array } page)
            {
                items.AddRange(page.EnumerateArray().Select(element => element.Clone()));
            }

            next = Text(document.RootElement, "nextLink");
        }

        return items;
    }

    private async Task<(string RequestName, JsonElement? Properties)> PutScheduleRequestAsync(
        string scopeId,
        object body,
        string? claimsJson,
        CancellationToken ct)
    {
        // Schedule requests are PUT under a client-generated GUID; the scope is
        // passed through verbatim, exactly as the eligibility reported it.
        var requestName = Guid.NewGuid().ToString();
        var url = $"{scopeId}/{Provider}/roleAssignmentScheduleRequests/{requestName}?api-version={ApiVersion}";
        using var message = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, BodyOptions), Encoding.UTF8, "application/json"),
        };
        if (claimsJson is not null)
        {
            message.Options.Set(ArmBearerTokenHandler.ClaimsOption, claimsJson);
        }

        using var response = await _arm.SendAsync(message, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        return (requestName, Child(document.RootElement, "properties")?.Clone());
    }

    private ActivationResult Failure(ArmRequestException error)
    {
        _logger.LogWarning(
            "PIM Azure resource request failed: {Code} (HTTP {Status}). {ArmMessage}",
            error.Code,
            error.StatusCode,
            error.Detail);

        return new(
            RequestId: string.Empty,
            Status: ActivationStatus.Failed,
            StartDateTime: null,
            EndDateTime: null,
            Error: PimErrorMapper.Map(error));
    }
}
