namespace SecureLab.Api.Application.Authorization;

// ЛР 04: аудиторська подія про відмову в привілейованій дії. Лише структуровані поля:
// хто (id з principal), що, над яким ресурсом, результат, серверний час і trace id відповіді.
// Пароля, cookie, token і тіла запиту тут немає.
public static partial class AuthorizationAudit
{
    [LoggerMessage(EventId = 4001, EventName = "authorization.denied", Level = LogLevel.Warning,
        Message = "authorization.denied subject={SubjectId} action={Action} resource={ResourceType}/{ResourceId} "
            + "result={Result} at={OccurredAtUtc:O} traceId={TraceId}")]
    public static partial void Denied(
        ILogger logger,
        Guid subjectId,
        string action,
        string resourceType,
        Guid resourceId,
        string result,
        DateTimeOffset occurredAtUtc,
        string traceId);
}
