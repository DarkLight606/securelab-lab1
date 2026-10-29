using System.Security.Claims;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Application.Authorization;

// ЛР 04: спільні правила видимості. Їх використовують і handler (окремий інцидент),
// і обмеження запитів для list, search та severity-summary, тож правило записане один раз.
public static class IncidentAccessRules
{
    // Ідентифікатор поточного користувача: лише claim NameIdentifier перевіреного principal.
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("Authenticated principal has no user id claim.");

    // Analyst бачить усі інциденти для аналізу; решта ролей (Reporter, Administrator) лише власні.
    public static bool SeesAllIncidents(this ClaimsPrincipal user) => user.IsInRole(DbSeeder.AnalystRole);

    // Умова стоїть у самому SQL-запиті, до сортування, Take і групування.
    public static IQueryable<Incident> VisibleTo(this IQueryable<Incident> incidents, ClaimsPrincipal user)
    {
        if (user.SeesAllIncidents())
        {
            return incidents;
        }

        var userId = user.GetUserId();
        return incidents.Where(incident => incident.OwnerUserId == userId);
    }
}
