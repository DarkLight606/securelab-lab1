using System.Net;
using System.Text.Json.Nodes;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// ЛР 04: дочірній ресурс IncidentComment. Доступ іде через батьківський інцидент.
public sealed class CommentAccessTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact] // A-12
    public async Task Comment_OnForeignIncident_ReturnsNotFound()
    {
        using var alice = await factory.LoginAsSeedUserAsync("alice");

        using var response = await alice.GetAsync($"/api/incident-comments/{DbSeeder.BobCommentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("журналу автентифікації", await response.Content.ReadAsStringAsync());
    }

    [Fact] // A-12
    public async Task InternalComment_ForReporterOwner_IsHiddenInDetailsAndDirectRoute()
    {
        using var alice = await factory.LoginAsSeedUserAsync("alice");

        using var direct = await alice.GetAsync($"/api/incident-comments/{DbSeeder.InternalCommentId}");
        var details = await alice.ReadIncidentAsync(DbSeeder.AliceIncidentId);

        Assert.Equal(HttpStatusCode.NotFound, direct.StatusCode);
        Assert.DoesNotContain(CommentTexts(details), text => text.StartsWith("Службово", StringComparison.Ordinal));
        Assert.Contains("Заголовки листа передано аналітику.", CommentTexts(details));
    }

    [Fact]
    public async Task InternalComment_ForAnalyst_IsVisibleAfterParentCheck()
    {
        using var morgan = await factory.LoginAsSeedUserAsync("morgan");

        using var direct = await morgan.GetAsync($"/api/incident-comments/{DbSeeder.InternalCommentId}");
        var details = await morgan.ReadIncidentAsync(DbSeeder.AliceIncidentId);

        Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
        Assert.Contains(CommentTexts(details), text => text.StartsWith("Службово", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OwnComment_ForReporterOwner_IsReturned()
    {
        using var alice = await factory.LoginAsSeedUserAsync("alice");

        using var response = await alice.GetAsync($"/api/incident-comments/{DbSeeder.AliceCommentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static List<string> CommentTexts(JsonObject details) =>
        details["comments"]!.AsArray().Select(comment => comment!["text"]!.GetValue<string>()).ToList();
}
