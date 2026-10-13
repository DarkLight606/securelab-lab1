using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

// Контракт реєстрації ЛР 03: T-01, T-02, послідовний duplicate 409, відмови 400 і межа DTO.
// Пароль нового користувача формується у тесті і не друкується; у відповідях його не має бути.
public sealed class RegistrationTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private const string Url = "/api/auth/register";

    private static readonly string[] ForbiddenFields =
        ["password", "confirmPassword", "passwordHash", "securityStamp", "concurrencyStamp", "cookie", "token", "email"];

    private static string FreshPassword() => "Reg-" + Guid.NewGuid().ToString("N")[..12] + "!Aa1";

    private static string FreshName() => "reg" + Guid.NewGuid().ToString("N")[..8];

    private static object Body(string userName, string password, string? email = null, string? displayName = "Студент 01")
        => new { userName, email = email ?? userName + "@example.test", displayName, password };

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    // T-01: коректна реєстрація -> 201, Location /api/me, безпечний DTO; новий користувач може увійти.
    [Fact]
    public async Task T01_Register_ValidRequest_Returns201SafeDtoAndUserCanLogin()
    {
        using var client = Client();
        var userName = FreshName();
        var password = FreshPassword();

        using var response = await client.PostAsJsonAsync(Url, Body(userName, password));
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/me", response.Headers.Location?.ToString());
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("id", out _));
        Assert.Equal(userName, root.GetProperty("userName").GetString());
        Assert.Equal("Студент 01", root.GetProperty("displayName").GetString());
        Assert.Equal(3, root.EnumerateObject().Count());
        foreach (var field in ForbiddenFields)
        {
            Assert.False(root.TryGetProperty(field, out _), $"Поле {field} не повинно бути у відповіді register.");
        }
        Assert.DoesNotContain(password, payload, StringComparison.Ordinal);

        // Реєстрація не виконує автоматичний вхід; пароль перевіряє стандартний hasher під час login.
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { userName, password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    // T-02 і duplicate-контракт: повтор тієї самої реєстрації, зайняте ім'я та зайнятий email дають однаковий 409.
    [Fact]
    public async Task T02_Register_Duplicate_Returns409Consistently()
    {
        using var client = Client();
        var userName = FreshName();
        var password = FreshPassword();
        using var first = await client.PostAsJsonAsync(Url, Body(userName, password));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var repeat = await client.PostAsJsonAsync(Url, Body(userName, password));
        using var sameName = await client.PostAsJsonAsync(Url, Body(userName, FreshPassword(), email: FreshName() + "@example.test"));
        using var sameEmail = await client.PostAsJsonAsync(Url, Body(FreshName(), FreshPassword(), email: userName + "@example.test"));

        foreach (var response in new[] { repeat, sameName, sameEmail })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        var repeatShape = await ProblemShape.ReadAsync(repeat);
        Assert.Equal(repeatShape, await ProblemShape.ReadAsync(sameName));
        Assert.Equal(repeatShape, await ProblemShape.ReadAsync(sameEmail));
        Assert.DoesNotContain(password, await repeat.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // T-02, слабкий пароль: 400 з ключем password; пароль у відповіді не повторюється.
    [Fact]
    public async Task Register_WeakPassword_Returns400WithPasswordKey()
    {
        using var client = Client();
        const string weak = "short";

        using var response = await client.PostAsJsonAsync(Url, Body(FreshName(), weak));
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(payload);
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("password", out _));
        Assert.DoesNotContain("\"short\"", payload, StringComparison.Ordinal);
    }

    // T-02, порожній запит: 400 з ключами всіх обов'язкових полів.
    [Fact]
    public async Task Register_MissingFields_Returns400WithFieldKeys()
    {
        using var client = Client();

        using var response = await client.PostAsJsonAsync(Url, new { });
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = document.RootElement.GetProperty("errors");
        foreach (var key in new[] { "userName", "email", "displayName", "password" })
        {
            Assert.True(errors.TryGetProperty(key, out _), $"Очікувався ключ {key}.");
        }
    }

    // Недозволені символи в імені та хибний формат email -> 400 з відповідними ключами.
    [Fact]
    public async Task Register_InvalidUserNameAndEmail_Returns400()
    {
        using var client = Client();

        using var response = await client.PostAsJsonAsync(Url,
            Body("bad name!", FreshPassword(), email: "not-an-email"));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = document.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("userName", out _));
        Assert.True(errors.TryGetProperty("email", out _));
    }

    // Знайдений додатковий дефект: displayName зі 121 символу давав 500 (помилка БД).
    // Після виправлення: 400 з ключем displayName, обліковий запис не створено.
    [Fact]
    public async Task Register_DisplayNameOverLimit_Returns400AndCreatesNothing()
    {
        using var client = Client();
        var userName = FreshName();

        using var response = await client.PostAsJsonAsync(Url,
            Body(userName, FreshPassword(), displayName: new string('д', 121)));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("displayName", out _));

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await users.FindByNameAsync(userName));
    }

    // Межа DTO: зайві role та id у JSON нічого не змінюють; роль лишається Reporter, Id генерує сервер.
    [Fact]
    public async Task Register_ExtraRoleAndIdFields_AreIgnored()
    {
        using var client = Client();
        var userName = FreshName();
        var suppliedId = Guid.Parse("99999999-0000-0000-0000-000000000001");
        var body = new
        {
            userName,
            email = userName + "@example.test",
            displayName = "Студент 02",
            password = FreshPassword(),
            role = "Administrator",
            id = suppliedId
        };

        using var response = await client.PostAsJsonAsync(Url, body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.NotEqual(suppliedId, document.RootElement.GetProperty("id").GetGuid());

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByNameAsync(userName);
        Assert.NotNull(user);
        Assert.Equal(["Reporter"], await users.GetRolesAsync(user));
    }
}
