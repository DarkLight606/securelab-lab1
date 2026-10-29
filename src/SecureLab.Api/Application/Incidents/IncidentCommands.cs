using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

// ЛР 04: змінювальні дії над інцидентом. Інцидент завантажується за id без порівняння з principal.
public sealed class IncidentCommands(SecureLabDbContext dbContext)
{
    public async Task<IncidentDetailsResponse?> UpdateAsync(
        Guid id, UpdateIncidentRequest request, CancellationToken cancellationToken)
    {
        var incident = await dbContext.Incidents.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (incident is null)
        {
            return null;
        }

        incident.Title = (request.Title ?? incident.Title).Trim();
        incident.Description = (request.Description ?? incident.Description).Trim();
        if (Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out var severity)
            && Enum.IsDefined(severity))
        {
            incident.Severity = severity;
        }

        if (Enum.TryParse<IncidentStatus>(request.Status, ignoreCase: true, out var status)
            && Enum.IsDefined(status))
        {
            incident.Status = status;
        }

        incident.OccurredAtUtc = request.OccurredAtUtc ?? incident.OccurredAtUtc;
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new IncidentDetailsResponse(
            incident.Id, incident.Title, incident.Description, incident.Severity.ToString(),
            incident.Status.ToString(), incident.OccurredAtUtc, incident.CreatedAtUtc, string.Empty, []);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var incident = await dbContext.Incidents.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (incident is null)
        {
            return false;
        }

        dbContext.Incidents.Remove(incident);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IncidentStatusResponse?> TriageAsync(
        Guid id, Guid actorUserId, TriageIncidentRequest request, CancellationToken cancellationToken)
    {
        var incident = await dbContext.Incidents.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (incident is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        dbContext.IncidentStatusHistory.Add(new IncidentStatusHistory
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            ChangedByUserId = actorUserId,
            OldStatus = incident.Status,
            NewStatus = IncidentStatus.Triaged,
            Note = (request.Note ?? string.Empty).Trim(),
            CreatedAtUtc = now
        });
        incident.Status = IncidentStatus.Triaged;
        incident.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new IncidentStatusResponse(incident.Id, incident.Status.ToString());
    }

    public Task<IncidentCommentResponse?> GetCommentAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.IncidentComments
            .AsNoTracking()
            .Where(comment => comment.Id == id)
            .Select(comment => new IncidentCommentResponse(
                comment.Id, comment.Author.DisplayName, comment.Text, comment.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
}
