using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

// Тести читають спільну локальну навчальну БД. Після встановлення scaffold ЛР 02 штатний seed
// містить Low=3 (усі зі статусом New), Medium=1 (Triaged) і High=1 (InProgress), без Critical.
// Перед кожним тестом БД повертається до цього стану, тому інциденти, створені тестами контракту,
// не впливають на підрахунок. Оновлено лише очікувані числа після звірки seed; перевірки не послаблено.
public sealed class SeveritySummaryEndpointTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>, IAsyncLifetime
{
    private const string SummaryUrl = "/api/incidents/severity-summary";

    // ЛР 04: група /api/incidents вимагає входу. Підсумок читає Morgan (Analyst): за матрицею ЛР 04 він бачить усі інциденти, тому очікувані числа ЛР 1–2 не змінюються.
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _client = await factory.LoginAsSeedUserAsync("morgan");
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetSummary_ReturnsExistingGroupsInCriticalityOrder()
    {
        using var response = await _client.GetAsync(SummaryUrl);
        var summary = await response.Content.ReadFromJsonAsync<List<IncidentSeveritySummaryResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(summary);
        Assert.Equal(["High", "Medium", "Low"], summary.Select(group => group.Severity));
        Assert.Equal(1, summary.Single(group => group.Severity == "High").Count);
        Assert.Equal(1, summary.Single(group => group.Severity == "Medium").Count);
        Assert.Equal(3, summary.Single(group => group.Severity == "Low").Count);
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
    [InlineData("Triaged", "Medium", 1)]
    [InlineData("inprogress", "High", 1)]
    [InlineData("New", "Low", 3)]
    public async Task GetSummary_ForValidStatus_CountsOnlyMatchingIncidents(string status, string expectedSeverity, int expectedCount)
    {
        var summary = await _client.GetFromJsonAsync<List<IncidentSeveritySummaryResponse>>(
            $"{SummaryUrl}?status={status}");

        Assert.NotNull(summary);
        var group = Assert.Single(summary);
        Assert.Equal(expectedSeverity, group.Severity);
        Assert.Equal(expectedCount, group.Count);
    }
}
