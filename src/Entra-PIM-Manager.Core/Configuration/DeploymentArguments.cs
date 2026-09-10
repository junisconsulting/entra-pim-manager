namespace EntraPimManager.Core.Configuration;

using EntraPimManager.Core.Auth;

/// <summary>
/// Parses the command line an unattended deployment uses to pin one App Registration
/// to a tenant, so a rollout never has to walk users through Settings → TENANTS:
/// <c>Entra-PIM-Manager.exe --tenant-id &lt;guid&gt; --client-id &lt;guid&gt;</c>.
/// </summary>
/// <remarks>
/// Velopack's installer cannot carry these: <c>Setup.exe --silent</c> runs the
/// <c>--veloapp-install</c> hook and finishes without ever starting the app, so its
/// <c>EXE_ARGS</c> passthrough never applies. The deployment therefore calls the
/// installed executable itself as a second step, which writes the entry and exits —
/// Intune waits for the install command to return, and a tray app never would.
/// <para/>
/// Parsing lives here rather than next to <c>Program.Main</c> because the test project
/// references Core only; the same rule left the older <c>--restart</c> handling untested.
/// </remarks>
public static class DeploymentArguments
{
    private const string TenantIdFlag = "--tenant-id";
    private const string ClientIdFlag = "--client-id";
    private const string CloudFlag = "--cloud";
    private const string LabelFlag = "--label";
    private const string TicketSystemFlag = "--ticket-system";

    /// <summary>
    /// Reads a deployment registration out of <paramref name="args"/>. Returns
    /// <see cref="Result.NotRequested"/> when neither id flag is present, so a normal
    /// launch — including <c>--restart</c> and Avalonia's own arguments — falls through
    /// untouched.
    /// </summary>
    public static Result Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? tenantId = null;
        string? clientId = null;
        string? cloud = null;
        string? label = null;
        string? ticketSystem = null;

        for (var i = 0; i < args.Length; i++)
        {
            var flag = KnownFlag(args[i]);
            if (flag is null)
            {
                continue;
            }

            if (i + 1 >= args.Length)
            {
                return Result.Invalid($"{flag} needs a value.");
            }

            var value = args[++i];
            switch (flag)
            {
                case TenantIdFlag: tenantId = value; break;
                case ClientIdFlag: clientId = value; break;
                case CloudFlag: cloud = value; break;
                case LabelFlag: label = value; break;
                default: ticketSystem = value; break;
            }
        }

        if (tenantId is null && clientId is null)
        {
            return Result.NotRequested;
        }

        if (tenantId is null || clientId is null)
        {
            return Result.Invalid($"{TenantIdFlag} and {ClientIdFlag} must be given together.");
        }

        if (!Guid.TryParse(tenantId.Trim(), out var tenant))
        {
            return Result.Invalid($"{TenantIdFlag} is not a GUID.");
        }

        // The client id is validated HERE because LocalConfigStore does not: it would
        // persist a typo, and the next launch would then fail options validation and
        // shut down with no window and no message. That is unrecoverable for a user
        // who never typed the value in the first place.
        if (!Guid.TryParse(clientId.Trim(), out _))
        {
            return Result.Invalid($"{ClientIdFlag} is not a GUID.");
        }

        if (!TryResolveCloud(cloud, out var resolvedCloud))
        {
            return Result.Invalid($"{CloudFlag} must be one of: {string.Join(", ", Enum.GetNames<EntraCloud>())}.");
        }

        // Normalised exactly like SettingsPanelViewModel.AddTenant so both routes
        // write byte-identical entries.
        return new Result(
            new TenantAppRegistration
            {
                TenantId = tenant.ToString(),
                ClientId = clientId.Trim(),
                Cloud = resolvedCloud.ToString(),
                Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
            },
            string.IsNullOrWhiteSpace(ticketSystem) ? null : ticketSystem.Trim(),
            Error: null);
    }

    /// <summary>The recognised flag in its canonical spelling, or null for anything else.</summary>
    private static string? KnownFlag(string argument)
    {
        string[] flags = [TenantIdFlag, ClientIdFlag, CloudFlag, LabelFlag, TicketSystemFlag];
        return Array.Find(flags, f => string.Equals(f, argument, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves <paramref name="value"/> to a cloud, defaulting to
    /// <see cref="EntraCloud.Global"/> when it was not given.
    /// </summary>
    /// <remarks>
    /// Matched against <see cref="Enum.GetNames{T}"/> rather than through
    /// <see cref="Enum.TryParse{T}(string, bool, out T)"/>, which also accepts numbers —
    /// <c>--cloud 7</c> would otherwise yield an undefined enum value.
    /// </remarks>
    private static bool TryResolveCloud(string? value, out EntraCloud cloud)
    {
        cloud = EntraCloud.Global;
        if (value is null)
        {
            return true;
        }

        var name = Array.Find(
            Enum.GetNames<EntraCloud>(),
            n => string.Equals(n, value.Trim(), StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return false;
        }

        cloud = Enum.Parse<EntraCloud>(name);
        return true;
    }

    /// <summary>What the command line asked for.</summary>
    /// <param name="Registration">The entry to persist, or null when none was parsed.</param>
    /// <param name="TicketSystem">
    /// The tenant's ticketing system, or null when it was not given. Kept apart from
    /// <paramref name="Registration"/> because it is workflow rather than auth
    /// configuration and lives in <c>settings.json</c>, not in the registration list.
    /// </param>
    /// <param name="Error">Why the arguments were rejected, or null when they were not.</param>
    public sealed record Result(TenantAppRegistration? Registration, string? TicketSystem, string? Error)
    {
        /// <summary>No deployment flag was present — carry on with a normal launch.</summary>
        public static Result NotRequested { get; } = new(null, null, null);

        /// <summary>True when the caller meant to configure, whether or not it succeeded.</summary>
        public bool IsRequested => Registration is not null || Error is not null;

        // ponytail: the message is for tests and future diagnostics — the app is a
        // WinExe with no console, so deployment only ever sees the exit code.
        internal static Result Invalid(string error) => new(null, null, error);
    }
}
