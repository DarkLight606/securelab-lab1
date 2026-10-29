using System.Net.Http.Json;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

// ЛР 04: список (A-05), severity-summary і search (A-11) повертають лише дозволену множину.
// Seed: Alice володіє 1 (Medium), 3 і 5 (Low); Bob володіє 2 (High) і 4 (Low, «O'Brien»).
public sealed class ScopedReadPathTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact] // A-05
    public async Task List_AsReporter_ContainsOnlyOwnIncidents()
    {
        using var alice = await factory.LoginAsSeedUserAsync("alice");

        var rows = await alice.GetFromJsonAsync<List<IncidentListItemResponse>>("/api/incidents");

        Assert.Equal(3, rows!.Count);
        Assert.DoesNotContain(rows, row => row.Id == DbSeeder.BobIncidentId);
    }

    [Fact] // A-11
    public async Task SeveritySummary_AsReporter_CountsOnlyOwnIncidents()
    {
        using var alice = await factory.LoginAsSeedUserAsync("alice");

        var summary = await alice.GetFromJsonAsync<List<IncidentSeveritySummaryResponse>>(
            "/api/incidents/severity-summary");

        Assert.Equal(
            [new("Medium", 1), new("Low", 2)],
            summary!);
    }

    [Fact] // A-11
    public async Task Search_AsReporter_DoesNotReturnForeignMatch()
    {
        using var alice = await factory.LoginAsSeedUserAsync("alice");
        using var bob = await factory.LoginAsSeedUserAsync("bob");

        var aliceRows = await alice.GetFromJsonAsync<List<SearchIncidentResponse>>("/api/incidents/search?q=O%27Brien");
        var bobRows = await bob.GetFromJsonAsync<List<SearchIncidentResponse>>("/api/incidents/search?q=O%27Brien");

        Assert.Empty(aliceRows!);
        Assert.Single(bobRows!);
    }

    [Fact] // A-11: Analyst за матрицею бачить усю множину для аналізу
    public async Task SeveritySummary_AsAnalyst_CountsAllIncidents()
    {
        using var morgan = await factory.LoginAsSeedUserAsync("morgan");

        var summary = await morgan.GetFromJsonAsync<List<IncidentSeveritySummaryResponse>>(
            "/api/incidents/severity-summary");

        Assert.Equal(
            [new("High", 1), new("Medium", 1), new("Low", 3)],
            summary!);
    }

    [Fact] // A-11: Administrator не має окремого права на чужі записи
    public async Task List_AsAdministrator_DoesNotContainReporterIncidents()
    {
        using var admin = await factory.LoginAsSeedUserAsync("admin");

        var rows = await admin.GetFromJsonAsync<List<IncidentListItemResponse>>("/api/incidents");

        Assert.Empty(rows!);
    }
}
