using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

// Пошук після виправлення (етап 4). Перед кожним тестом БД повертається до штатного seed
// (5 інцидентів), тому очікувані множини результатів детерміновані.
public sealed class SearchSecurityTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>, IAsyncLifetime
{
    private const string Url = "/api/incidents/search";
    // ЛР 04: група /api/incidents вимагає входу. Пошук виконує Morgan (Analyst): за матрицею ЛР 04 він бачить усі інциденти, тому множини ЛР 2 не змінюються.
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

    private async Task<List<SearchIncidentResponse>> SearchAsync(string query)
    {
        var result = await _client.GetFromJsonAsync<List<SearchIncidentResponse>>($"{Url}?{query}");
        Assert.NotNull(result);
        return result!;
    }

    // S-02: контрольний ввід більше не змінює логіку. Множина результатів порожня (літеральний пошук),
    // а не просто "не 500". Перевіряємо і порожнечу, і відсутність чужих записів.
    [Fact]
    public async Task Search_WithControlInjectionInput_ReturnsEmptySet()
    {
        var control = Uri.EscapeDataString("zz-no-match' OR TRUE -- ");
        using var response = await _client.GetAsync($"{Url}?q={control}");
        var rows = await response.Content.ReadFromJsonAsync<List<SearchIncidentResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(rows);
        Assert.Empty(rows!);
    }

    // Буквальний метасимвол: q=% екранується і шукається як символ, а не "усе". Жоден seed не містить '%'.
    [Fact]
    public async Task Search_WithPercentSign_IsTreatedLiterally()
    {
        var rows = await SearchAsync("q=%25");
        Assert.Empty(rows);
    }

    // T-04: нормальний пошук USB повертає рівно один очікуваний запис.
    [Fact]
    public async Task Search_NormalWord_ReturnsMatchingIncident()
    {
        var rows = await SearchAsync("q=USB");
        var row = Assert.Single(rows);
        Assert.Contains("USB", row.Title, StringComparison.OrdinalIgnoreCase);
    }

    // T-04: легітимний апостроф працює (200, не 500) і знаходить очікуваний запис.
    [Theory]
    [InlineData("O%27Brien", "O'Brien")]
    [InlineData("%D0%BA%D0%BE%D0%BC%D0%BF%27%D1%8E%D1%82%D0%B5%D1%80%D0%BD%D0%BE%D0%B3%D0%BE", "комп'ютерного")]
    public async Task Search_LegitimateApostrophe_ReturnsMatch(string encoded, string expectedSubstring)
    {
        using var response = await _client.GetAsync($"{Url}?q={encoded}");
        var rows = await response.Content.ReadFromJsonAsync<List<SearchIncidentResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(rows);
        Assert.Contains(rows!, r =>
            r.Title.Contains(expectedSubstring, StringComparison.OrdinalIgnoreCase)
            || r.Description.Contains(expectedSubstring, StringComparison.OrdinalIgnoreCase));
    }

    // T-05: невідоме сортування -> 400 з ключем sortBy; значення не доходить до SQL.
    [Fact]
    public async Task Search_WithUnknownSortBy_Returns400WithSortByKey()
    {
        using var response = await _client.GetAsync($"{Url}?q=USB&sortBy=title");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("sortBy", out _));
    }

    // Порожнє sortBy означає createdAtUtc за замовчуванням: запит проходить (200).
    [Fact]
    public async Task Search_WithoutSortBy_UsesDefaultOrderAndSucceeds()
    {
        using var response = await _client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static int SeverityRank(string s) => s switch
    {
        "Critical" => 0, "High" => 1, "Medium" => 2, _ => 3
    };

    private static int StatusRank(string s) => s switch
    {
        "New" => 0, "Triaged" => 1, "InProgress" => 2, "Resolved" => 3, _ => 4
    };

    // sortBy=severity ранжує за критичністю (Critical..Low), а не за абеткою.
    [Fact]
    public async Task Search_SortBySeverity_OrdersByCriticality()
    {
        var ranks = (await SearchAsync("sortBy=severity")).Select(r => SeverityRank(r.Severity)).ToList();
        Assert.Equal(ranks.OrderBy(x => x), ranks);
        Assert.Equal("High", (await SearchAsync("sortBy=severity"))[0].Severity);
    }

    // sortBy=status ранжує за життєвим циклом (New..Closed), а не за абеткою.
    [Fact]
    public async Task Search_SortByStatus_OrdersByLifecycle()
    {
        var ranks = (await SearchAsync("sortBy=status")).Select(r => StatusRank(r.Status)).ToList();
        Assert.Equal(ranks.OrderBy(x => x), ranks);
        Assert.Equal("New", (await SearchAsync("sortBy=status"))[0].Status);
    }
}
