using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// Оцінювані безпекові сценарії ЛР 03: A-01 ... A-07.
// Кожен сценарій є інтеграційним: запит проходить справжній cookie-handler і middleware застосунку.
// У повідомленнях про помилку немає пароля, cookie чи hash.
public sealed class AuthenticationSecurityTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private const string MeUrl = "/api/me";
    private const string IncidentsUrl = "/api/incidents";

    private static string StudyPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private static readonly string[] ForbiddenProfileFields =
        ["password", "confirmPassword", "passwordHash", "securityStamp", "concurrencyStamp", "cookie", "token", "email"];

    private HttpClient Anonymous() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    private static object IncidentBody(string title, Guid? forgedOwner = null)
    {
        var description = "Опис достатньої довжини для перевірки власника нового інциденту.";
        var occurredAtUtc = DateTimeOffset.UtcNow.AddHours(-2);
        return forgedOwner is null
            ? new { title, description, severity = "Low", occurredAtUtc }
            : new { title, description, severity = "Low", occurredAtUtc, ownerUserId = forgedOwner.Value };
    }

    private async Task<Guid> OwnerInDatabaseAsync(Guid incidentId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var incident = await db.Incidents.AsNoTracking().SingleAsync(item => item.Id == incidentId);
        return incident.OwnerUserId;
    }

    // A-01: анонімний запит до захищеного endpoint дає 401 без redirect, без HTML і без WWW-Authenticate.
    [Fact]
    public async Task A01_AnonymousGetMe_Returns401WithoutRedirect()
    {
        using var anonymous = Anonymous();

        using var response = await anonymous.GetAsync(MeUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // A-01 для операції створення: анонімний POST не створює запис і повертає 401.
    [Fact]
    public async Task A01_AnonymousCreateIncident_Returns401()
    {
        using var anonymous = Anonymous();

        using var response = await anonymous.PostAsJsonAsync(IncidentsUrl, IncidentBody($"Anon-{Guid.NewGuid():N}"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // A-01, варіант із заявленим id: без cookie header X-Demo-UserId більше не відкриває профіль.
    [Fact]
    public async Task A01_HeaderWithoutCookie_Returns401()
    {
        using var anonymous = Anonymous();
        using var request = new HttpRequestMessage(HttpMethod.Get, MeUrl);
        request.Headers.Add("X-Demo-UserId", DbSeeder.BobId.ToString());

        using var response = await anonymous.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // A-02: після login профіль формується з cookie/principal і містить лише дозволені поля.
    [Fact]
    public async Task A02_LoginThenMe_ReturnsVerifiedPrincipalWithSafeFields()
    {
        using var alice = await factory.LoginAsync("alice", StudyPassword);

        using var response = await alice.GetAsync(MeUrl);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.Equal(DbSeeder.AliceId, root.GetProperty("id").GetGuid());
        Assert.Equal("alice", root.GetProperty("userName").GetString());
        Assert.Equal("Аліса Коваль", root.GetProperty("displayName").GetString());
        Assert.Contains("Reporter", root.GetProperty("roles").EnumerateArray().Select(item => item.GetString()));
        foreach (var field in ForbiddenProfileFields)
        {
            Assert.False(root.TryGetProperty(field, out _), $"Поле {field} не повинно бути у відповіді /api/me.");
        }
    }

    // A-03: чинна cookie Alice плюс header X-Demo-UserId Bob -> відповідь лишається профілем Alice.
    [Fact]
    public async Task A03_MeWithForgedUserHeader_KeepsCookiePrincipal()
    {
        using var alice = await factory.LoginAsync("alice", StudyPassword);
        using var request = new HttpRequestMessage(HttpMethod.Get, MeUrl);
        request.Headers.Add("X-Demo-UserId", DbSeeder.BobId.ToString());

        using var response = await alice.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(DbSeeder.AliceId, document.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("alice", document.RootElement.GetProperty("userName").GetString());
    }

    // A-04: автентифікований create без поля власника -> owner нового запису дорівнює principal (Bob).
    [Fact]
    public async Task A04_AuthenticatedCreateWithoutOwner_SetsOwnerFromPrincipal()
    {
        using var bob = await factory.LoginAsync("bob", StudyPassword);

        using var response = await bob.PostAsJsonAsync(IncidentsUrl, IncidentBody($"A04-{Guid.NewGuid():N}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(created.RootElement.TryGetProperty("ownerUserId", out _));
        var incidentId = created.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(DbSeeder.BobId, await OwnerInDatabaseAsync(incidentId));

        using var details = JsonDocument.Parse(await bob.GetStringAsync($"{IncidentsUrl}/{incidentId}"));
        Assert.Equal("Боб Мельник", details.RootElement.GetProperty("ownerDisplayName").GetString());
    }

    // A-05: Bob надсилає чужий ownerUserId Alice -> запис створено, але власник лишається Bob.
    [Fact]
    public async Task A05_CreateWithForgedOwner_KeepsPrincipalOwner()
    {
        using var bob = await factory.LoginAsync("bob", StudyPassword);

        using var response = await bob.PostAsJsonAsync(
            IncidentsUrl, IncidentBody($"A05-{Guid.NewGuid():N}", forgedOwner: DbSeeder.AliceId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var incidentId = created.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(DbSeeder.BobId, await OwnerInDatabaseAsync(incidentId));
        Assert.NotEqual(DbSeeder.AliceId, await OwnerInDatabaseAsync(incidentId));
    }

    // A-06: login -> захищений запит 200 -> logout 204 -> той самий клієнт отримує 401.
    [Fact]
    public async Task A06_Logout_ThenProtectedRequest_Returns401()
    {
        using var alice = await factory.LoginAsync("alice", StudyPassword);
        using var before = await alice.GetAsync(MeUrl);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var logout = await alice.PostAsync("/api/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        // Основну cookie обираємо за назвою; значення не читаємо і не друкуємо.
        var deletion = logout.Headers.GetValues("Set-Cookie")
            .Where(value => value.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal))
            .ToArray();
        Assert.True(deletion.Length == 1, "Очікувався один Set-Cookie для основної cookie.");
        Assert.Contains("expires=thu, 01 jan 1970", deletion[0].ToLowerInvariant());

        using var after = await alice.GetAsync(MeUrl);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    // A-07: невідомий користувач і хибний пароль дають однаковий зовнішній результат (status, Content-Type,
    // стабільні поля Problem Details). traceId різний за призначенням і не порівнюється.
    [Fact]
    public async Task A07_UnknownUserAndWrongPassword_SameExternalResult()
    {
        using var first = Anonymous();
        using var second = Anonymous();
        var wrongPassword = "Wrong-" + Guid.NewGuid().ToString("N") + "!A1";

        using var unknown = await first.PostAsJsonAsync("/api/auth/login",
            new { userName = "ghost-" + Guid.NewGuid().ToString("N")[..8], password = wrongPassword });
        using var wrong = await second.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = wrongPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(unknown.StatusCode, wrong.StatusCode);
        Assert.Equal(unknown.Content.Headers.ContentType?.ToString(), wrong.Content.Headers.ContentType?.ToString());
        Assert.False(unknown.Headers.Contains("Set-Cookie"));
        Assert.False(wrong.Headers.Contains("Set-Cookie"));

        var unknownShape = await ProblemShape.ReadAsync(unknown);
        var wrongShape = await ProblemShape.ReadAsync(wrong);
        Assert.Equal(unknownShape, wrongShape);
    }
}
