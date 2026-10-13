using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using SecureLab.Api.Application.Auth;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Scaffolding;

public static partial class Lab03Identity
{
    public static void AddLab03Identity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
        }).AddEntityFrameworkStores<SecureLabDbContext>().AddDefaultTokenProviders();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
    }

    public static void MapLab03Identity(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        auth.MapPost("/register", RegisterAsync)
            .WithName("Register")
            .Produces<RegisterResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        auth.MapPost("/login", LoginAsync)
            .WithName("Login")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // Sign-out виконує Identity: сервер надсилає Set-Cookie з порожнім значенням і датою в минулому.
        auth.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        // Профіль читається лише з перевіреного principal. Заголовок X-Demo-UserId більше ні на що не впливає.
        app.MapGet("/api/me", GetCurrentUserAsync)
            .WithName("GetCurrentUser")
            .RequireAuthorization()
            .Produces<CurrentUserResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        // Приклад лише mechanics: не повертає identity і не реалізує оцінювану operation.
        auth.MapGet("/session-check", () => Results.NoContent()).RequireAuthorization();
    }

    // Межі довжини збігаються з моделлю: DisplayName 120 (SecureLabDbContext), UserName і Email 256 (Identity).
    private const int DisplayNameMinLength = 2;
    private const int DisplayNameMaxLength = 120;
    private const int EmailMaxLength = 256;
    private const int PasswordMaxLength = 128;

    private const string PasswordPolicyMessage =
        "Пароль має містити щонайменше 10 символів, велику й малу латинську літеру, цифру "
        + "та символ, що не є літерою чи цифрою.";

    private const string DuplicateTitle = "Обліковий запис із таким ім'ям або адресою вже існує";

    private static readonly EmailAddressAttribute EmailFormat = new();

    // Ті самі символи, що дозволяє Identity за замовчуванням, плюс власна межа довжини 3..32.
    [GeneratedRegex("^[A-Za-z0-9._@+-]{3,32}$")]
    private static partial Regex UserNamePattern();

    // Етап 2. Реєстрація: contract validation -> UserManager.CreateAsync -> безпечний response DTO.
    private static async Task<IResult> RegisterAsync(
        RegisterRequest input,
        UserManager<ApplicationUser> users,
        ILogger<ApplicationUser> logger,
        HttpContext httpContext)
    {
        var errors = new Dictionary<string, string[]>();

        // Нормалізуємо лише ті поля, для яких це визначає контракт. Пароль не обрізаємо і не логуємо.
        var userName = (input.UserName ?? string.Empty).Trim();
        var email = (input.Email ?? string.Empty).Trim();
        var displayName = (input.DisplayName ?? string.Empty).Trim();
        var password = input.Password ?? string.Empty;

        if (!UserNamePattern().IsMatch(userName))
        {
            errors["userName"] = ["Від 3 до 32 символів: латинські літери, цифри та знаки - . _ @ +."];
        }

        if (email.Length == 0 || email.Length > EmailMaxLength || !EmailFormat.IsValid(email))
        {
            errors["email"] = ["Вкажіть коректну адресу електронної пошти не довшу за 256 символів."];
        }

        if (displayName.Length is < DisplayNameMinLength or > DisplayNameMaxLength)
        {
            errors["displayName"] = ["Ім'я для показу має містити від 2 до 120 символів."];
        }

        if (password.Length == 0 || password.Length > PasswordMaxLength)
        {
            errors["password"] = [PasswordPolicyMessage];
        }

        if (errors.Count > 0)
        {
            AuthenticationEvents.Record(logger, AuthenticationEvents.RegisterRejected,
                AuthenticationEvents.UserHint(userName), "contract", httpContext.TraceIdentifier);
            return Results.ValidationProblem(errors);
        }

        // PasswordHash не обчислюється тут: його створює стандартний hasher усередині CreateAsync.
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = email,
            DisplayName = displayName
        };

        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            return MapRegistrationFailure(result, userName, logger, httpContext);
        }

        var role = await users.AddToRoleAsync(user, DbSeeder.ReporterRole);
        if (!role.Succeeded)
        {
            throw new InvalidOperationException("Default role assignment failed.");
        }

        AuthenticationEvents.Record(logger, AuthenticationEvents.RegisterSucceeded,
            AuthenticationEvents.UserHint(userName), "created", httpContext.TraceIdentifier);

        return Results.Created("/api/me", new RegisterResponse(user.Id, user.UserName!, user.DisplayName));
    }

    // Контракт відмов реєстрації: дублікат -> 409 Problem Details з однаковим заголовком для імені та email;
    // решта очікуваних кодів Identity -> 400 Validation Problem Details з ключем поля.
    // У відповідь не потрапляють пароль, повний запит, hash чи текст винятку.
    private static IResult MapRegistrationFailure(
        IdentityResult result,
        string userName,
        ILogger logger,
        HttpContext httpContext)
    {
        var codes = result.Errors.Select(error => error.Code).ToArray();
        var errors = new Dictionary<string, string[]>();

        if (codes.Any(code => code is "DuplicateUserName" or "DuplicateEmail"))
        {
            AuthenticationEvents.Record(logger, AuthenticationEvents.RegisterRejected,
                AuthenticationEvents.UserHint(userName), "duplicate", httpContext.TraceIdentifier);
            return Results.Problem(
                title: DuplicateTitle,
                detail: "Оберіть інше ім'я користувача або адресу електронної пошти.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (codes.Any(code => code.StartsWith("Password", StringComparison.Ordinal)))
        {
            errors["password"] = [PasswordPolicyMessage];
        }

        if (codes.Contains("InvalidUserName"))
        {
            errors["userName"] = ["Ім'я користувача містить недозволені символи."];
        }

        if (codes.Contains("InvalidEmail"))
        {
            errors["email"] = ["Адреса електронної пошти має некоректний формат."];
        }

        if (errors.Count == 0)
        {
            errors["request"] = ["Реєстрацію не виконано."];
        }

        AuthenticationEvents.Record(logger, AuthenticationEvents.RegisterRejected,
            AuthenticationEvents.UserHint(userName), "policy", httpContext.TraceIdentifier);
        return Results.ValidationProblem(errors);
    }

    // Етап 3. Вхід: однаковий 401 для невідомого імені й хибного пароля; 429 після вичерпання спроб.
    private static async Task<IResult> LoginAsync(
        LoginInput input,
        SignInManager<ApplicationUser> signIn,
        LoginAttemptLimiter limiter,
        ILogger<ApplicationUser> logger,
        HttpContext httpContext)
    {
        var userName = (input.UserName ?? string.Empty).Trim();
        var password = input.Password ?? string.Empty;
        var userHint = AuthenticationEvents.UserHint(userName);

        if (userName.Length is 0 or > EmailMaxLength || password.Length is 0 or > PasswordMaxLength)
        {
            AuthenticationEvents.Record(logger, AuthenticationEvents.LoginFailed, userHint, "contract", httpContext.TraceIdentifier);
            return Results.Unauthorized();
        }

        var partitionKey = LoginAttemptLimiter.PartitionKey(
            httpContext.Connection.RemoteIpAddress?.ToString(), userName);

        if (limiter.IsExhausted(partitionKey))
        {
            AuthenticationEvents.Record(logger, AuthenticationEvents.LoginRateLimited, userHint, "429", httpContext.TraceIdentifier);
            httpContext.Response.Headers.RetryAfter = ((int)limiter.Window.TotalSeconds).ToString();
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        // Пароль перевіряє стандартний Identity hasher; рядки тут не порівнюються.
        var result = await signIn.PasswordSignInAsync(userName, password, isPersistent: false, lockoutOnFailure: false);
        if (result.Succeeded)
        {
            AuthenticationEvents.Record(logger, AuthenticationEvents.LoginSucceeded, userHint, "204", httpContext.TraceIdentifier);
            return Results.NoContent();
        }

        // Невідомий користувач і хибний пароль дають один і той самий зовнішній результат.
        limiter.RecordFailure(partitionKey);
        AuthenticationEvents.Record(logger, AuthenticationEvents.LoginFailed, userHint, "401", httpContext.TraceIdentifier);
        return Results.Unauthorized();
    }

    private static async Task<IResult> LogoutAsync(
        SignInManager<ApplicationUser> signIn,
        ILogger<ApplicationUser> logger,
        HttpContext httpContext)
    {
        var userHint = AuthenticationEvents.UserHint(httpContext.User.Identity?.Name);
        await signIn.SignOutAsync();
        AuthenticationEvents.Record(logger, AuthenticationEvents.Logout, userHint, "204", httpContext.TraceIdentifier);
        return Results.NoContent();
    }

    // Етап 3. Профіль поточного користувача: ідентифікатор і ролі лише з HttpContext.User.
    // Заявлений клієнтом ідентифікатор (header, query, body) не читається взагалі.
    private static async Task<IResult> GetCurrentUserAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> users)
    {
        var claimedId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(claimedId, out var verifiedUserId))
        {
            // Автентифікований principal без identifier claim: помилка конфігурації, не підстава брати id з клієнта.
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
        }

        var user = await users.FindByIdAsync(verifiedUserId.ToString());
        if (user is null)
        {
            // Обліковий запис зник після видачі cookie: сеанс більше не вважається чинним.
            return Results.Unauthorized();
        }

        var roles = httpContext.User.FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var userName = httpContext.User.FindFirstValue(ClaimTypes.Name) ?? user.UserName ?? string.Empty;

        return Results.Ok(new CurrentUserResponse(verifiedUserId, userName, user.DisplayName, roles));
    }
}

public sealed record LoginInput(string? UserName, string? Password);
