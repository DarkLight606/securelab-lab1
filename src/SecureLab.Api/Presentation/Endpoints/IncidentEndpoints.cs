using System.Security.Claims;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentEndpoints
{
    // Explicit allowlist of status values accepted by the severity summary.
    // Enum.TryParse would also accept a number such as "1", so only these names pass.
    private static readonly Dictionary<string, IncidentStatus> SummaryStatusAllowlist =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["New"] = IncidentStatus.New,
            ["Triaged"] = IncidentStatus.Triaged,
            ["InProgress"] = IncidentStatus.InProgress,
            ["Resolved"] = IncidentStatus.Resolved,
            ["Closed"] = IncidentStatus.Closed
        };

    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/incidents")
            .WithTags("Incidents");

        group.MapGet("/", GetListAsync)
            .WithName("GetIncidents")
            .Produces<IReadOnlyList<IncidentListItemResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetDetailsAsync)
            .WithName("GetIncidentDetails")
            .Produces<IncidentDetailsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/severity-summary", GetSeveritySummaryAsync)
            .WithName("GetIncidentSeveritySummary")
            .Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>()
            .ProducesValidationProblem();

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateIncident")
            .RequireAuthorization();

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteIncident")
            .RequireAuthorization();

        group.MapPost("/{id:guid}/triage", TriageAsync)
            .WithName("TriageIncident")
            .RequireAuthorization();

        endpoints.MapGet("/api/incident-comments/{id:guid}", GetCommentAsync)
            .WithTags("Incidents")
            .WithName("GetIncidentComment")
            .RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> GetListAsync(
        string? status,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        IncidentStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
                || !Enum.IsDefined(candidate))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
                });
            }

            parsedStatus = candidate;
        }

        return Results.Ok(await queries.GetListAsync(parsedStatus, cancellationToken));
    }

    private static async Task<IResult> GetSeveritySummaryAsync(
        string? status,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        IncidentStatus? allowedStatus = null;
        if (status is not null)
        {
            if (!SummaryStatusAllowlist.TryGetValue(status, out var candidate))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
                });
            }

            allowedStatus = candidate;
        }

        return Results.Ok(await queries.GetSeveritySummaryAsync(allowedStatus, cancellationToken));
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var incident = await queries.GetDetailsAsync(id, cancellationToken);
        return incident is null
            ? Results.Problem(
                title: "Інцидент не знайдено",
                detail: $"Інцидент '{id}' не існує.",
                statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(incident);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateIncidentRequest request,
        IncidentCommands commands,
        CancellationToken cancellationToken)
    {
        var updated = await commands.UpdateAsync(id, request, cancellationToken);
        return updated is null ? NotFound(id) : Results.Ok(updated);
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        IncidentCommands commands,
        CancellationToken cancellationToken) =>
        await commands.DeleteAsync(id, cancellationToken) ? Results.NoContent() : NotFound(id);

    private static async Task<IResult> TriageAsync(
        Guid id,
        TriageIncidentRequest? request,
        HttpContext httpContext,
        IncidentCommands commands,
        CancellationToken cancellationToken)
    {
        var actorUserId = Guid.Parse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await commands.TriageAsync(id, actorUserId, request ?? new TriageIncidentRequest(null), cancellationToken);
        return result is null ? NotFound(id) : Results.Ok(result);
    }

    private static async Task<IResult> GetCommentAsync(
        Guid id,
        IncidentCommands commands,
        CancellationToken cancellationToken)
    {
        var comment = await commands.GetCommentAsync(id, cancellationToken);
        return comment is null
            ? Results.Problem(title: "Коментар не знайдено", statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(comment);
    }

    private static IResult NotFound(Guid id) =>
        Results.Problem(
            title: "Інцидент не знайдено",
            detail: $"Інцидент '{id}' не існує.",
            statusCode: StatusCodes.Status404NotFound);
}
