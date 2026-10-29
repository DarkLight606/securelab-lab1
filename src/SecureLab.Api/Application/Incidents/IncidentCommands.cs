using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Authorization;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

public enum CommandOutcome
{
    Done,
    NotFound,
    Forbidden,
    Conflict,
    Invalid
}

public sealed record CommandResult<T>(CommandOutcome Outcome, T? Value = default, Dictionary<string, string[]>? Errors = null);

// ЛР 04: змінювальні дії над інцидентом. Порядок у кожній: tracked entity -> рішення handler ->
// перевірка стану -> зміна лише дозволених полів -> SaveChangesAsync.
public sealed class IncidentCommands(
    SecureLabDbContext dbContext,
    IAuthorizationService authorization,
    ILogger<IncidentCommands> logger)
{
    public async Task<CommandResult<IncidentStatusResponse>> UpdateAsync(
        Guid id, UpdateIncidentRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var incident = await dbContext.Incidents.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        var access = await DecideAsync(incident, IncidentOperations.Update, user);
        if (access != CommandOutcome.Done)
        {
            return new(access);
        }

        var errors = IncidentContentRules.Validate(
            request.Title, request.Description, request.Severity, request.OccurredAtUtc, DateTimeOffset.UtcNow,
            out var content);
        if (errors.Count > 0)
        {
            return new(CommandOutcome.Invalid, Errors: errors);
        }

        incident!.Title = content.Title;
        incident.Description = content.Description;
        incident.Severity = content.Severity;
        incident.OccurredAtUtc = content.OccurredAtUtc;
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(CommandOutcome.Done, new IncidentStatusResponse(incident.Id, incident.Status.ToString()));
    }

    public async Task<CommandOutcome> DeleteAsync(Guid id, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var incident = await dbContext.Incidents.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        var access = await DecideAsync(incident, IncidentOperations.Delete, user);
        if (access != CommandOutcome.Done)
        {
            return access;
        }

        dbContext.Incidents.Remove(incident!);
        await dbContext.SaveChangesAsync(cancellationToken);
        return CommandOutcome.Done;
    }

    public async Task<CommandResult<IncidentStatusResponse>> TriageAsync(
        Guid id, TriageIncidentRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var note = (request.Note ?? string.Empty).Trim();
        if (note.Length > 500)
        {
            return new(CommandOutcome.Invalid, Errors: new() { ["note"] = ["Коментар має бути не довшим за 500 символів."] });
        }

        var incident = await dbContext.Incidents.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        var access = await DecideAsync(incident, IncidentOperations.Triage, user);
        if (access != CommandOutcome.Done)
        {
            // trace id той самий, що ASP.NET Core пише в traceId відповіді Problem Details.
            AuthorizationAudit.Denied(logger, user.GetUserId(), "incident.triage", "incident", id,
                access == CommandOutcome.NotFound ? "denied:not-visible" : "denied:role",
                DateTimeOffset.UtcNow, Activity.Current?.Id ?? "-");
            return new(access);
        }

        // Сервер читає поточний статус із БД і сам визначає наступний.
        if (incident!.Status != IncidentStatus.New)
        {
            return new(CommandOutcome.Conflict);
        }

        var now = DateTimeOffset.UtcNow;
        dbContext.IncidentStatusHistory.Add(new IncidentStatusHistory
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            ChangedByUserId = user.GetUserId(),
            OldStatus = IncidentStatus.New,
            NewStatus = IncidentStatus.Triaged,
            Note = note.Length == 0 ? "Первинну оцінку виконано." : note,
            CreatedAtUtc = now
        });
        incident.Status = IncidentStatus.Triaged;
        incident.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(CommandOutcome.Done, new IncidentStatusResponse(incident.Id, incident.Status.ToString()));
    }

    // Невидимий для користувача інцидент і неіснуючий дають один результат NotFound (політика 404).
    // Якщо інцидент видимий, але дія заборонена, результат Forbidden (403).
    private async Task<CommandOutcome> DecideAsync(
        Incident? incident, OperationAuthorizationRequirement operation, ClaimsPrincipal user)
    {
        if (incident is null)
        {
            return CommandOutcome.NotFound;
        }

        var facts = new IncidentAccessFacts(incident.OwnerUserId, incident.Status);
        if (!(await authorization.AuthorizeAsync(user, facts, IncidentOperations.Read)).Succeeded)
        {
            return CommandOutcome.NotFound;
        }

        return (await authorization.AuthorizeAsync(user, facts, operation)).Succeeded
            ? CommandOutcome.Done
            : CommandOutcome.Forbidden;
    }
}
