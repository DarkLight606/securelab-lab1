using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SecureLab.Api.Tests;

// Контракт створення інциденту 2-A (етап 2). Кожен тест використовує унікальний title,
// тому не залежить від інших записів у спільній локальній навчальній БД.
// ЛР 03: POST /api/incidents захищено, тому перевірки контракту (201, 400, 409) виконує
// cookie-клієнт Bob. Окремий анонімний сценарій A-01 лежить в AuthenticationSecurityTests.
public sealed class IncidentCreateTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>, IAsyncLifetime
{
    private const string Url = "/api/incidents";
    private HttpClient _client = null!;

    private static string StudyPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    public async Task InitializeAsync() => _client = await factory.LoginAsync("bob", StudyPassword);

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    private static object ValidBody(string title, string? severity = "Medium", string? description = null)
        => new
        {
            title,
            description = description ?? "Достатній опис навчального інциденту для перевірки контракту.",
            severity,
            occurredAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
        };

    private static string Unique() => $"Regression-{Guid.NewGuid():N}";

    // T-01: коректне створення повертає 201 і response DTO без server-managed полів.
    [Fact]
    public async Task Create_WithValidRequest_Returns201WithoutServerManagedFields()
    {
        using var response = await _client.PostAsJsonAsync(Url, ValidBody(Unique()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("id", out _));
        Assert.True(root.TryGetProperty("title", out _));
        Assert.True(root.TryGetProperty("severity", out _));
        Assert.Equal("New", root.GetProperty("status").GetString());
        Assert.True(root.TryGetProperty("occurredAtUtc", out _));
        Assert.True(root.TryGetProperty("createdAtUtc", out _));
        Assert.False(root.TryGetProperty("ownerUserId", out _));
        Assert.False(root.TryGetProperty("updatedAtUtc", out _));
    }

    // T-02: порожній title повертає 400 application/problem+json без внутрішніх деталей.
    [Fact]
    public async Task Create_WithEmptyTitle_Returns400ProblemJson()
    {
        var body = new { title = "", description = "опис достатньої довжини для перевірки", severity = "Medium", occurredAtUtc = DateTimeOffset.UtcNow.AddHours(-1) };

        using var response = await _client.PostAsJsonAsync(Url, body);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(payload);
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("title", out _));
        // Безпечна помилка: без stack trace, SQL чи внутрішніх назв.
        Assert.DoesNotContain("FromSqlRaw", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", payload, StringComparison.OrdinalIgnoreCase);
    }

    // T-02: невідома текстова назва severity теж дає 400 з ключем severity.
    [Fact]
    public async Task Create_WithUnknownSeverity_Returns400WithSeverityKey()
    {
        using var response = await _client.PostAsJsonAsync(Url, ValidBody(Unique(), severity: "Purple"));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("severity", out _));
    }

    // T-02 (severity "7"): TryParse прийме, IsDefined відкине -> 400 з ключем severity, запис не створюється.
    [Fact]
    public async Task Create_WithNumericSeverity_Returns400WithSeverityKey()
    {
        using var response = await _client.PostAsJsonAsync(Url, ValidBody(Unique(), severity: "7"));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("severity", out _));
    }

    // T-02 (occurredAtUtc): відсутній і надто далеко в майбутньому -> 400 з ключем occurredAtUtc.
    [Fact]
    public async Task Create_WithMissingOccurredAt_Returns400()
    {
        var body = new { title = Unique(), description = "опис достатньої довжини для перевірки", severity = "Low" };
        using var response = await _client.PostAsJsonAsync(Url, body);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("occurredAtUtc", out _));
    }

    [Fact]
    public async Task Create_WithFarFutureOccurredAt_Returns400()
    {
        var body = new { title = Unique(), description = "опис достатньої довжини для перевірки", severity = "Low", occurredAtUtc = DateTimeOffset.UtcNow.AddHours(1) };
        using var response = await _client.PostAsJsonAsync(Url, body);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("occurredAtUtc", out _));
    }

    // T-03: другий активний інцидент із тим самим title -> 409 у Problem Details.
    [Fact]
    public async Task Create_DuplicateActiveTitle_Returns409()
    {
        var title = Unique();
        using var first = await _client.PostAsJsonAsync(Url, ValidBody(title));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await _client.PostAsJsonAsync(Url, ValidBody(title));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
    }

    // T-06: синтаксично коректний, але відсутній id -> 404 у Problem Details.
    [Fact]
    public async Task GetDetails_ForMissingId_Returns404ProblemJson()
    {
        using var response = await _client.GetAsync("/api/incidents/99999999-9999-9999-9999-999999999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // A-02 (ЛР 02) / CP-01: зайві поля ownerUserId і status у тілі ігноруються (захист від overposting).
    // Після ЛР 03 власник береться з principal (тут увійшов Bob), а status лишається New.
    [Fact]
    public async Task Create_IgnoresClientSuppliedOwnerAndStatus()
    {
        var body = new
        {
            title = Unique(),
            description = "Опис достатньої довжини для перевірки ігнорування зайвих полів.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
            ownerUserId = "10000000-0000-0000-0000-000000000001", // Alice: чуже значення
            status = "Closed"
        };

        using var createResponse = await _client.PostAsJsonAsync(Url, body);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        Assert.Equal("New", created.RootElement.GetProperty("status").GetString());

        var location = createResponse.Headers.Location!.ToString();
        using var details = JsonDocument.Parse(await _client.GetStringAsync(location));
        Assert.Equal("Боб Мельник", details.RootElement.GetProperty("ownerDisplayName").GetString());
        Assert.Equal("New", details.RootElement.GetProperty("status").GetString());
    }

    // T-09: cross-field 2-A. High + опис 39 символів -> 400 з ключем description; 40 символів -> 201.
    [Fact]
    public async Task Create_HighSeverityWithShortDescription_Returns400WithDescriptionKey()
    {
        var body = new { title = Unique(), description = new string('ж', 39), severity = "High", occurredAtUtc = DateTimeOffset.UtcNow.AddHours(-1) };
        using var response = await _client.PostAsJsonAsync(Url, body);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("description", out _));
    }

    [Fact]
    public async Task Create_HighSeverityWithExactlyFortyCharDescription_Returns201()
    {
        var body = new { title = Unique(), description = new string('ж', 40), severity = "High", occurredAtUtc = DateTimeOffset.UtcNow.AddHours(-1) };
        using var response = await _client.PostAsJsonAsync(Url, body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
