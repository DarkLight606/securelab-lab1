namespace SecureLab.Api.Presentation.Contracts;

public sealed record IncidentListItemResponse(
    Guid Id,
    string Title,
    string Severity,
    string Status,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset CreatedAtUtc);

public sealed record IncidentDetailsResponse(
    Guid Id,
    string Title,
    string Description,
    string Severity,
    string Status,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset CreatedAtUtc,
    string OwnerDisplayName,
    IReadOnlyList<IncidentCommentResponse> Comments);

public sealed record IncidentCommentResponse(
    Guid Id,
    string AuthorDisplayName,
    string Text,
    DateTimeOffset CreatedAtUtc);

public sealed record IncidentSeveritySummaryResponse(
    string Severity,
    int Count);

// Відповідь на створення інциденту (POST /api/incidents), ЛР 02.
// Містить лише поля, які клієнту дозволено бачити: без OwnerUserId і updatedAtUtc.
public sealed record CreatedIncidentResponse(
    Guid Id,
    string Title,
    string Severity,
    string Status,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset CreatedAtUtc);

// Відповідь пошуку інцидентів (GET /api/incidents/search), ЛР 02.
public sealed record SearchIncidentResponse(
    Guid Id,
    string Title,
    string Description,
    string Severity,
    string Status,
    DateTimeOffset CreatedAtUtc);
