using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

// Поглиблення: втрата чинності старого сеансу після визначеної серверної події.
// Подія: UpdateSecurityStampAsync (наприклад, після зміни пароля адміністратором).
// Момент перевірки: стандартний SecurityStampValidator; у цьому host інтервал перевірки
// зменшено до нуля, тому кожен запит звіряє stamp у cookie зі stamp у БД.
public sealed class SessionRevocationTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private static string StudyPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private WebApplicationFactory<Program> HostWithImmediateValidation() => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
            services.Configure<SecurityStampValidatorOptions>(options =>
                options.ValidationInterval = TimeSpan.Zero)));

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> host, string userName, string password)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { userName, password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    [Fact]
    public async Task OldCookie_AfterSecurityStampUpdate_Returns401_WhileFreshLoginWorks()
    {
        using var host = HostWithImmediateValidation();
        using var oldSession = await LoginAsync(host, "morgan", StudyPassword);
        using var before = await oldSession.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        // Серверна подія: оновлення security stamp користувача morgan.
        using (var scope = host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var morgan = await users.FindByNameAsync("morgan");
            Assert.NotNull(morgan);
            var result = await users.UpdateSecurityStampAsync(morgan);
            Assert.True(result.Succeeded);
        }

        using var after = await oldSession.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);

        // Новий вхід видає cookie з новим stamp і знову працює.
        using var freshSession = await LoginAsync(host, "morgan", StudyPassword);
        using var fresh = await freshSession.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
    }

    // Контроль: без серверної події та сама cookie лишається чинною навіть за негайної перевірки.
    [Fact]
    public async Task Cookie_WithoutServerEvent_StaysValid()
    {
        using var host = HostWithImmediateValidation();
        using var session = await LoginAsync(host, "alice", StudyPassword);

        using var first = await session.GetAsync("/api/me");
        using var second = await session.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }
}
