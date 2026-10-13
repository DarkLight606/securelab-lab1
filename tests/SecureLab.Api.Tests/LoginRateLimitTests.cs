using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace SecureLab.Api.Tests;

// Обмеження невдалих спроб входу: 429 після PermitLimit невдач у вікні, інший користувач не зачеплений,
// успішні входи ліміт не витрачають, після вікна вхід відновлюється. Лічильники належать host,
// тому кожен сценарій створює окремий host із коротким вікном.
public sealed class LoginRateLimitTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private const int PermitLimit = 3;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    private static string StudyPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private WebApplicationFactory<Program> FreshHost() => factory.WithWebHostBuilder(builder =>
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Auth:LoginAttemptLimit:PermitLimit"] = PermitLimit.ToString(),
                ["Auth:LoginAttemptLimit:Window"] = Window.ToString()
            })));

    private static HttpClient Client(WebApplicationFactory<Program> host) => host.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string userName, string password)
        => client.PostAsJsonAsync("/api/auth/login", new { userName, password });

    [Fact]
    public async Task FailedLogins_ExceedingLimit_Return429_ThenRecoverAfterWindow()
    {
        using var host = FreshHost();
        using var client = Client(host);
        var probeUser = "probe-" + Guid.NewGuid().ToString("N")[..8];
        var wrongPassword = "Wrong-" + Guid.NewGuid().ToString("N")[..8] + "!A1";

        for (var attempt = 0; attempt < PermitLimit; attempt++)
        {
            using var denied = await LoginAsync(client, probeUser, wrongPassword);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }

        using var limited = await LoginAsync(client, probeUser, wrongPassword);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);

        // Інший ключ (інше ім'я з того самого клієнта) не заблоковано: 401, а не 429.
        using var otherUser = await LoginAsync(client, "other-" + Guid.NewGuid().ToString("N")[..8], wrongPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, otherUser.StatusCode);

        await Task.Delay(Window + TimeSpan.FromMilliseconds(500));
        using var recovered = await LoginAsync(client, probeUser, wrongPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, recovered.StatusCode);
    }

    // 429 повертається і для наявного акаунта, і для невідомого: обмеження не розкриває існування.
    [Fact]
    public async Task RateLimit_ForExistingAccount_SameAsUnknown()
    {
        using var host = FreshHost();
        using var client = Client(host);
        var wrongPassword = "Wrong-" + Guid.NewGuid().ToString("N")[..8] + "!A1";

        for (var attempt = 0; attempt < PermitLimit; attempt++)
        {
            using var denied = await LoginAsync(client, "bob", wrongPassword);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }

        using var limitedKnown = await LoginAsync(client, "bob", wrongPassword);
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedKnown.StatusCode);
        Assert.False(limitedKnown.Headers.Contains("Set-Cookie"));

        // Навіть правильний пароль у заблокованому вікні дає 429: спроба не доходить до перевірки пароля.
        using var limitedCorrect = await LoginAsync(client, "bob", StudyPassword);
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedCorrect.StatusCode);
        Assert.False(limitedCorrect.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task SuccessfulLogins_DoNotConsumeFailureBudget()
    {
        using var host = FreshHost();
        using var client = Client(host);

        for (var attempt = 0; attempt < PermitLimit + 2; attempt++)
        {
            using var ok = await LoginAsync(client, "alice", StudyPassword);
            Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        }

        using var wrong = await LoginAsync(client, "alice", "Wrong-" + Guid.NewGuid().ToString("N")[..8] + "!A1");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }
}
