using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elaris.Domain.Common;
using Elaris.Domain.EarlyAccess.Requests;
using Elaris.Domain.EarlyAccess.Responses;
using Elaris.Domain.Feedback.Requests;
using Elaris.Domain.Feedback.Responses;
using Elaris.Domain.Suggestions.Requests;
using Elaris.Domain.Suggestions.Responses;

namespace Elaris.Api.IntegrationTests;

[Collection("Integration")]
public class PublicEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AuthApiFactory _factory;

    public PublicEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task EarlyAccess_ListCitiesAndRegister_Succeeds()
    {
        var client = _factory.CreateClient();

        var citiesResponse = await client.GetAsync("/early-access/cities");
        Assert.Equal(HttpStatusCode.OK, citiesResponse.StatusCode);
        var cities = await citiesResponse.Content.ReadFromJsonAsync<ApiResponse<List<EarlyAccessCityDto>>>(JsonOptions);
        Assert.True(cities!.Success);
        Assert.Contains(cities.Data!, c => c.Name == "Mumbai");

        var email = $"early-{Guid.NewGuid():N}@example.com";
        var create = await client.PostAsJsonAsync(
            "/early-access/register",
            new JoinEarlyAccessRequest(
                "Ada Lovelace",
                email,
                "Mumbai",
                "Elaris",
                true,
                true,
                "+919876543210"));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<ApiResponse<EarlyAccessSignupDto>>(JsonOptions);
        Assert.True(created!.Success);
        Assert.True(created.Data!.Created);

        var update = await client.PostAsJsonAsync(
            "/early-access/register",
            new JoinEarlyAccessRequest(
                "Ada L",
                email,
                "Bangalore",
                "Elaris Professionals",
                true,
                true,
                "+919876543211"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<ApiResponse<EarlyAccessSignupDto>>(JsonOptions);
        Assert.False(updated!.Data!.Created);
        Assert.Equal("Bangalore", updated.Data.City);
    }

    [Fact]
    public async Task Suggestion_And_Feedback_Submit_Succeed()
    {
        var client = _factory.CreateClient();

        var suggestion = await client.PostAsJsonAsync(
            "/public/suggestions",
            new SubmitSuggestionRequest(
                "Ada",
                "ada-suggestion@example.com",
                "More partner cafés in Mumbai would help.",
                "+919876543210"));
        Assert.Equal(HttpStatusCode.OK, suggestion.StatusCode);
        var suggestionBody = await suggestion.Content.ReadFromJsonAsync<ApiResponse<SuggestionDto>>(JsonOptions);
        Assert.True(suggestionBody!.Success);

        var feedback = await client.PostAsJsonAsync(
            "/public/feedback",
            new SubmitFeedbackRequest(
                "Ada",
                "ada-feedback@example.com",
                "I need help with a pilot support request.",
                "9876543210"));
        Assert.Equal(HttpStatusCode.OK, feedback.StatusCode);
        var feedbackBody = await feedback.Content.ReadFromJsonAsync<ApiResponse<FeedbackSubmissionDto>>(JsonOptions);
        Assert.True(feedbackBody!.Success);
    }
}
