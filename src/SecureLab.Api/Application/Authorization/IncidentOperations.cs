using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace SecureLab.Api.Application.Authorization;

// ЛР 04: операції над інцидентом, для яких сервер ухвалює окреме рішення (рядки docs/access-matrix.md).
public static class IncidentOperations
{
    public static readonly OperationAuthorizationRequirement Read = new() { Name = nameof(Read) };
    public static readonly OperationAuthorizationRequirement Update = new() { Name = nameof(Update) };
    public static readonly OperationAuthorizationRequirement Delete = new() { Name = nameof(Delete) };
    public static readonly OperationAuthorizationRequirement Triage = new() { Name = nameof(Triage) };
    public static readonly OperationAuthorizationRequirement ReadInternalComments = new() { Name = nameof(ReadInternalComments) };
}

// Факти про інцидент, потрібні для рішення: власник і поточний статус, прочитані з БД, а не з запиту.
public sealed record IncidentAccessFacts(Guid OwnerUserId, Data.Entities.IncidentStatus Status);
