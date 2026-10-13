using System.Text.Json;

namespace SecureLab.Api.Tests;

// Стабільні поля Problem Details без traceId: саме їх порівнює A-07.
public sealed record ProblemShape(
    string? Type, string? Title, int? Status, string? Detail,
    bool HasType, bool HasTitle, bool HasStatus, bool HasDetail)
{
    public static async Task<ProblemShape> ReadAsync(HttpResponseMessage reply)
    {
        var body = await reply.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return new(null, null, null, null, false, false, false, false);
        }

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        string? Text(string name) => root.TryGetProperty(name, out var value) ? value.GetString() : null;
        int? status = root.TryGetProperty("status", out var number) ? number.GetInt32() : null;
        return new(Text("type"), Text("title"), status, Text("detail"),
            root.TryGetProperty("type", out _),
            root.TryGetProperty("title", out _),
            root.TryGetProperty("status", out _),
            root.TryGetProperty("detail", out _));
    }
}
