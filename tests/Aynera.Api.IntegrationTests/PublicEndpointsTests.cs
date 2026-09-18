using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aynera.Domain.Common;
using Aynera.Domain.EarlyAccess.Requests;
using Aynera.Domain.EarlyAccess.Responses;
using Aynera.Domain.Feedback.Requests;
using Aynera.Domain.Feedback.Responses;
using Aynera.Domain.Suggestions.Requests;
using Aynera.Domain.Suggestions.Responses;

namespace Aynera.Api.IntegrationTests;

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

        var citiesResponse = await client.GetAsync("/early-access/cities/GetAll");
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
                "Aynera",
                true,
                true,
                "+919876543210",
                "Fluid",
                "Duos"));
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
                "Aynera Professionals",
                true,
                true,
                "+919876543211",
                "Intent",
                "Both"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<ApiResponse<EarlyAccessSignupDto>>(JsonOptions);
        Assert.False(updated!.Data!.Created);
        Assert.Equal("Bangalore", updated.Data.City);
    }

    [Fact]
    public async Task Suggestion_And_Feedback_Submit_Succeed()
    {
        const string suggestionPath = "/suggestions/Create";
        const string feedbackPath = "/feedback/Create";
        var client = _factory.CreateClient();

        var suggestion = await client.PostAsJsonAsync(
            suggestionPath,
            new SubmitSuggestionRequest(
                "Ada",
                "ada-suggestion@example.com",
                "More partner cafés in Mumbai would help.",
                "+919876543210"));
        Assert.Equal(HttpStatusCode.OK, suggestion.StatusCode);
        var suggestionBody = await suggestion.Content.ReadFromJsonAsync<ApiResponse<SuggestionDto>>(JsonOptions);
        Assert.True(suggestionBody!.Success);

        var feedback = await client.PostAsJsonAsync(
            feedbackPath,
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
