namespace EntraPimManager.AppAvalonia.ViewModels;

/// <summary>
/// Centralised formatter for the cross-tenant label shown in rows and headers
/// across the popup. Keeping the rule in one place avoids drift between the
/// active list, the eligibility list, and the per-tenant group headers.
/// </summary>
internal static class TenantLabelFormatter
{
    /// <summary>
    /// Composes the label from a (possibly null) tenant display name and the
    /// tenant GUID. When <paramref name="includeId"/> is true, the GUID is
    /// appended to the name for disambiguation (used in the Settings accounts
    /// list where two enrollments may share an identity but differ by tenant).
    /// </summary>
    public static string Format(string? name, string tenantId, bool includeId = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return tenantId;
        }

        return includeId ? $"{name} · {tenantId}" : name;
    }
}
