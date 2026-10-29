using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// ЛР 04: читання деталей інциденту (головна action). Політика відмови 404.
public sealed class IncidentAccessTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact] // A-01
    public async Task Details_AsAnonymous_Returns401_WithoutRedirect()
    {
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await anonymous.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain("Підозрілий лист", await response.Content.ReadAsStringAsync());
    }

    [Fact] // A-02
    public async Task Details_AsOwner_ReturnsOwnIncident()
    {
        using var alice = await factory.LoginAsSeedUserAsync("alice");

        var incident = await alice.ReadIncidentAsync(DbSeeder.AliceIncidentId);

        Assert.Equal(DbSeeder.AliceIncidentId, incident["id"]!.GetValue<Guid>());
        Assert.Equal("Аліса Коваль", incident["ownerDisplayName"]!.GetValue<string>());
    }

    [Fact] // A-03
    public async Task Details_AsNonOwner_ReturnsNotFound_WithoutForeignData()
    {
        using var alice = await factory.LoginAsSeedUserAsync("alice");

        using var response = await alice.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Невідома спроба входу", body);
        Assert.DoesNotContain("Боб Мельник", body);
    }

    [Fact] // A-03R
    public async Task Details_AsAnalyst_ReturnsForeignIncidentForAnalysis()
    {
        using var morgan = await factory.LoginAsSeedUserAsync("morgan");

        var incident = await morgan.ReadIncidentAsync(DbSeeder.AliceIncidentId);

        Assert.Equal("Аліса Коваль", incident["ownerDisplayName"]!.GetValue<string>());
    }

    [Fact] // A-10
    public async Task Details_ForForeignAndUnknownId_AreIndistinguishable()
    {
        using var alice = await factory.LoginAsSeedUserAsync("alice");

        using var foreign = await alice.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");
        using var unknown = await alice.GetAsync($"/api/incidents/{Guid.NewGuid()}");

        Assert.Equal(unknown.StatusCode, foreign.StatusCode);
        Assert.Equal(
            unknown.Content.Headers.ContentType?.ToString(),
            foreign.Content.Headers.ContentType?.ToString());
        var foreignBody = await foreign.Content.ReadFromJsonAsync<JsonObject>();
        var unknownBody = await unknown.Content.ReadFromJsonAsync<JsonObject>();
        foreignBody!.Remove("traceId");
        unknownBody!.Remove("traceId");
        Assert.True(JsonNode.DeepEquals(unknownBody, foreignBody));
    }

    [Fact] // A-01 для mutation perimeter
    public async Task Update_AsAnonymous_Returns401()
    {
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await anonymous.PutAsJsonAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}", IncidentTestData.ContentUpdate("Анонім"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
