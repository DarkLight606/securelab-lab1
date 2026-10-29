using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Application.Incidents;

public sealed record IncidentContent(string Title, string Description, IncidentSeverity Severity, DateTimeOffset OccurredAtUtc);

// ЛР 04: правила content-полів для оновлення. Межі ті самі, що в контракті створення ЛР 02.
public static class IncidentContentRules
{
    public static Dictionary<string, string[]> Validate(
        string? rawTitle, string? rawDescription, string? rawSeverity, DateTimeOffset? occurredAtUtc,
        DateTimeOffset now, out IncidentContent content)
    {
        var errors = new Dictionary<string, string[]>();
        var title = (rawTitle ?? string.Empty).Trim();
        var description = (rawDescription ?? string.Empty).Trim();

        if (title.Length is 0 or > 160)
        {
            errors["title"] = ["Заголовок обов'язковий і не довший за 160 символів."];
        }

        if (description.Length is 0 or > 4000)
        {
            errors["description"] = ["Опис обов'язковий і не довший за 4000 символів."];
        }

        var severityValid = Enum.TryParse<IncidentSeverity>(rawSeverity, ignoreCase: true, out var severity)
            && Enum.IsDefined(severity)
            && !int.TryParse(rawSeverity, out _);
        if (!severityValid)
        {
            errors["severity"] = ["Допустимі значення: Low, Medium, High, Critical."];
        }

        if (occurredAtUtc is null || occurredAtUtc.Value > now.AddMinutes(5))
        {
            errors["occurredAtUtc"] = ["Вкажіть час настання, не пізніший за поточний більш ніж на 5 хвилин."];
        }

        if (severityValid && severity is IncidentSeverity.High or IncidentSeverity.Critical
            && !errors.ContainsKey("description") && description.Length < 40)
        {
            errors["description"] = ["Для рівня High або Critical опис має містити щонайменше 40 символів."];
        }

        content = new IncidentContent(title, description, severity, occurredAtUtc?.ToUniversalTime() ?? default);
        return errors;
    }
}
