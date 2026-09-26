# Карта архітектури

SecureLab — навчальний трекер інцидентів (варіант 2-A). Це один серверний
проєкт ASP.NET Core, який віддає і браузерний клієнт, і JSON API. Дані
лежать у PostgreSQL, що працює в Docker Compose.

## Компоненти

| Компонент | Розташування | Відповідальність |
|---|---|---|
| Browser client | `src/SecureLab.Api/Client/` | Формує HTTP-запити через `fetch`, показує відповідь через `textContent` і створення DOM-вузлів |
| ASP.NET Core API, Presentation | `src/SecureLab.Api/Presentation/` | Endpoints: читає параметри запиту, перевіряє їх, повертає HTTP-відповідь; response DTO |
| ASP.NET Core API, Application | `src/SecureLab.Api/Application/` | `IncidentQueries`: будує запити читання і повертає DTO |
| EF Core (Npgsql) | `src/SecureLab.Api/Data/` | `SecureLabDbContext`, entities, migration, seed і reset |
| PostgreSQL у Docker Compose | `infra/compose.yaml` | Контейнер `postgres`, порт `127.0.0.1:54329`, іменований volume з даними |

## Маршрут ЛР 1: підсумок за severity

```text
кнопка «Показати підсумок» у Client/index.html (#summary-button)
  → loadSeveritySummary у Client/app.js
  → GET /api/incidents/severity-summary[?status=...]
  → IncidentEndpoints.GetSeveritySummaryAsync (перевірка status за allowlist)
  → IncidentQueries.GetSeveritySummaryAsync (AsNoTracking, GroupBy, Count, ToListAsync)
  → SecureLabDbContext.Incidents
  → PostgreSQL: таблиця incidents (SELECT severity, count(*) ... GROUP BY severity)
  → сортування за критичністю в коді
  → IReadOnlyList<IncidentSeveritySummaryResponse> як JSON
  → textContent у списку #summary-list
```

Досліджений у етапі 2 готовий маршрут деталей:

```text
клік на картці інциденту → loadIncidentDetails (Client/app.js)
  → GET /api/incidents/{id}
  → IncidentEndpoints.GetDetailsAsync (маршрутне обмеження :guid; null → 404)
  → IncidentQueries.GetDetailsAsync (AsNoTracking, Where за id, проєкція, SingleOrDefaultAsync)
  → SecureLabDbContext.Incidents → таблиця incidents (+ study_users, incident_comments)
  → IncidentDetailsResponse як JSON
  → renderIncidentDetails: textContent і document.createTextNode
```

## Ключові файли

| Рівень | Файл | Що там шукати |
|---|---|---|
| Клієнт | `src/SecureLab.Api/Client/index.html` | кнопка, вибір статусу, статус і список підсумку |
| Клієнт | `src/SecureLab.Api/Client/app.js` | `loadSeveritySummary`, три стани UI, `apiFetch` |
| Endpoint | `src/SecureLab.Api/Presentation/Endpoints/IncidentEndpoints.cs` | `MapGet("/severity-summary", ...)`, `SummaryStatusAllowlist` |
| Application | `src/SecureLab.Api/Application/Incidents/IncidentQueries.cs` | `GetSeveritySummaryAsync`, `SeveritySummaryOrder`, лог |
| DTO | `src/SecureLab.Api/Presentation/Contracts/IncidentResponses.cs` | `IncidentSeveritySummaryResponse(Severity, Count)` |
| DbContext | `src/SecureLab.Api/Data/SecureLabDbContext.cs` | `DbSet<Incident> Incidents`, `ToTable("incidents")`, `HasConversion<string>()` |
| Таблиця | `incidents` у PostgreSQL | колонки `severity` і `status` зберігаються текстом |
| Перевірка | `tests/SecureLab.Api.Tests/SeveritySummaryEndpointTests.cs`, `tests/http/incidents.http` | інтеграційні тести і ручні HTTP-сценарії |

## Контракт severity-summary

| Властивість | Рішення |
|---|---|
| Запит | `GET /api/incidents/severity-summary`, body немає |
| Параметр | `status` необов'язковий; allowlist `New`, `Triaged`, `InProgress`, `Resolved`, `Closed` без урахування регістру; інше значення (зокрема число `1` або порожній рядок) дає `400 Validation Problem Details` |
| Відповідь | `200 OK`, масив `{ "severity": string, "count": int }`, інших полів немає |
| Нульові групи | лише наявні групи: рівень без інцидентів у відповідь не потрапляє; якщо інцидентів немає, відповідь `[]` |
| Порядок | за критичністю: `Critical`, `High`, `Medium`, `Low`; задано масивом у коді після матеріалізації, бо SQL сортував би текст за абеткою (`High`, `Low`, `Medium`) |
| Baseline seed | `High: 1`, `Medium: 1`, `Low: 1`, без `Critical` |

## Межі довіри

| Межа | Дані, що її перетинають | Що не можна припускати | Контроль у маршруті |
|---|---|---|---|
| браузер → API | method, URL, path id, query `status`, headers | що клієнт надішле лише значення з `<select>` або ID зі списку | `:guid` для id; allowlist `status` у `IncidentEndpoints` і `400`; `404` для відсутнього ресурсу |
| API → PostgreSQL | id і status як умови запиту | що рядок із запиту можна вклеїти в SQL | EF Core передає значення параметрами (`@id`, `@status`); `AsNoTracking()` для читання; allowlist ще до запиту |
| API → браузер | JSON response | що право прочитати entity дає право отримати всі її поля | окремі DTO: `IncidentDetailsResponse` без `OwnerUserId` і email; `IncidentSeveritySummaryResponse` лише з severity і count |
| дані response → DOM | текст назв, описів, коментарів, severity | що текст із БД безпечний як HTML | `textContent`, `document.createTextNode`, створення вузлів; у `app.js` немає небезпечного HTML-приймача (перевіряє тест) |
| конфігурація → API | connection string, змінні середовища | що локальні навчальні облікові дані придатні для іншого середовища | значення поза Git через `ConnectionStrings__SecureLab`; `.env` ігнорується Git; у журналах параметри SQL приховані (`'?'`) |

## Конфігураційні входи

Значення тут навмисно не наведено.

| Вхід | Що задає |
|---|---|
| `global.json` | версія .NET SDK (смуга 10.0.3xx, не нижче 10.0.302, `latestPatch`) |
| `src/SecureLab.Api/appsettings.json` | рівні журналювання; міграції і reset вимкнені поза Development |
| `src/SecureLab.Api/appsettings.Development.json` | локальний connection string навчального стенда; дозвіл міграцій і reset |
| `src/SecureLab.Api/Properties/launchSettings.json` | URL `http://localhost:5080` і `ASPNETCORE_ENVIRONMENT=Development` для `dotnet run` |
| `infra/compose.yaml` | образ PostgreSQL, назва БД, користувач, порт, volume, healthcheck |
| `infra/.env.example` (або локальний `infra/.env`, який не комітиться) | `POSTGRES_PORT` для Docker Compose |
| змінна середовища `ConnectionStrings__SecureLab` | перевизначає connection string без зміни файлів у Git |

## Повернення до відомого стану

1. Зупинити API (`Ctrl+C`), щоб з БД не працювали два процеси.
2. Виконати `dotnet run --no-build --project src/SecureLab.Api -- --reset-database`
   (без `--no-build`, якщо код змінювався після збірки).
3. Команда працює лише в Development з `Database:AllowReset=true`: застосовує
   migration, очищує таблиці `incident_comments`, `incident_status_history`,
   `incidents`, `study_users` і знову заповнює seed.
4. Знову запустити API і перевірити `GET /api/incidents` та
   `GET /api/incidents/severity-summary`.

Стенд зупиняється командою `docker compose --env-file infra/.env.example -f infra/compose.yaml down` без `-v`, тож дані у volume лишаються.

## Журналювання нового шляху

`IncidentQueries.GetSeveritySummaryAsync` пише один структурований запис:
шаблон `Built severity summary with {GroupCount} groups for status filter {Status}`
і два параметри, кількість груп і фільтр. Описи інцидентів, connection string,
cookies і токени до журналу не потрапляють.

## ЛР 2: контракт створення і безпечний пошук (варіант 2-A)

Гілка `lab/2-input-sqli` від тегу `v0.1.0`; scaffold `lab-02-start-v1`. Додано два
маршрути в `src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs`.

### Маршрут створення інциденту

```text
POST /api/incidents з JSON-тілом
  → binding у CreateIncidentRequest (тільки title, description, severity, occurredAtUtc)
  → серверна валідація: required, довжина title<=160 і description<=4000, severity через
    Enum.TryParse + Enum.IsDefined, occurredAtUtc не далі ніж +5 хв, cross-field High/Critical → опис>=40
  → помилки збираються у Dictionary і повертаються як 400 application/problem+json (до доступу до БД)
  → перевірка предметного конфлікту: активний інцидент з тим самим title (Trim, з регістром) → 409
  → створення entity: Id, OwnerUserId (demo Alice), Status=New, часи задає сервер
  → SaveChangesAsync (EF Core параметризує INSERT)
  → 201 Created + CreatedIncidentResponse (без OwnerUserId і updatedAtUtc)
```

### Маршрут пошуку після виправлення

```text
GET /api/incidents/search?q=...&sortBy=...
  → sortBy перевіряється за allowlist (createdAtUtc | severity | status); інше → 400 з ключем sortBy
  → q екранується (\, %, _) і передається у EF.Functions.ILike як параметр @pattern ESCAPE '\'
  → сортування задає серверний CASE-вираз (Critical..Low, New..Closed), а не колонка з клієнта
  → Take(50) → SearchIncidentResponse[] як JSON
```

### Оновлені межі довіри для ЛР 2

| Межа | Дані | Контроль після ЛР 2 |
|---|---|---|
| браузер → API (create) | title, description, severity, occurredAtUtc | окремий request DTO без server-managed полів; серверна валідація формату, діапазону і cross-field; 400 problem+json |
| браузер → API (search) | q, sortBy | q як параметр ILIKE; sortBy лише з allowlist, інакше 400; ім'я колонки з клієнта не приймається |
| API → PostgreSQL | значення пошуку і сортування | значення передаються параметром; структуру (ORDER BY) вибирає код; жодної конкатенації недовіреного тексту в SQL |

### A-01: огляд точок доступу до даних (fixed commit)

| Файл / метод | Категорія | Висновок |
|---|---|---|
| `Scaffolding/Lab02Endpoints.cs` — search | LINQ + параметр | виправлено: `EF.Functions.ILike` з параметром і allowlist сортування |
| `Scaffolding/Lab02Endpoints.cs` — create | LINQ | `AnyAsync` і `Add/SaveChanges`; EF параметризує; safe |
| `Application/Incidents/IncidentQueries.cs` | LINQ | список, summary, деталі — тільки LINQ; параметри `@status`, `@id`; safe |
| `Data/DatabaseBootstrap.cs` — `ExecuteSqlRawAsync` | trusted static SQL | `TRUNCATE` з фіксованими іменами таблиць, без вводу; лише в Development; safe |
| `Data/DatabaseBootstrap.cs` — `MigrateAsync` | EF migration | згенерований EF, без вводу; safe |

Після виправлення `FromSqlRaw` у `src/` немає.
