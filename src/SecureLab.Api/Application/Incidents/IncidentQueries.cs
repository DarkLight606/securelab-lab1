using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Authorization;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

public sealed class IncidentQueries(
    SecureLabDbContext dbContext,
    IAuthorizationService authorization,
    ILogger<IncidentQueries> logger)
{
    // Order of the severity summary: from the most critical level to the least critical.
    // Severity is stored as text, so ORDER BY in SQL would sort alphabetically
    // (High, Low, Medium). The order is therefore fixed here, in code.
    private static readonly IncidentSeverity[] SeveritySummaryOrder =
    [
        IncidentSeverity.Critical,
        IncidentSeverity.High,
        IncidentSeverity.Medium,
        IncidentSeverity.Low
    ];

    public async Task<IReadOnlyList<IncidentListItemResponse>> GetListAsync(
        IncidentStatus? status,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incidents with status filter {Status}", status);

        // ЛР 04: список обмежено дозволеною множиною ще в SQL-запиті.
        var query = dbContext.Incidents.AsNoTracking().VisibleTo(user);
        if (status is not null)
        {
            query = query.Where(incident => incident.Status == status);
        }

        return await query
            .OrderByDescending(incident => incident.CreatedAtUtc)
            .Select(incident => new IncidentListItemResponse(
                incident.Id,
                incident.Title,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IncidentSeveritySummaryResponse>> GetSeveritySummaryAsync(
        IncidentStatus? status,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        // ЛР 04 (A-11): групування рахує лише інциденти, які користувач має право бачити.
        var query = dbContext.Incidents.AsNoTracking().VisibleTo(user);
        if (status is not null)
        {
            query = query.Where(incident => incident.Status == status);
        }

        // Zero-group policy: only existing groups. GroupBy returns only severities
        // that have incidents, so a level without incidents is absent and an empty
        // selection gives an empty array.
        var groups = await query
            .GroupBy(incident => incident.Severity)
            .Select(group => new { Severity = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var summary = groups
            .OrderBy(group => Array.IndexOf(SeveritySummaryOrder, group.Severity))
            .Select(group => new IncidentSeveritySummaryResponse(group.Severity.ToString(), group.Count))
            .ToList();

        logger.LogInformation(
            "Built severity summary with {GroupCount} groups for status filter {Status}",
            summary.Count,
            status);

        return summary;
    }

    public async Task<IncidentDetailsResponse?> GetDetailsAsync(
        Guid id,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incident {IncidentId}", id);

        // ЛР 04: спершу факти для рішення (власник і статус), потім рішення handler, лише після дозволу дані.
        var facts = await dbContext.Incidents
            .AsNoTracking()
            .Where(incident => incident.Id == id)
            .Select(incident => new IncidentAccessFacts(incident.OwnerUserId, incident.Status))
            .SingleOrDefaultAsync(cancellationToken);
        if (facts is null || !(await authorization.AuthorizeAsync(user, facts, IncidentOperations.Read)).Succeeded)
        {
            return null;
        }

        var showInternal = (await authorization.AuthorizeAsync(user, facts, IncidentOperations.ReadInternalComments)).Succeeded;

        return await dbContext.Incidents
            .AsNoTracking()
            .Where(incident => incident.Id == id)
            .Select(incident => new IncidentDetailsResponse(
                incident.Id,
                incident.Title,
                incident.Description,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc,
                incident.Owner.DisplayName,
                incident.Comments
                    .Where(comment => showInternal || !comment.IsInternal)
                    .OrderBy(comment => comment.CreatedAtUtc)
                    .Select(comment => new IncidentCommentResponse(
                        comment.Id,
                        comment.Author.DisplayName,
                        comment.Text,
                        comment.CreatedAtUtc))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    // ЛР 04: дочірній ресурс. Доступ до коментаря випливає з доступу до батьківського інциденту;
    // internal comment бачить лише той, кому handler дозволив ReadInternalComments.
    public async Task<IncidentCommentResponse?> GetCommentAsync(
        Guid id,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.IncidentComments
            .AsNoTracking()
            .Where(comment => comment.Id == id)
            .Select(comment => new
            {
                Facts = new IncidentAccessFacts(comment.Incident.OwnerUserId, comment.Incident.Status),
                comment.IsInternal,
                Response = new IncidentCommentResponse(
                    comment.Id, comment.Author.DisplayName, comment.Text, comment.CreatedAtUtc)
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null || !(await authorization.AuthorizeAsync(user, row.Facts, IncidentOperations.Read)).Succeeded)
        {
            return null;
        }

        if (row.IsInternal
            && !(await authorization.AuthorizeAsync(user, row.Facts, IncidentOperations.ReadInternalComments)).Succeeded)
        {
            return null;
        }

        return row.Response;
    }
}
