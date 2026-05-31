namespace EntraPimManager.Tests.Auth;

using System.Net;
using System.Net.Http.Headers;
using EntraPimManager.Core.Auth;

public sealed class ClaimsChallengeParserTests
{
    // Sample from the msal-dotnet-desktop-wam skill: an Authentication Context (c1) challenge.
    private const string SampleBase64Url =
        "eyJhY2Nlc3NfdG9rZW4iOnsiYWNycyI6eyJlc3NlbnRpYWwiOnRydWUsInZhbHVlIjoiYzEifX19";

    private const string SampleDecoded =
        "{\"access_token\":{\"acrs\":{\"essential\":true,\"value\":\"c1\"}}}";

    [Fact]
    public void DecodeBase64Url_DecodesUrlSafeUnpaddedInput()
    {
        var decoded = ClaimsChallengeParser.DecodeBase64Url(SampleBase64Url);

        Assert.Equal(SampleDecoded, decoded);
    }

    [Fact]
    public void ExtractClaimsChallenge_FromBearerHeader_ReturnsDecodedClaims()
    {
        var headerParameter =
            "realm=\"\", authorization_uri=\"https://login.microsoftonline.com/x/authorize\", "
            + "error=\"insufficient_claims\", claims=\"" + SampleBase64Url + "\"";

        using var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer", headerParameter));

        var claims = ClaimsChallengeParser.ExtractClaimsChallenge(response);

        Assert.Equal(SampleDecoded, claims);
    }

    [Fact]
    public void ExtractClaimsChallenge_WhenStatusIsNotUnauthorized_ReturnsNull()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        Assert.Null(ClaimsChallengeParser.ExtractClaimsChallenge(response));
    }

    [Fact]
    public void ExtractClaimsChallenge_WhenHeaderHasNoClaimsParameter_ReturnsNull()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue(
            "Bearer", "realm=\"\", error=\"invalid_token\""));

        Assert.Null(ClaimsChallengeParser.ExtractClaimsChallenge(response));
    }
}
