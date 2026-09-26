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
