namespace EntraPimManager.Core.Arm;

/// <summary>
/// A non-success response from Azure Resource Manager. ARM answers with a
/// CloudError body (<c>{ "error": { "code", "message" } }</c>) whose
/// <see cref="Code"/> is the stable, programmatic identifier the error mapper
/// keys on; both are empty when the body was not JSON.
/// </summary>
public sealed class ArmRequestException : Exception
{
    public ArmRequestException(int statusCode, string code, string detail)
        : base($"ARM {statusCode} {code}: {detail}")
    {
        StatusCode = statusCode;
        Code = code;
        Detail = detail;
    }

    /// <summary>HTTP status of the response.</summary>
    public int StatusCode { get; }

    /// <summary>ARM <c>error.code</c>, e.g. <c>RoleAssignmentRequestPolicyValidationFailed</c>.</summary>
    public string Code { get; }

    /// <summary>ARM <c>error.message</c> — names the failed policy rule, among other things.</summary>
    public string Detail { get; }
}
