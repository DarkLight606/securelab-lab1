using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SecureLab.Api.Tests;

// Фактичні атрибути cookie фіксованого профілю. Значення cookie не читається і не друкується.
public sealed class CookieProfileTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private const string CookiePrefix = ".AspNetCore.Identity.Application=";

    private static string StudyPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private HttpClient Client(string baseAddress) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri(baseAddress),
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    // Перша частина "назва=значення" відкидається; лишаються тільки атрибути.
    private static string[] Attributes(HttpResponseMessage response)
    {
        var matching = response.Headers.GetValues("Set-Cookie")
            .Where(value => value.StartsWith(CookiePrefix, StringComparison.Ordinal))
            .ToArray();
        Assert.True(matching.Length == 1, "Очікувався один Set-Cookie для основної cookie.");
        return matching[0].Split(';').Skip(1).Select(part => part.Trim().ToLowerInvariant()).ToArray();
    }

    // HTTPS-запит (production-подібний сценарій): httponly, secure, samesite=lax, path=/, без expires.
    [Fact]
    public async Task Login_OverHttps_SetsHttpOnlySecureLaxSessionCookie()
    {
        using var client = Client("https://localhost");

        using var response = await client.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password = StudyPassword });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var attributes = Attributes(response);
        Assert.Contains("httponly", attributes);
        Assert.Contains("secure", attributes);
        Assert.Contains("samesite=lax", attributes);
        Assert.Contains("path=/", attributes);
        Assert.DoesNotContain(attributes, attribute => attribute.StartsWith("expires=") || attribute.StartsWith("max-age="));
    }

    // Локальний HTTP у Development: політика SameAsRequest не додає secure, решта атрибутів та сама.
    [Fact]
    public async Task Login_OverHttp_InDevelopment_OmitsSecureAttribute()
    {
        using var client = Client("http://localhost");

        using var response = await client.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password = StudyPassword });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var attributes = Attributes(response);
        Assert.Contains("httponly", attributes);
        Assert.Contains("samesite=lax", attributes);
        Assert.DoesNotContain("secure", attributes);
    }

    // Logout наказує браузеру видалити основну cookie: порожнє значення і дата в минулому.
    [Fact]
    public async Task Logout_DeletesApplicationCookie()
    {
        using var alice = await factory.LoginAsync("alice", StudyPassword);

        using var logout = await alice.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var deletion = logout.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(CookiePrefix, StringComparison.Ordinal));
        Assert.StartsWith(CookiePrefix + ";", deletion, StringComparison.Ordinal);
        Assert.Contains("expires=thu, 01 jan 1970", deletion.ToLowerInvariant());
    }
}
