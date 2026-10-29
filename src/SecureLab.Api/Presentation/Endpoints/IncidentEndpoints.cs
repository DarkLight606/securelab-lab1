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
        // ЛР 04: уся група вимагає автентифікації; анонімний запит одержує 401 ще до коду endpoint.
        var group = endpoints.MapGroup("/api/incidents")
            .WithTags("Incidents")
            .RequireAuthorization();

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
            .WithName("UpdateIncident");

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteIncident");

        group.MapPost("/{id:guid}/triage", TriageAsync)
            .WithName("TriageIncident");

        endpoints.MapGet("/api/incident-comments/{id:guid}", GetCommentAsync)
            .WithTags("Incidents")
            .WithName("GetIncidentComment")
            .RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> GetListAsync(
        string? status,
        ClaimsPrincipal user,
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

        return Results.Ok(await queries.GetListAsync(parsedStatus, user, cancellationToken));
    }

    private static async Task<IResult> GetSeveritySummaryAsync(
        string? status,
        ClaimsPrincipal user,
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

        return Results.Ok(await queries.GetSeveritySummaryAsync(allowedStatus, user, cancellationToken));
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        ClaimsPrincipal user,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var incident = await queries.GetDetailsAsync(id, user, cancellationToken);
        return incident is null ? IncidentNotFound() : Results.Ok(incident);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateIncidentRequest request,
        ClaimsPrincipal user,
        IncidentCommands commands,
        CancellationToken cancellationToken)
    {
        var result = await commands.UpdateAsync(id, request, user, cancellationToken);
        return ToResult(result.Outcome, result.Value, result.Errors);
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ClaimsPrincipal user,
        IncidentCommands commands,
        CancellationToken cancellationToken)
    {
        var outcome = await commands.DeleteAsync(id, user, cancellationToken);
        return outcome == CommandOutcome.Done ? Results.NoContent() : ToResult<object>(outcome, null, null);
    }

    private static async Task<IResult> TriageAsync(
        Guid id,
        TriageIncidentRequest? request,
        ClaimsPrincipal user,
        IncidentCommands commands,
        CancellationToken cancellationToken)
    {
        var result = await commands.TriageAsync(id, request ?? new TriageIncidentRequest(null), user, cancellationToken);
        return ToResult(result.Outcome, result.Value, result.Errors);
    }

    private static async Task<IResult> GetCommentAsync(
        Guid id,
        ClaimsPrincipal user,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var comment = await queries.GetCommentAsync(id, user, cancellationToken);
        return comment is null
            ? Results.Problem(title: "Коментар не знайдено", statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(comment);
    }

    private static IResult ToResult<T>(CommandOutcome outcome, T? value, Dictionary<string, string[]>? errors) =>
        outcome switch
        {
            CommandOutcome.Done => Results.Ok(value),
            CommandOutcome.NotFound => IncidentNotFound(),
            CommandOutcome.Forbidden => Results.Problem(
                title: "Дію заборонено",
                detail: "Ваша роль не дозволяє цю дію для цього інциденту.",
                statusCode: StatusCodes.Status403Forbidden),
            CommandOutcome.Conflict => Results.Problem(
                title: "Перехід стану неможливий",
                detail: "Первинну оцінку можна виконати лише для інциденту в статусі New.",
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.ValidationProblem(errors ?? [])
        };

    // ЛР 04, політика 404: одна відповідь для чужого й неіснуючого інциденту. У detail немає id,
    // тож тіла двох відповідей відрізняються лише traceId (A-10).
    private static IResult IncidentNotFound() =>
        Results.Problem(
            title: "Інцидент не знайдено",
            detail: "Інцидент не існує або недоступний.",
            statusCode: StatusCodes.Status404NotFound);
}
