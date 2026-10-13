namespace SecureLab.Api.Presentation.Contracts;

// Request DTO реєстрації (POST /api/auth/register), ЛР 03.
// Лише поля, які клієнт має право надіслати. Id, роль, PasswordHash і власника ресурсів
// призначає сервер, тому їх тут немає; невідомі поля JSON ігноруються під час binding.
public sealed record RegisterRequest(
    string? UserName,
    string? Email,
    string? DisplayName,
    string? Password);

// Response DTO реєстрації: безпечна відповідь без password, hash, stamp чи email.
public sealed record RegisterResponse(
    Guid Id,
    string UserName,
    string DisplayName);

// Response DTO профілю поточного користувача (GET /api/me), ЛР 03.
// Id і ролі беруться з перевіреного principal, displayName читається з БД за цим самим Id.
public sealed record CurrentUserResponse(
    Guid Id,
    string UserName,
    string DisplayName,
    IReadOnlyList<string> Roles);
