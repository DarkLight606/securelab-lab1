using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace SecureLab.Api.Tests;

// ЛР 04: спільні кроки тестів авторизації. Позитивні змінювальні тести працюють лише з інцидентом,
// який створили самі (новий запис завжди New), і прибирають його у finally.
public static class IncidentTestData
{
    public static readonly Guid AliceNewIncidentId = Guid.Parse("20000000-0000-0000-0000-000000000005");

    public static async Task<Guid> CreateOwnIncidentAsync(this HttpClient owner, string label)
    {
        using var response = await owner.PostAsJsonAsync("/api/incidents", new
        {
            title = $"ЛР4 {label} {Guid.NewGuid():N}",
            description = "Тестовий запис авторизації без реальних даних.",
            severity = "Low",
            occurredAtUtc = "2026-10-01T08:00:00+00:00"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        return body!["id"]!.GetValue<Guid>();
    }

    public static async Task<JsonObject> ReadIncidentAsync(this HttpClient reader, Guid id)
    {
        using var response = await reader.GetAsync($"/api/incidents/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    public static object ContentUpdate(string title) => new
    {
        title,
        description = "Система зафіксувала вхід до облікового запису з нового пристрою.",
        severity = "High",
        occurredAtUtc = "2026-08-02T14:10:00+00:00"
    };
}
