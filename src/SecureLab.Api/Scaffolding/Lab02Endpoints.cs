using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Scaffolding;

// Навчальний старт ЛР 02. Запускати лише з локальними штучними даними.
// Пошук у цьому файлі свідомо лишається уразливим до фіксованого стану "до"; його усувають на етапі 4.
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
        // УРАЗЛИВИЙ ПОШУК (стан "до"). q і sortBy склеюються в текст SQL, тому значення може стати структурою.
        app.MapGet("/api/incidents/search", async (string? q, string? sortBy, SecureLabDbContext db, CancellationToken ct) =>
        {
            var order = sortBy switch
            {
                null or "" or "createdAtUtc" => "created_at_utc DESC",
                "severity" => "severity", "status" => "status", _ => sortBy
            };
            var sql = "SELECT * FROM incidents WHERE title ILIKE '%" + (q ?? "")
                + "%' OR description ILIKE '%" + (q ?? "") + "%' ORDER BY " + order + " LIMIT 50";
            var rows = await db.Incidents.FromSqlRaw(sql).AsNoTracking().ToListAsync(ct);
            return Results.Ok(rows.Select(row => new
            {
                row.Id, row.Title, row.Description,
                Severity = row.Severity.ToString(), Status = row.Status.ToString(), row.CreatedAtUtc
            }));
        });

        // Контракт створення інциденту 2-A: серверна валідація формату, діапазону, взаємозалежних полів
        // і предметного правила ще до доступу до БД. Клієнт не може передати owner, status чи audit-часи.
        app.MapPost("/api/incidents", async (CreateIncidentRequest request, SecureLabDbContext db, CancellationToken ct) =>
        {
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
                OwnerUserId = DbSeeder.AliceId,          // сталий demo-контекст; verified principal з'явиться в ЛР 03
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
        });
    }
}

// Request DTO: лише поля, які надсилає клієнт. Немає OwnerUserId, Status чи audit-часів,
// тому клієнт не може перезаписати server-managed поля (захист від overposting).
public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);
