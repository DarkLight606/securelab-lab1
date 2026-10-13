using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace SecureLab.Api.Application.Auth;

// Налаштування обмеження спроб входу (розділ Auth:LoginAttemptLimit у appsettings).
public sealed class LoginAttemptLimitOptions
{
    public const string SectionName = "Auth:LoginAttemptLimit";

    // Скільки невдалих спроб дозволено на один ключ за вікно.
    public int PermitLimit { get; set; } = 5;

    // Фіксоване вікно; після його завершення лічильник ключа обнуляється.
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(60);
}

// Лічильник невдалих спроб входу на основі стандартного FixedWindowRateLimiter.
// Рахуються лише невдалі спроби, тому успішні входи інших тестів чи користувачів ліміт не вичерпують.
// Ключ розділу: IP-адреса клієнта + нормалізоване ім'я користувача. Пароль у ключ не входить.
public sealed class LoginAttemptLimiter(IOptions<LoginAttemptLimitOptions> options) : IDisposable
{
    private readonly LoginAttemptLimitOptions _options = options.Value;

    private readonly PartitionedRateLimiter<string> _limiter =
        PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.Value.PermitLimit,
                Window = options.Value.Window,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    public TimeSpan Window => _options.Window;

    public static string PartitionKey(string? remoteIpAddress, string userName)
        => (remoteIpAddress ?? "unknown") + "|" + userName.Trim().ToUpperInvariant();

    // permitCount = 0 лише перевіряє, чи лишилися дозволи, і нічого не списує.
    public bool IsExhausted(string partitionKey)
    {
        using var lease = _limiter.AttemptAcquire(partitionKey, permitCount: 0);
        return !lease.IsAcquired;
    }

    // Списує один дозвіл після невдалої спроби входу.
    public void RecordFailure(string partitionKey)
    {
        using var lease = _limiter.AttemptAcquire(partitionKey, permitCount: 1);
    }

    public void Dispose() => _limiter.Dispose();
}
