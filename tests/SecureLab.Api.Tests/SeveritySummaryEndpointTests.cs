using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

// These tests read the shared local study database and expect the baseline seed:
// one Low (New), one Medium (Triaged) and one High (InProgress) incident, no Critical.
public sealed class SeveritySummaryEndpointTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private const string SummaryUrl = "/api/incidents/severity-summary";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetSummary_ReturnsExistingGroupsInCriticalityOrder()
    {
        using var response = await _client.GetAsync(SummaryUrl);
        var summary = await response.Content.ReadFromJsonAsync<List<IncidentSeveritySummaryResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(summary);
        Assert.Equal(["High", "Medium", "Low"], summary.Select(group => group.Severity));
        Assert.All(summary, group => Assert.Equal(1, group.Count));
        Assert.DoesNotContain(summary, group => group.Severity == "Critical");
    }

    [Fact]
    public async Task GetSummary_ReturnsOnlySeverityAndCountFields()
    {
        var json = await _client.GetStringAsync(SummaryUrl);
        using var document = JsonDocument.Parse(json);

        foreach (var group in document.RootElement.EnumerateArray())
        {
            Assert.Equal(
                ["count", "severity"],
                group.EnumerateObject().Select(property => property.Name).Order());
        }
    }

    [Fact]
    public async Task GetSummary_ForStatusWithoutIncidents_ReturnsEmptyArray()
    {
        using var response = await _client.GetAsync($"{SummaryUrl}?status=Resolved");
        var summary = await response.Content.ReadFromJsonAsync<List<IncidentSeveritySummaryResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(summary);
        Assert.Empty(summary);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("1")]
    [InlineData("Resolved' OR '1'='1")]
    public async Task GetSummary_ForStatusOutsideAllowlist_ReturnsValidationProblem400(string status)
    {
        using var response = await _client.GetAsync(
            $"{SummaryUrl}?status={Uri.EscapeDataString(status)}");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("status", out _));
    }

    [Theory]
    [InlineData("Triaged", "Medium")]
    [InlineData("inprogress", "High")]
    [InlineData("New", "Low")]
    public async Task GetSummary_ForValidStatus_CountsOnlyMatchingIncidents(string status, string expectedSeverity)
    {
        var summary = await _client.GetFromJsonAsync<List<IncidentSeveritySummaryResponse>>(
            $"{SummaryUrl}?status={status}");

        Assert.NotNull(summary);
        var group = Assert.Single(summary);
        Assert.Equal(expectedSeverity, group.Severity);
        Assert.Equal(1, group.Count);
    }
}
