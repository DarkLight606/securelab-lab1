namespace SecureLab.Api.Application.Auth;

// Мінімальні події автентифікації для журналу застосунку.
// Поле події містить лише тип події, обережний опис користувача, результат і trace id.
// Навмисно виключено: password, cookie, password hash, security stamp, token, заголовок
// Authorization і тіло запиту. Повне ім'я користувача теж не пишеться: у поле імені під час
// невдалої спроби людина могла випадково ввести пароль.
public static partial class AuthenticationEvents
{
    public const string LoginSucceeded = "login.succeeded";
    public const string LoginFailed = "login.failed";
    public const string LoginRateLimited = "login.rate_limited";
    public const string Logout = "logout";
    public const string RegisterSucceeded = "register.succeeded";
    public const string RegisterRejected = "register.rejected";

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Authentication event {EventType}: user {UserHint}, result {Result}, trace {TraceId}")]
    public static partial void Record(ILogger logger, string eventType, string userHint, string result, string traceId);

    // Підказка замість імені: перші два символи і довжина, наприклад "al***(5)".
    public static string UserHint(string? userName)
    {
        var value = (userName ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return "(empty)";
        }

        var prefix = value.Length >= 2 ? value[..2] : value;
        return $"{prefix}***({value.Length})";
    }
}
