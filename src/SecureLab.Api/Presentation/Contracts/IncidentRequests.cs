namespace SecureLab.Api.Presentation.Contracts;

// ЛР 04: звичайне оновлення вмісту інциденту (PUT /api/incidents/{id}).
public sealed record UpdateIncidentRequest(
    string? Title,
    string? Description,
    string? Severity,
    DateTimeOffset? OccurredAtUtc,
    string? Status);

// ЛР 04: первинна оцінка інциденту (POST /api/incidents/{id}/triage).
public sealed record TriageIncidentRequest(string? Note);

public sealed record IncidentStatusResponse(Guid Id, string Status);
