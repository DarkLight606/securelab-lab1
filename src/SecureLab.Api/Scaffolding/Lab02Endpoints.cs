using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Authorization;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Scaffolding;

// Пошук і створення інцидентів ЛР 02. Запускати лише з локальними штучними даними.
public static class Lab02Endpoints
{
    // Активні статуси: наявність інциденту в одному з них блокує повторне створення того самого title.
    private static readonly IncidentStatus[] ActiveStatuses =
    [
        IncidentStatus.New,
        IncidentStatus.Triaged,
        IncidentStatus.InProgress,
        IncidentStatus.Resolved
    ];

    public static void MapLab02Endpoints(this WebApplication app)
    {
        // БЕЗПЕЧНИЙ ПОШУК (стан "після"). Значення q передається параметром через EF.Functions.ILike,
        // а структуру (напрямок сортування) вибирає серверний allowlist. Ім'я колонки з клієнта не береться.
        app.MapGet("/api/incidents/search", async (string? q, string? sortBy, ClaimsPrincipal user, SecureLabDbContext db, CancellationToken ct) =>
        {
            // sortBy обирає СТРУКТУРУ запиту, тому перевіряється за allowlist і не параметризується.
            var sort = string.IsNullOrEmpty(sortBy) ? "createdAtUtc" : sortBy;
            if (sort is not ("createdAtUtc" or "severity" or "status"))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["sortBy"] = ["Допустимі значення: createdAtUtc, severity, status."]
                });
            }

            // ЛР 04 (A-11): пошук іде лише серед інцидентів, які користувач має право бачити,
            // до сортування і Take(50).
            var query = db.Incidents.AsNoTracking().VisibleTo(user);

            if (!string.IsNullOrEmpty(q))
            {
                // Літеральний підрядок: екрануємо escape-символ, потім обидва wildcard ILIKE (% і _).
                const string escape = "\\";
                var pattern = "%" + q
                    .Replace(escape, escape + escape)
                    .Replace("%", escape + "%")
                    .Replace("_", escape + "_") + "%";
                query = query.Where(incident =>
                    EF.Functions.ILike(incident.Title, pattern, escape)
                    || EF.Functions.ILike(incident.Description, pattern, escape));
            }

            // Кожній назві allowlist відповідає явний LINQ-вираз. Severity і Status зберігаються текстом,
            // тому потрібне явне ранжування, а не абеткове сортування колонки.
            var ordered = sort switch
            {
                "severity" => query
                    .OrderBy(incident => incident.Severity == IncidentSeverity.Critical ? 0
                        : incident.Severity == IncidentSeverity.High ? 1
                        : incident.Severity == IncidentSeverity.Medium ? 2 : 3)
                    .ThenByDescending(incident => incident.CreatedAtUtc),
                "status" => query
                    .OrderBy(incident => incident.Status == IncidentStatus.New ? 0
                        : incident.Status == IncidentStatus.Triaged ? 1
                        : incident.Status == IncidentStatus.InProgress ? 2
                        : incident.Status == IncidentStatus.Resolved ? 3 : 4)
                    .ThenByDescending(incident => incident.CreatedAtUtc),
                _ => query.OrderByDescending(incident => incident.CreatedAtUtc)
            };

            var rows = await ordered
                .Take(50)
                .Select(incident => new SearchIncidentResponse(
                    incident.Id,
                    incident.Title,
                    incident.Description,
                    incident.Severity.ToString(),
                    incident.Status.ToString(),
                    incident.CreatedAtUtc))
                .ToListAsync(ct);

            return Results.Ok(rows);
        }).RequireAuthorization();

        // Контракт створення інциденту 2-A: серверна валідація формату, діапазону, взаємозалежних полів
        // і предметного правила ще до доступу до БД. Клієнт не може передати owner, status чи audit-часи.
        // ЛР 03: endpoint вимагає автентифікації (анонімний запит дає 401 ще до цього коду),
        // а власника нового запису бере лише з перевіреного principal.
        app.MapPost("/api/incidents", async (
            CreateIncidentRequest request,
            HttpContext httpContext,
            SecureLabDbContext db,
            CancellationToken ct) =>
        {
            // Ідентифікатор поточного користувача: claim NameIdentifier із cookie-сеансу, не з JSON чи header.
            var claimedId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(claimedId, out var ownerUserId))
            {
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
            }

            var now = DateTimeOffset.UtcNow;
            var errors = new Dictionary<string, string[]>();

            // Нормалізуємо текстові поля один раз; ці самі значення йдуть далі в правила й у запис.
            var title = (request.Title ?? string.Empty).Trim();
            var description = (request.Description ?? string.Empty).Trim();

            if (title.Length == 0)
            {
                errors["title"] = ["Вкажіть заголовок інциденту."];
            }
            else if (title.Length > 160)
            {
                errors["title"] = ["Заголовок має бути не довшим за 160 символів."];
            }

            if (description.Length == 0)
            {
                errors["description"] = ["Додайте опис інциденту."];
            }
            else if (description.Length > 4000)
            {
                errors["description"] = ["Опис має бути не довшим за 4000 символів."];
            }

            // Severity: TryParse підтверджує перетворення, IsDefined відкидає число на кшталт "7".
            var severityValid =
                Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out var severity)
                && Enum.IsDefined(severity);
            if (!severityValid)
            {
                errors["severity"] = ["Допустимі значення: Low, Medium, High, Critical."];
            }

            if (request.OccurredAtUtc is null)
            {
                errors["occurredAtUtc"] = ["Вкажіть час настання інциденту."];
            }
            else if (request.OccurredAtUtc.Value > now.AddMinutes(5))
            {
                errors["occurredAtUtc"] = ["Час настання не може бути більш ніж на 5 хвилин у майбутньому."];
            }

            // Взаємозалежне правило: для High або Critical опис після Trim має містити щонайменше 40 символів.
            if (severityValid
                && severity is IncidentSeverity.High or IncidentSeverity.Critical
                && !errors.ContainsKey("description")
                && description.Length < 40)
            {
                errors["description"] = ["Для рівня High або Critical опис має містити щонайменше 40 символів."];
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            // Предметний конфлікт: активний інцидент із таким самим title (Trim, з урахуванням регістру) -> 409.
            var duplicateActive = await db.Incidents
                .AsNoTracking()
                .AnyAsync(existing => existing.Title == title && ActiveStatuses.Contains(existing.Status), ct);
            if (duplicateActive)
            {
                return Results.Problem(
                    title: "Інцидент із таким заголовком уже існує",
                    detail: "Уже є активний інцидент із таким самим заголовком.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var incident = new Incident
            {
                Id = Guid.NewGuid(),
                OwnerUserId = ownerUserId,               // лише з перевіреного principal (ЛР 03)
                Title = title,
                Description = description,
                Severity = severity,
                Status = IncidentStatus.New,
                OccurredAtUtc = request.OccurredAtUtc!.Value,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Incidents.Add(incident);
            await db.SaveChangesAsync(ct);

            return Results.Created(
                $"/api/incidents/{incident.Id}",
                new CreatedIncidentResponse(
                    incident.Id,
                    incident.Title,
                    incident.Severity.ToString(),
                    incident.Status.ToString(),
                    incident.OccurredAtUtc,
                    incident.CreatedAtUtc));
        }).RequireAuthorization();
    }
}

// Request DTO: лише поля, які надсилає клієнт. Немає OwnerUserId, Status чи audit-часів,
// тому клієнт не може перезаписати server-managed поля (захист від overposting).
// Зайве ownerUserId у JSON ігнорується під час binding і ніколи не відновлюється з raw JSON (A-05).
public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);
