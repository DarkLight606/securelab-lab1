using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

public sealed class SecureLabApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.AddProvider(new CapturingLoggerProvider(LogEntries)));
    }

    // ЛР 04: записи журналу API під час тестів (для перевірки audit-події A-09).
    public ConcurrentQueue<CapturedLogEntry> LogEntries { get; } = new();

    // ЛР 04: прибирає інцидент, створений тестом, разом із дочірніми рядками (cascade). Seed не чіпає.
    public async Task RemoveIncidentAsync(Guid id)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(incident => incident.Id == id).ExecuteDeleteAsync();
    }

    // Повертає локальну навчальну БД до відомого seed (baseline ЛР 1 плюс два Low-інциденти
    // scaffold ЛР 02). Використовує наявний перевірений reset із DatabaseBootstrap, тому нового
    // місця з raw SQL не додає. Потрібен тестам, що рахують severity: їх результат не має
    // залежати від інцидентів, які створюють тести контракту.
    public Task ResetDatabaseAsync()
    {
        var configuration = Services.GetRequiredService<IConfiguration>();
        var environment = Services.GetRequiredService<IHostEnvironment>();
        return DatabaseBootstrap.InitializeAsync(Services, configuration, environment, resetRequested: true);
    }
}

public sealed record CapturedLogEntry(string Category, EventId EventId, string Message);

internal sealed class CapturingLoggerProvider(ConcurrentQueue<CapturedLogEntry> sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, sink);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLogEntry> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            sink.Enqueue(new CapturedLogEntry(category, eventId, formatter(state, exception)));
    }
}
