namespace EntraPimManager.Core.Auth;

using System.Net;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Parses Conditional Access claims challenges out of a Graph <c>401</c> response.
/// When a downstream API rejects a token, it returns a <c>WWW-Authenticate</c>
/// header carrying a base64url-encoded <c>claims</c> value; MSAL needs the decoded
/// JSON to re-acquire a sufficient token.
/// </summary>
public static class ClaimsChallengeParser
{
    private static readonly Regex ClaimsRegex = new(
        "claims=\"([^\"]+)\"",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Returns the decoded claims-challenge JSON from an HTTP <c>401</c> response,
    /// or <c>null</c> when the response is not a claims challenge.
    /// </summary>
    public static string? ExtractClaimsChallenge(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return null;
        }

        foreach (var header in response.Headers.WwwAuthenticate)
        {
            if (!string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parameter = header.Parameter;
            if (string.IsNullOrEmpty(parameter))
            {
                continue;
            }

            var match = ClaimsRegex.Match(parameter);
            if (match.Success)
            {
                return DecodeBase64Url(match.Groups[1].Value);
            }
        }

        return null;
    }

    /// <summary>
    /// Decodes a base64url string (URL-safe alphabet, no padding) to UTF-8 text.
    /// </summary>
    internal static string DecodeBase64Url(string base64Url)
    {
        ArgumentException.ThrowIfNullOrEmpty(base64Url);

        var base64 = base64Url.Replace('-', '+').Replace('_', '/');

        var remainder = base64.Length % 4;
        if (remainder > 0)
        {
            base64 += new string('=', 4 - remainder);
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }
}
