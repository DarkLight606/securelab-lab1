using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// ЛР 04: привілейована дія POST /api/incidents/{id}/triage (лише Analyst, лише New -> Triaged).
public sealed class IncidentTriageTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact] // A-06
    public async Task Triage_AsReporterOwner_ReturnsForbidden_AndStatusStaysNew()
    {
        using var alice = await factory.LoginAsSeedUserAsync("alice");

        using var response = await alice.PostAsJsonAsync(
            $"/api/incidents/{IncidentTestData.AliceNewIncidentId}/triage", new { note = "Спроба Reporter" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var after = await alice.ReadIncidentAsync(IncidentTestData.AliceNewIncidentId);
        Assert.Equal("New", after["status"]!.GetValue<string>());
    }

    [Fact] // A-07
    public async Task Triage_AsAnalyst_MovesNewToTriaged()
    {
        using var alice = await factory.LoginAsSeedUserAsync("alice");
        using var morgan = await factory.LoginAsSeedUserAsync("morgan");
        var id = await alice.CreateOwnIncidentAsync("triage");
        try
        {
            using var response = await morgan.PostAsJsonAsync($"/api/incidents/{id}/triage", new { note = "Перша оцінка" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonObject>();
            Assert.Equal("Triaged", body!["status"]!.GetValue<string>());
            Assert.Equal("Triaged", (await alice.ReadIncidentAsync(id))["status"]!.GetValue<string>());
        }
        finally
        {
            await factory.RemoveIncidentAsync(id);
        }
    }

    [Fact] // A-07: роль не дає зайвого переходу
    public async Task Triage_AsAnalyst_ForNonNewIncident_ReturnsConflict_AndStatusUnchanged()
    {
        using var morgan = await factory.LoginAsSeedUserAsync("morgan");

        using var response = await morgan.PostAsJsonAsync($"/api/incidents/{DbSeeder.BobIncidentId}/triage", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("InProgress", (await morgan.ReadIncidentAsync(DbSeeder.BobIncidentId))["status"]!.GetValue<string>());
    }

    [Fact] // A-06 для чужого ресурсу: Reporter не бачить інцидент Alice
    public async Task Triage_AsOtherReporter_ReturnsNotFound()
    {
        using var bob = await factory.LoginAsSeedUserAsync("bob");
        using var alice = await factory.LoginAsSeedUserAsync("alice");

        using var response = await bob.PostAsJsonAsync($"/api/incidents/{IncidentTestData.AliceNewIncidentId}/triage", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("New", (await alice.ReadIncidentAsync(IncidentTestData.AliceNewIncidentId))["status"]!.GetValue<string>());
    }

    [Fact] // A-09
    public async Task Triage_Denied_WritesSafeAuditEventWithResponseTraceId()
    {
        using var alice = await factory.LoginAsSeedUserAsync("alice");
        factory.LogEntries.Clear();

        using var response = await alice.PostAsJsonAsync(
            $"/api/incidents/{IncidentTestData.AliceNewIncidentId}/triage", new { note = "секретна нотатка" });
        var problem = await response.Content.ReadFromJsonAsync<JsonObject>();
        var traceId = problem!["traceId"]!.GetValue<string>();

        var audit = Assert.Single(factory.LogEntries, entry => entry.EventId.Name == "authorization.denied");
        Assert.Contains($"subject={DbSeeder.AliceId}", audit.Message);
        Assert.Contains("action=incident.triage", audit.Message);
        Assert.Contains($"resource=incident/{IncidentTestData.AliceNewIncidentId}", audit.Message);
        Assert.Contains($"traceId={traceId}", audit.Message);
        Assert.DoesNotContain(CookieClient.SeedPassword, audit.Message);
        Assert.DoesNotContain("секретна нотатка", audit.Message);
        Assert.DoesNotContain(".AspNetCore", audit.Message);
    }
}
