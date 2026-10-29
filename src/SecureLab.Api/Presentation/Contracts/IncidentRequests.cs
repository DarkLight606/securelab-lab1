namespace SecureLab.Api.Presentation.Contracts;

// ЛР 04: звичайне оновлення вмісту інциденту (PUT /api/incidents/{id}). Лише content-поля:
// owner, status, роль і службові часи визначає сервер. Невідомі поля JSON binding ігнорує (A-08a).
public sealed record UpdateIncidentRequest(
    string? Title,
    string? Description,
    string? Severity,
    DateTimeOffset? OccurredAtUtc);

// ЛР 04: первинна оцінка (POST /api/incidents/{id}/triage). Наступний статус визначає сервер,
// клієнт може передати лише коментар до переходу.
public sealed record TriageIncidentRequest(string? Note);

public sealed record IncidentStatusResponse(Guid Id, string Status);
