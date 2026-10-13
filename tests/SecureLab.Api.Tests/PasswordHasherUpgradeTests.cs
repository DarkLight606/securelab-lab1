using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

// Поглиблення: готовність стандартного hasher до посилення параметрів.
// Критерій доказу: hash, створений зі стандартною кількістю ітерацій, перевіряється hasher-ом
// із більшою кількістю і дає SuccessRehashNeeded; UserManager під час перевірки пароля
// зберігає оновлений hash. Самі значення hash не друкуються.
public sealed class PasswordHasherUpgradeTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private const int StrongerIterations = 210_000;

    private static string FreshPassword() => "Hash-" + Guid.NewGuid().ToString("N")[..12] + "!Aa1";

    private static PasswordHasher<ApplicationUser> HasherWith(int iterations)
        => new(Options.Create(new PasswordHasherOptions { IterationCount = iterations }));

    [Fact]
    public void DefaultHash_VerifiedByStrongerHasher_ReportsRehashNeeded()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "hasher-probe", DisplayName = "Проба" };
        var password = FreshPassword();
        var defaultHasher = HasherWith(new PasswordHasherOptions().IterationCount);
        var strongerHasher = HasherWith(StrongerIterations);

        var storedHash = defaultHasher.HashPassword(user, password);
        var sameParameters = defaultHasher.VerifyHashedPassword(user, storedHash, password);
        var strongerParameters = strongerHasher.VerifyHashedPassword(user, storedHash, password);
        var wrongPassword = strongerHasher.VerifyHashedPassword(user, storedHash, password + "x");

        Assert.NotEqual(password, storedHash);
        Assert.Equal(PasswordVerificationResult.Success, sameParameters);
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, strongerParameters);
        Assert.Equal(PasswordVerificationResult.Failed, wrongPassword);
        // Два hash того самого пароля різні, бо сіль випадкова для кожного виклику.
        Assert.NotEqual(storedHash, defaultHasher.HashPassword(user, password));
    }

    [Fact]
    public async Task UserManager_WithStrongerIterations_RehashesOnSuccessfulCheck()
    {
        var userName = "rehash" + Guid.NewGuid().ToString("N")[..8];
        var password = FreshPassword();

        // Акаунт створено host-ом зі стандартними параметрами.
        using (var registrar = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }))
        {
            using var created = await registrar.PostAsJsonAsync("/api/auth/register",
                new { userName, email = userName + "@example.test", displayName = "Проба hasher", password });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        string hashBefore;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            hashBefore = (await users.FindByNameAsync(userName))!.PasswordHash!;
        }

        // Host із посиленими параметрами: успішна перевірка пароля оновлює збережений hash.
        using var strongerHost = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Configure<PasswordHasherOptions>(options => options.IterationCount = StrongerIterations)));
        string hashAfter;
        using (var scope = strongerHost.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByNameAsync(userName);
            Assert.NotNull(user);
            Assert.True(await users.CheckPasswordAsync(user, password));
            hashAfter = (await users.FindByNameAsync(userName))!.PasswordHash!;
        }

        Assert.NotEqual(hashBefore, hashAfter);
        Assert.DoesNotContain(password, hashAfter, StringComparison.Ordinal);

        // Старий host зі стандартними параметрами далі приймає оновлений hash: параметри записані в ньому.
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { userName, password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }
}
