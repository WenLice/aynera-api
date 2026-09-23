using System.Net;
using System.Text.Json;

namespace Aynera.Api.IntegrationTests;

/// <summary>
/// Passes the face check with the test provider. Photos and the intro video are matched against the
/// face it verifies, so a member has to do this before uploading either.
/// </summary>
internal static class FaceCheck
{
    public static async Task PassAsync(HttpClient client)
    {
        using var start = await client.PostAsync("/liveness/Start", null);
        var body = await start.Content.ReadAsStringAsync();
        Assert.True(start.StatusCode == HttpStatusCode.OK, $"Start failed: {start.StatusCode} {body}");
        var sessionId = JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("sessionId").GetString();

        using var complete = await client.PostAsync($"/liveness/{sessionId}/Complete", null);
        var verdict = await complete.Content.ReadAsStringAsync();
        Assert.True(complete.StatusCode == HttpStatusCode.OK, $"Complete failed: {complete.StatusCode} {verdict}");
        Assert.Contains("\"outcome\":\"Passed\"", verdict);
    }
}
