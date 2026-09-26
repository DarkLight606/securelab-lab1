using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

public sealed class SecureLabApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
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
