# Звіт до лабораторної роботи № 1

Виконав: Халітов Денис, група КБ-41. Повний звіт з рисунками здано окремо у .docx.

## 1. Ідентифікація стану

- Варіант: 2-A «Трекер інцидентів».
- Робоча гілка: `lab/1-system`; основна гілка: `main`.
- Фінальний тег: `v0.1.0` (annotated, на merge-commit у `main`).
- Commit hash: тег створено після цього commit, тому hash тут не записано.
  Його дає `git rev-list -n 1 v0.1.0`; значення внесено у звіт .docx.
- Remote: приватний репозиторій GitHub `securelab-lab1`.

## 2. Змінений маршрут

```text
кнопка «Показати підсумок» (Client/index.html)
  → loadSeveritySummary (Client/app.js)
  → GET /api/incidents/severity-summary[?status=...]
  → IncidentEndpoints.GetSeveritySummaryAsync: status перевіряється за allowlist
  → IncidentQueries.GetSeveritySummaryAsync: AsNoTracking → GroupBy(Severity) → Count() → ToListAsync
  → SecureLabDbContext.Incidents → incidents: SELECT severity, count(*) ... GROUP BY severity
  → порядок Critical, High, Medium, Low задається в коді
  → IncidentSeveritySummaryResponse[] як JSON
  → textContent у #summary-list
```

Ключові файли: `IncidentEndpoints.cs`, `IncidentQueries.cs`, `IncidentResponses.cs`,
`Client/index.html`, `Client/app.js`, `Client/styles.css`, `SeveritySummaryEndpointTests.cs`,
`tests/http/incidents.http`, `docs/architecture.md`.

## 3. Виконані зміни

| Commit | Що змінено |
|---|---|
| Document severity summary contract in HTTP scenarios | контракт у коментарі `.http` до зміни коду |
| Add severity summary response contract and query | DTO `IncidentSeveritySummaryResponse(Severity, Count)`; query з `AsNoTracking`, `GroupBy`, `Count`, `ToListAsync`; явний порядок; структурований лог |
| Expose severity summary endpoint with status allowlist | handler 501 замінено на `GetSeveritySummaryAsync`; `Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>()`; allowlist `status` і 400; `.http`-сценарії 200, 200 `[]`, 400 |
| Show severity summary in browser client | кнопка, вибір статусу, стани «Завантаження…», «Даних немає», помилка; вивід через `textContent` |
| Add integration tests for severity summary | 9 тестових випадків: успіх і порядок, лише два поля, порожній результат, три невалідні status, три валідні status |
| Document architecture map and trust boundaries | компоненти, маршрут, 5 меж довіри, конфігураційні входи, reset |

Політика нульових груп: лише наявні групи. Порожня вибірка дає `[]`.
Порядок: за критичністю `Critical`, `High`, `Medium`, `Low`. Його задає масив у
`IncidentQueries`, бо SQL сортував би текстові значення за абеткою.

## 4. Перевірка

Усі значення нижче отримано 27.09.2026 на стенді після `--reset-database`.

| ID | Передумови | Дія | Очікувано | Фактично | Доказ |
|---|---|---|---|---|---|
| T-01 | PostgreSQL healthy, API запущено | `GET /health` | 200, API бачить БД | `200 OK`, `application/json`, `{"status":"ready"}` | `curl -si` |
| T-02 | відновлений seed | `GET /api/incidents?status=Triaged` | 200, список відповідає фільтру | `200`, один інцидент `…0001`, Medium, Triaged | `curl -si` |
| T-03 | відновлений seed | `GET /api/incidents?status=Resolved` | 200 з `[]` | `200`, `[]` | `curl -si` |
| T-04 | відновлений seed | `GET /api/incidents/99999999-9999-9999-9999-999999999999` | 404 Problem Details | `404`, `application/problem+json`, title «Інцидент не знайдено» | `curl -si`; тест `GetDetails_ForUnknownId_ReturnsProblemDetails404` |
| T-05 | відновлений seed | `GET /api/incidents?status=Unknown` | 400 Validation Problem Details | `400`, `application/problem+json`, `errors.status` | `curl -si` |
| T-06 | етап 3, відновлений seed | `GET /api/incidents/severity-summary` | 200; High, Medium, Low по 1; без Critical; порядок за критичністю | `200`, `[{"severity":"High","count":1},{"severity":"Medium","count":1},{"severity":"Low","count":1}]` | `curl -i`; тест `GetSummary_ReturnsExistingGroupsInCriticalityOrder` |
| T-06a | етап 3 | `…/severity-summary?status=Triaged` | 200, лише Medium 1 | `200`, `[{"severity":"Medium","count":1}]` | `curl -si`; тест з `Triaged`, `inprogress`, `New` |
| T-06b | етап 3 | `…/severity-summary?status=Resolved` | 200 `[]` | `200`, `[]` | `curl -si`; тест `GetSummary_ForStatusWithoutIncidents_ReturnsEmptyArray` |
| T-06c | етап 3 | `…/severity-summary?status=Unknown` (а також `1` і рядок з лапками) | 400 Validation Problem Details | `400`, `application/problem+json`, `errors.status` | `curl -si`; тест на 3 значення |
| T-07 | етап 3, API і клієнт запущено | натиснути «Показати підсумок» | UI показує результат; є стани завантаження, порожньо, помилка | `GET /api/incidents/severity-summary`, 200, 52 мс, DOM: `High: 1`, `Medium: 1`, `Low: 1`; з Resolved: `[]` і «Даних немає»; після зупинки API: «Не вдалося отримати підсумок. Спробуйте пізніше.» | запис Playwright і скріншоти; скріншот DevTools Network робить студент |
| T-08 | після експериментів | `--reset-database`, повторити T-02 і T-06 | seed повертає відомий стан | T-02 і T-06 дали ті самі відповіді | вивід reset і повторні `curl -s` |

Автоматизована перевірка: `bash scripts/test.sh` дала
`Passed! Failed: 0, Passed: 13, Skipped: 0, Total: 13` (4 тести baseline і 9 нових випадків).

Перевірка diff на секрети: перед кожним commit переглянуто `git diff --staged`.
Дві автоматичні перевірки не знайшли у staged diff `.env`, журналів, дампів,
build-артефактів, паролів, токенів, cookies чи ключів.

## 5. Security-сценарій

У ЛР 1 навмисно вразливого стану немає, тому стану «до» і PoC немає.
Пов'язана з безпекою частина роботи: параметр `status` приходить від клієнта,
тому сервер пропускає лише значення з allowlist. Числове `1` і рядок
`Resolved' OR '1'='1` дають 400 ще до запиту до БД. EF Core у будь-якому разі
передає значення параметром `@status`. Залишковий ризик: API не має
автентифікації, тож підсумок бачить будь-хто з доступом до `localhost:5080`;
це тема ЛР 3–4.

## 6. Висновок

Endpoint `GET /api/incidents/severity-summary` замість 501 повертає 200 з
підсумком за severity. На baseline seed це High, Medium і Low по одному
інциденту без Critical, у порядку критичності. Необов'язковий `status` фільтрує
підсумок, а значення поза allowlist дає 400. Клієнт показує результат через
`textContent` і має окремі стани завантаження, порожнього результату й помилки.
Усі 13 тестів проходять, після reset сценарії відтворюються.

# Звіт до лабораторної роботи № 2

Виконав: Халітов Денис, група КБ-41. Повний звіт з рисунками здано окремо у .docx.

## 1. Ідентифікація стану

- Варіант: 2-A «Трекер інцидентів». Baseline scaffold: `lab-02-start-v1`.
- Робоча гілка: `lab/2-input-sqli` від тегу `v0.1.0` (base commit `c059195`).
- Vulnerable commit: `58c9e849d2b0b3fb28a9319895b98c39d9482e20` (контракт створення виправлено, пошук ще уразливий).
- Fixed commit: цей commit гілки; його hash дає `git rev-list -n 1 v0.2.0`.
- Фінальний тег: `v0.2.0` (annotated, на fixed commit).

## 2. Перевірка (стовпець «Фактично» з реальних запусків)

Значення отримано на локальному стенді після `--reset-database` (seed: Low=3, Medium=1, High=1).

| ID | Сценарій | Дія | Очікувано | Фактично |
|---|---|---|---|---|
| T-01 | Коректне створення | POST valid DTO | 201, response DTO без зайвих полів | 201; тіло id,title,severity,status,occurredAtUtc,createdAtUtc; без ownerUserId і updatedAtUtc |
| T-02 | Некоректний DTO | POST severity "7" | 400 problem+json | 400; `errors.severity`; тіло без SQL і stack trace |
| T-03 | Предметний конфлікт | повторний POST того самого active title | 409 | перший 201, другий 409 problem+json |
| T-06 | Ресурс не знайдено | GET неіснуючого id | 404 problem+json | 404; title «Інцидент не знайдено» |
| T-09 | Cross-field 2-A | High + опис 39 і 40 символів | 39→400(description), 40→201 | 39→400 `errors.description`; 40→201 |
| S-01 | SQLi до fix | контрольний read-only ввід | розширена вибірка | 5 із 5 записів (нормальний USB=1, zz-no-match=0); апостроф O'Brien дав 500 |
| S-02 | Retest SQLi | той самий ввід після fix | порожня/обмежена вибірка | 0 записів; `q=%` теж 0 (буквальний метасимвол) |
| T-04 | Позитивна регресія | USB, O'Brien, комп'ютерного | коректні записи, без 500 | USB→1, O'Brien→1, комп'ютерного→1, усі 200 |
| T-05 | Невідоме сортування | sortBy=title | 400 з ключем sortBy | 400 problem+json `errors.sortBy` |
| A-02 | Overposting | POST з ownerUserId=Bob, status=Closed | зайві поля ігноруються | створено з owner Alice і status New |

Сортування: `sortBy=severity` → High, Medium, Low, Low, Low; `sortBy=status` → New×3, Triaged, InProgress.
Автоматизована перевірка `bash scripts/test.sh`: Passed 34, Failed 0.

## 3. Security-сценарій (8 пунктів)

1. Контекст і гіпотеза. Локальний endpoint пошуку отримує недовірені q і sortBy; є ризик, що значення увійде в структуру SQL.
2. Стан до. Vulnerable commit `58c9e849d2b0b3fb28a9319895b98c39d9482e20`; метод search у `Lab02Endpoints.cs` склеює q і sortBy у текст `FromSqlRaw`.
3. Мінімальний PoC. Один локальний read-only контрольний ввід у `tests/http/lab-02-search.http`; руйнівних дій немає.
4. Спостереження. Контрольний ввід повернув усі 5 записів замість 0; легітимний апостроф O'Brien дав 500 через розрив рядка.
5. Першопричина. q і sortBy з'єднувалися зі SQL-текстом до виконання, тому значення ставало структурою команди.
6. Виправлення. LINQ і `EF.Functions.ILike` з екрануванням і параметром для q; allowlist createdAtUtc|severity|status для sortBy; серверна валідація контракту створення.
7. Retest і позитивна регресія. Той самий ввід дає порожню вибірку; USB і апостроф працюють як звичайний пошук.
8. Залишковий ризик. Перевірка не замінює автентифікацію, авторизацію, пагінацію, rate limit чи огляд інших endpoint; owner ще з demo-контексту (ЛР 3).

# Звіт до лабораторної роботи № 3

Виконав: Халітов Денис, група КБ-41. Повний звіт з рисунками здано окремо у .docx.

## Відтворення (усі рівні)

- Обраний рівень: «Відмінно» (достатній і добрий рівні виконано повністю, плюс три поглиблення).
- Baseline 2-A «Трекер інцидентів»; індивідуальних варіантів немає.
- Base tag `v0.2.0` = commit `9539debaaaad535a6191c4fcc2bbed30457731ca` (fixed commit ЛР 2).
- Scaffold: release `lab-03-start-v1`, base_commit `9539debaaaad535a6191c4fcc2bbed30457731ca` (`.scaffolds/lab-03.json`), встановлено окремим commit `a930d0f`.
- Гілка `lab/3-authentication` від `v0.2.0`. Тег `v0.3.0` анотований, на останньому commit гілки; hash дає `git rev-list -n 1 v0.3.0`.
- ОС/SDK: macOS 15 (Apple Silicon), .NET SDK 10.0.302, Docker (Colima), PostgreSQL 18 у Compose.
- Команди: `dotnet build SecureLab.sln -c Release`; `docker compose --env-file infra/.env.example -f infra/compose.yaml up -d --wait`; `dotnet run --project src/SecureLab.Api -- --reset-database`; `dotnet test tests/SecureLab.Api.Tests -c Release`. Пароль seed задано лише у терміналі через `SeedUsers__Password`.
- Фактичний результат після останньої зміни: build 0 Error(s); тести `Passed: 61, Failed: 0` (35 успадкованих плюс 26 нових).
- Міграція `Lab03Identity` застосовується під час reset і звичайного запуску; два послідовні reset (`resources/commands/reset-twice.sh`) дали однаковий seed (чотири акаунти зі сталими Guid, інциденти Alice і Bob) і двічі зелений набір тестів.

## Власна реалізація і межі

- DTO boundary: `RegisterRequest(UserName, Email, DisplayName, Password)` і `RegisterResponse(Id, UserName, DisplayName)` у `Presentation/Contracts/AuthContracts.cs`; `CurrentUserResponse(Id, UserName, DisplayName, Roles)` для `/api/me`; `CreateIncidentRequest` без owner, status, id. Невідомі поля JSON ігноруються під час binding і не відновлюються з raw JSON.
- Звідки `/api/me` і create беруть id: лише з `HttpContext.User`, claim `ClaimTypes.NameIdentifier`, який authentication handler сформував із зашифрованої cookie. Заголовок `X-Demo-UserId`, query і body не читаються.
- Cookie options (надані scaffold, перевірено): `HttpOnly=true`, `SameSite=Lax`, `SecurePolicy=SameAsRequest` у Development і `Always` поза ним; challenge дає 401, forbid дає 403 замість redirect. Межа локального HTTP: у `http://localhost:5080` атрибут `secure` відсутній, бо запит іде через HTTP; тест `Login_OverHttps_SetsHttpOnlySecureLaxSessionCookie` робить запит з HTTPS base address і бачить `secure`. Це не доводить наявність TLS у production.
- Фактичні flags (добрий рівень): HTTPS-запит: `path=/; secure; samesite=lax; httponly`; HTTP-запит у Development: `path=/; samesite=lax; httponly`. Без `expires` і `max-age` (сеансова cookie).
- Строк дії: стандартний для Identity, 14 днів зі sliding expiration; окремо не змінювався. Закриття вікна браузера не замінює logout.
- Однакові відмови login: невідоме ім'я і хибний пароль дають `401 application/problem+json` з однаковими `type`, `title`, `status`; різний лише `traceId`. Автотест A-07 порівнює стабільні поля через `ProblemShape`.
- Duplicate-контракт: зайняте ім'я, зайнятий email і повтор тієї самої реєстрації дають однаковий `409 Conflict` Problem Details із заголовком «Обліковий запис із таким ім'ям або адресою вже існує». Порушення політики пароля, недозволені символи імені та хибний email дають `400 Validation Problem Details` з ключем поля.
- Rate limit: ключ розділу `IP клієнта + нормалізоване ім'я користувача`; рахуються лише невдалі спроби; 5 невдач за 60 с дають `429` з `Retry-After: 60`; після вікна вхід відновлюється. Реалізація: `LoginAttemptLimiter` на `PartitionedRateLimiter` із `FixedWindowRateLimiterOptions`, налаштування `Auth:LoginAttemptLimit` в `appsettings.json`.
- Події автентифікації: `login.succeeded`, `login.failed`, `login.rate_limited`, `logout`, `register.succeeded`, `register.rejected`. У полях: тип події, підказка користувача (`al***(5)`), результат, trace id. Виключено: пароль, cookie, hash, security stamp, token, заголовок Authorization, тіло запиту і повне ім'я користувача.
- CSRF навчальний борг: cookie-сеанс плюс `SameSite=Lax` відсікає cross-site POST із звичайних форм і скриптів, але не захищає top-level GET і не є повною CSRF-моделлю (anti-forgery token). Повний flow у ЛР 6.
- Канали перебору облікових записів: `POST /api/auth/register` (409 розкриває, що ім'я або email зайняті; компроміс: користувач має знати, чому реєстрація не вдалася; пом'якшення: однаковий текст для імені та email, rate limit login, події). `POST /api/auth/login` каналу не дає: однаковий 401 і однаковий 429 для будь-якого імені.
- Поглиблення (відмінний рівень): (1) втрата чинності старого сеансу після `UpdateSecurityStampAsync` (тест `SessionRevocationTests`); (2) готовність hasher до посилення параметрів: `SuccessRehashNeeded` і перезапис hash під час `CheckPasswordAsync` (тест `PasswordHasherUpgradeTests`); (3) threat analysis login flow у звіті .docx; (4) знайдений і усунутий дефект: `displayName` зі 121 символу давав 500 (помилка БД), тепер 400 з ключем `displayName` і без створеного акаунта.

## Докази

Усі значення нижче отримано 13.10.2026 на локальному стенді після `--reset-database`.

| ID | Рівень | Передумова/дія | Очікувано | Фактично | Безпечний доказ |
|---|---|---|---|---|---|
| T-01 | достатній | register valid DTO | safe 201 | 201, `Location: /api/me`, тіло `id, userName, displayName` | тест `T01_Register_ValidRequest_Returns201SafeDtoAndUserCanLogin`; curl |
| T-02 | достатній | повтор тієї самої реєстрації | safe rejection | 409 problem+json, без пароля у тілі | тест `T02_Register_Duplicate_Returns409Consistently`; curl |
| T-03 | достатній | `\d identity_users` | password не відкрито | стовпець `PasswordHash text`, стовпця з відкритим паролем немає; login новоствореного користувача проходить через Identity verifier | опис схеми без даних |
| A-01 | достатній | anonymous GET /api/me і POST /api/incidents | 401, no redirect | 401 problem+json, без `Location`, без `WWW-Authenticate` | тести `A01_*` |
| A-02 | достатній | login alice → GET /api/me | principal user | 200, `id` Alice, `userName alice`, `roles [Reporter]`; без email, hash, stamp, token | тест `A02_LoginThenMe_ReturnsVerifiedPrincipalWithSafeFields` |
| A-05 | достатній | Bob створює запис із forged owner Alice | новий owner = Bob | 201; у БД `OwnerUserId` = Bob; details `ownerDisplayName` «Боб Мельник» | тест `A05_CreateWithForgedOwner_KeepsPrincipalOwner`; curl |
| S-01 | достатній | review response/log/diff | без витоків | у відповідях register, me, login немає password, hash, stamp, cookie, token; журнал подій без credentials; diff без секретів | чекліст, `grep` журналу |
| A-03 | добрий | cookie Alice + `X-Demo-UserId` Bob | principal не змінюється | 200 і профіль Alice | тест `A03_MeWithForgedUserHeader_KeepsCookiePrincipal` |
| A-04 | добрий | create без owner у DTO | owner = principal | 201; у БД owner Bob | тест `A04_AuthenticatedCreateWithoutOwner_SetsOwnerFromPrincipal` |
| A-06 | добрий | logout, повторний /api/me | 401 | logout 204 із Set-Cookie «порожнє значення, expires 1970»; далі 401 | тест `A06_Logout_ThenProtectedRequest_Returns401` |
| A-07 | добрий | unknown user / wrong password | однаковий результат | обидва 401 problem+json, однакові type/title/status | тест `A07_UnknownUserAndWrongPassword_SameExternalResult` |
| Browser flow | добрий | 5 спостережень етапу 4 | state/profile/create/logout | до входу «не ввійшли», профілю немає; після login Alice «сеанс активний» і поля Alice; create без owner дає 201; після logout профіль прибрано і /api/me дає 401; localStorage/sessionStorage порожні, Console без credentials | скріншоти, `browser_observations.json` |
| Duplicate | добрий | ім'я, email, повтор | послідовний 409 | три однакові 409 з тим самим title/detail | тест `T02_Register_Duplicate_Returns409Consistently` |
| Cookie flags | добрий | login через HTTPS base address | фіксований профіль | `httponly, secure, samesite=lax, path=/`, без expires; через HTTP без `secure` | тести `CookieProfileTests` |
| Rate limit і події | добрий | 6 невдалих спроб probe | 429, відновлення | спроби 1–5: 401; 6: 429 `Retry-After: 60`; інше ім'я з того самого IP: 401; у тесті з вікном 2 с після паузи знову 401; події без credentials | тести `LoginRateLimitTests`; curl; витяг журналу |
| Поглиблення 1 | відмінний | security stamp оновлено | стара cookie недійсна | до події 200, після `UpdateSecurityStampAsync` 401, новий login 200 | тест `SessionRevocationTests` |
| Поглиблення 2 | відмінний | hasher з 210 000 ітерацій | rehash | `SuccessRehashNeeded`; після `CheckPasswordAsync` збережений hash змінився; старий host далі приймає пароль | тест `PasswordHasherUpgradeTests` |
| Поглиблення 3 | відмінний | threat analysis login flow | активи, entry points, межі, загроза, контрзахід, residual risk | у звіті .docx | розділ «Аналіз загроз» |
| Поглиблення 4 | відмінний | displayName 121 символ | 400, акаунт не створено | до: 500; після: 400 `errors.displayName`, `FindByNameAsync` = null | тест `Register_DisplayNameOverLimit_Returns400AndCreatesNothing`; curl |

Маршрут довіри: `POST /api/auth/login` → `SignInManager.PasswordSignInAsync` (стандартний hasher звіряє пароль із `PasswordHash`) → `Set-Cookie .AspNetCore.Identity.Application` (HttpOnly, SameSite=Lax, Secure за HTTPS) → браузер додає cookie сам → `UseAuthentication()` розшифровує квиток і будує `HttpContext.User` → `UseAuthorization()` перевіряє `.RequireAuthorization()` → endpoint читає `ClaimTypes.NameIdentifier` → `Incident.OwnerUserId` = цей Guid.

Пояснення: пароль перевіряється лише всередині Identity (`PasswordSignInAsync`, `CreateAsync`), hash нікуди не повертається, бо він є артефактом автентифікації; HttpOnly, Secure і SameSite задані в `AddLab03Identity` через `ConfigureApplicationCookie`; локальний HTTP не доводить Secure, бо політика `SameAsRequest` додає атрибут тільки для HTTPS-запиту; клієнтський UserId не входить до ланцюжка довіри, бо його може написати будь-хто, а cookie підписана і зашифрована серверними ключами.

Обґрунтування partition key: лише IP блокує всіх за спільним NAT; лише ім'я дає змогу сторонньому заблокувати чужий вхід; пара IP+ім'я вимагає від атакуючого і різних адрес, і різних імен, а звичайний користувач за тим самим NAT не страждає від чужих невдач. Виключені поля події: пароль, cookie, hash, stamp, token, Authorization, тіло запиту, повне ім'я користувача (у поле імені під час невдалої спроби людина могла ввести пароль).

## Декларація використання ШІ

ШІ не використовувався. Журнал: не застосовується.
