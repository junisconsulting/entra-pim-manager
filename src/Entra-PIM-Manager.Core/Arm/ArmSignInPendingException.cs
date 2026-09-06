namespace EntraPimManager.Core.Arm;

/// <summary>
/// The Azure Resource Manager call was abandoned while it was still acquiring a
/// token — not while it was waiting for Azure.
/// </summary>
/// <remarks>
/// Raised when the surface timeout cancels <see cref="ArmBearerTokenHandler"/>
/// inside the token acquisition. The realistic cause is a broker sign-in or
/// consent prompt that opened in the background and nobody answered in time,
/// which happens in every tenant that has not consented to the Azure permission
/// yet. Without this distinction the user is told "the request timed out" and
/// goes looking for a network problem that does not exist.
/// </remarks>
public sealed class ArmSignInPendingException : Exception
{
    public ArmSignInPendingException(Exception innerException)
        : base("Acquiring an Azure Resource Manager token was cancelled before it completed.", innerException)
    {
    }

    public ArmSignInPendingException()
        : this(new OperationCanceledException())
    {
    }

    public ArmSignInPendingException(string message)
        : base(message)
    {
    }

    public ArmSignInPendingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
