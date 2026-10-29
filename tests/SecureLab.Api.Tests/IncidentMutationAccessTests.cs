using System.Net;
using System.Net.Http.Json;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// ЛР 04: update і delete. Кожна відмова перевіряється повторним читанням стану власником.
public sealed class IncidentMutationAccessTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact] // A-04
    public async Task Update_AsNonOwner_ReturnsNotFound_AndDoesNotChangeIncident()
    {
        using var alice = await factory.LoginAsSeedUserAsync("alice");
        using var bob = await factory.LoginAsSeedUserAsync("bob");
        var before = await bob.ReadIncidentAsync(DbSeeder.BobIncidentId);

        using var response = await alice.PutAsJsonAsync(
            $"/api/incidents/{DbSeeder.BobIncidentId}", IncidentTestData.ContentUpdate("Змінено Alice"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var after = await bob.ReadIncidentAsync(DbSeeder.BobIncidentId);
        Assert.Equal(before.ToJsonString(), after.ToJsonString());
    }

    [Fact] // A-04
    public async Task Delete_AsNonOwner_ReturnsNotFound_AndIncidentRemains()
    {
        using var alice = await factory.LoginAsSeedUserAsync("alice");
        using var bob = await factory.LoginAsSeedUserAsync("bob");

        using var response = await alice.DeleteAsync($"/api/incidents/{DbSeeder.BobIncidentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var after = await bob.ReadIncidentAsync(DbSeeder.BobIncidentId);
        Assert.Equal("Невідома спроба входу", after["title"]!.GetValue<string>());
    }

    [Fact] // A-03R: аналітичне читання не дає права змінювати вміст
    public async Task Update_AsAnalyst_ReturnsForbidden_AndDoesNotChangeIncident()
    {
        using var morgan = await factory.LoginAsSeedUserAsync("morgan");
        using var alice = await factory.LoginAsSeedUserAsync("alice");
        var before = await alice.ReadIncidentAsync(DbSeeder.AliceIncidentId);

        using var response = await morgan.PutAsJsonAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}", IncidentTestData.ContentUpdate("Змінено Morgan"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var after = await alice.ReadIncidentAsync(DbSeeder.AliceIncidentId);
        Assert.Equal(before.ToJsonString(), after.ToJsonString());
    }

    [Fact] // позитивна регресія update
    public async Task Update_AsOwner_ChangesOnlyContent()
    {
        using var alice = await factory.LoginAsSeedUserAsync("alice");
        var id = await alice.CreateOwnIncidentAsync("update");
        try
        {
            using var response = await alice.PutAsJsonAsync($"/api/incidents/{id}", IncidentTestData.ContentUpdate("Оновлено власником"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var after = await alice.ReadIncidentAsync(id);
            Assert.Equal("Оновлено власником", after["title"]!.GetValue<string>());
            Assert.Equal("New", after["status"]!.GetValue<string>());
        }
        finally
        {
            await factory.RemoveIncidentAsync(id);
        }
    }

    [Fact] // A-08a
    public async Task Update_WithForgedOwnerRoleAndStatus_IgnoresProtectedFields()
    {
        using var alice = await factory.LoginAsSeedUserAsync("alice");
        using var bob = await factory.LoginAsSeedUserAsync("bob");
        var id = await alice.CreateOwnIncidentAsync("overposting");
        try
        {
            using var response = await alice.PutAsJsonAsync($"/api/incidents/{id}", new
            {
                title = "Дозволена зміна вмісту",
                description = "Тестовий запис авторизації без реальних даних.",
                severity = "Low",
                occurredAtUtc = "2026-10-01T08:00:00+00:00",
                status = "Closed",
                ownerUserId = DbSeeder.BobId,
                role = "Administrator"
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var after = await alice.ReadIncidentAsync(id);
            Assert.Equal("Дозволена зміна вмісту", after["title"]!.GetValue<string>());
            Assert.Equal("New", after["status"]!.GetValue<string>());
            Assert.Equal("Аліса Коваль", after["ownerDisplayName"]!.GetValue<string>());
            using var bobView = await bob.GetAsync($"/api/incidents/{id}");
            Assert.Equal(HttpStatusCode.NotFound, bobView.StatusCode);
            using var me = await alice.GetAsync("/api/me");
            Assert.DoesNotContain("Administrator", await me.Content.ReadAsStringAsync());
        }
        finally
        {
            await factory.RemoveIncidentAsync(id);
        }
    }

    [Fact] // позитивна регресія delete
    public async Task Delete_AsOwnerOfNewIncident_RemovesIt()
    {
        using var alice = await factory.LoginAsSeedUserAsync("alice");
        var id = await alice.CreateOwnIncidentAsync("delete");
        try
        {
            using var response = await alice.DeleteAsync($"/api/incidents/{id}");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            using var after = await alice.GetAsync($"/api/incidents/{id}");
            Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
        }
        finally
        {
            await factory.RemoveIncidentAsync(id);
        }
    }

    [Fact] // умова delete: запис, який уже аналізують, власник не прибирає
    public async Task Delete_AsOwnerOfTriagedIncident_ReturnsForbidden_AndIncidentRemains()
    {
        using var alice = await factory.LoginAsSeedUserAsync("alice");

        using var response = await alice.DeleteAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var after = await alice.ReadIncidentAsync(DbSeeder.AliceIncidentId);
        Assert.Equal("Triaged", after["status"]!.GetValue<string>());
    }
}
