# Звіт ЛР04

Студент: Халітов Денис Георгійович, КБ-41. Гілка `lab/4-authorization`, тег `v0.4.0`.

## Контекст і межа виконання
- Рівень: відмінний («Відмінно»). Виконано весь добрий рівень, A-11 для search і severity-summary,
  відокремлений resource-based handler і обґрунтування нижче.
- Власний baseline 2-A, base tag `v0.3.0` = `17787b0`. Виправлень стану ЛР 3 не знадобилось:
  стартові перевірки пройшли (61 тест, login alice/bob/morgan, `/api/me` 200, анонімний POST 401).
- Головна action: details/read `GET /api/incidents/{id}`. Політика відмови: `404` для чужого й неіснуючого інциденту.
- Маршрути мого рівня: list, details, search, severity-summary, create (успадкований), PUT, DELETE,
  `POST /api/incidents/{id}/triage`, `GET /api/incident-comments/{id}`.

## Матриця і server-side рішення
Повна матриця: `docs/access-matrix.md`. Коротко: Reporter бачить і змінює лише власні інциденти;
Analyst читає всі інциденти й internal comment, а змінює стан лише переходом New -> Triaged;
Administrator у 2-A не має окремих прав на інциденти.

Де стоїть рішення:
- `Application/Authorization/IncidentAuthorizationHandler.cs` вирішує для одного інциденту операції
  `Read`, `Update`, `Delete`, `Triage`, `ReadInternalComments`. Вхід: principal із cookie і факти з БД
  (`IncidentAccessFacts`: власник і статус).
- `Application/Authorization/IncidentAccessRules.cs`, метод `VisibleTo`, обмежує запити list, search і
  severity-summary тим самим правилом видимості ще в SQL.
- `UpdateIncidentRequest` містить лише title, description, severity, occurredAtUtc. Owner, status і роль
  клієнт передати не може. Наступний статус triage визначає сервер.
- Уся група `/api/incidents` і маршрут коментаря вимагають автентифікації (`RequireAuthorization`).

## Вразливий стан і PoC
Стан після ЛР 3 (v0.3.0 = 17787b0): `GET /api/incidents/{id}` шукає інцидент лише за id і не вимагає входу.
У цьому commit додано PUT, DELETE, triage і `GET /api/incident-comments/{id}` у найпростішому вигляді:
запис завантажується за id, рішення про право немає. Сценарій: `tests/http/lab-04-access.http`.

Спостереження на локальному стенді після `--reset-database` (cookie і пароль не показано):

| Суб'єкт | Запит | Очікувано за матрицею | Фактично |
|---|---|---|---|
| анонім | GET details Alice | 401 | 200, дані Alice |
| alice | GET details Alice | 200 | 200 |
| alice | GET details Bob (змінено лише id) | 404 | 200, «Невідома спроба входу», власник Боб Мельник |
| alice | PUT інциденту Bob (title) | 404, стан без змін | 200, title Bob став «Змінено Alice» |
| alice | DELETE інциденту Bob | 404, запис лишається | 204, Bob далі отримує 404 на власний інцидент |
| alice | PUT власного з `status: Closed`, `ownerUserId`, `role` | status не змінюється | 200, status став Closed |
| alice (Reporter) | POST triage власного New | 403 | 200, status Triaged |
| alice | GET коментаря Morgan до інциденту Bob | 404 | 200, текст коментаря |
| alice | GET internal comment | 404 | 200, службовий текст |
| alice | list, severity-summary, search `O'Brien` | лише власні | усі 5 записів, High 1 / Medium 1 / Low 3, інцидент Bob …0004 |

Готовий helper `login-and-read.py --user alice --incident bob`: `login_status=204`, `read_status=200`,
`{"id": "20000000-0000-0000-0000-000000000002", "title": "Невідома спроба входу", "ownerDisplayName": "Боб Мельник"}`.
Маршрут details відповідає навіть без cookie: крім IDOR, він ще не вимагає автентифікації.

## Виправлення та retest
Vulnerable commit: `8c45d2d`. Fixed commit (тег `v0.4.0`) записано наступним commit «Record lab 4 hashes».
Ті самі запити після виправлення, `--reset-database` перед кожним прогоном:

| Суб'єкт | Запит | До | Після |
|---|---|---|---|
| анонім | GET details Alice | 200 | 401 |
| alice | GET details Alice | 200 | 200 |
| alice | GET details Bob | 200, дані Bob | 404 «Інцидент не знайдено» |
| alice | GET неіснуючого id | 404 з id у detail | 404, тіло те саме, що для Bob, крім traceId |
| morgan | GET details Alice | 200 | 200, бачить і internal comment |
| alice | PUT інциденту Bob | 200, title змінено | 404, title Bob без змін |
| alice | DELETE інциденту Bob | 204, запис зник | 404, Bob далі читає свій інцидент |
| alice | PUT власного з forged status/owner/role | status Closed | 200, status New, власник Аліса |
| alice | triage власного New | 200 Triaged | 403, status New |
| morgan | triage того самого New | 200 | 200 Triaged |
| alice | коментар до інциденту Bob / internal | 200 / 200 | 404 / 404 |
| alice | list / severity-summary / search O'Brien | 5 записів / High 1, Medium 1, Low 3 / запис Bob | 3 власні / Medium 1, Low 2 / `[]` |

`login-and-read.py --user alice --incident bob` після виправлення: `login_status=204`, `read_status=404`,
`{"title": "Інцидент не знайдено", "status": 404, "traceId": "00-…"}`. Для `--user morgan --incident alice`: `read_status=200`.

Для mutation відмова не змінює даних: тести читають стан власником до і після запиту й порівнюють JSON повністю.
Для read DTO не має `ownerUserId` і email, а відповідь на чужий id не містить жодного поля Bob.

## Доказова таблиця
Якщо головна action — mutation, доказ owner/non-owner для details запишіть у стовпці «Доказ» рядка A-03R.

Усі рівні: автоматичні HTTP A-02/A-03, A-03R — test або `.http`. Добрий додає A-01, A-04–A-07, одне з A-08a/A-08b, A-09, A-12; A-10 лише для 404. A-06 і A-07 автоматичні. Відмінний додає A-11 (search і severity-summary); A-06/A-08a/b успадковано. Запишіть фактичні результати; рядки вищого рівня позначте з поясненням.

| ID | Рівень | Сценарій | Передумови | Дія | Очікувано | Фактично | Доказ |
|---|---|---|---|---|---|---|---|
| A-01 | добрий | Анонімний доступ | немає authentication context | protected request | `401`, не HTML redirect | `401`, без `Location` | `Details_AsAnonymous_Returns401_WithoutRedirect`, `Update_AsAnonymous_Returns401` |
| A-02 | достатній, обов’язковий | Власний ресурс | Alice має власний seed resource | вибрана головна action: details/read або одна mutation | allow | `200`, id = …0001 | `Details_AsOwner_ReturnsOwnIncident` |
| A-03 | достатній, обов’язковий | Чужий ресурс | Alice, Bob resource | та сама головна action з підміненим id | `403` або `404`; для mutation state без змін | `404`, у тілі немає даних Bob | `Details_AsNonOwner_ReturnsNotFound_WithoutForeignData` |
| A-03R | достатній, entry-контракт ЛР 5 | Дозволений аналітичний read | Morgan (`Analyst`), ресурс Alice | `GET /api/incidents/{id}` | allow для аналізу; не надає права довільної mutation | `200` для Morgan; PUT Morgan до інциденту Alice — `403`, стан без змін | `Details_AsAnalyst_ReturnsForeignIncidentForAnalysis`, `Update_AsAnalyst_ReturnsForbidden_AndDoesNotChangeIncident`, `.http` |
| A-04 | добрий | Чужий ресурс, усі змінювальні дії | Alice, Bob resource | підмінити id в update/delete | deny, state без змін | PUT і DELETE — `404`; стан Bob ідентичний до і після | `Update_AsNonOwner_ReturnsNotFound_AndDoesNotChangeIncident`, `Delete_AsNonOwner_ReturnsNotFound_AndIncidentRemains` |
| A-05 | добрий | List | Alice | list | лише дозволена множина | 3 власні записи, Bob відсутній | `List_AsReporter_ContainsOnlyOwnIncidents`, `GetList_AsAlice_ReturnsOwnIncidentsOnly` |
| A-06 | добрий | Вертикальне підвищення привілеїв | звичайний Reporter | privileged transition | deny, status без змін | `403`, status лишився New | `Triage_AsReporterOwner_ReturnsForbidden_AndStatusStaysNew`, `Triage_AsOtherReporter_ReturnsNotFound` |
| A-07 | добрий | Дозволена привілейована дія | роль і контекст за матрицею | transition за матрицею | allow | `200`, New -> Triaged; для InProgress — `409` | `Triage_AsAnalyst_MovesNewToTriaged`, `Triage_AsAnalyst_ForNonNewIncident_ReturnsConflict_AndStatusUnchanged` |
| A-08a | добрий, альтернатива A-08b | Надлишкове присвоєння (overposting): контракт ігнорує невідоме поле | authenticated user; DTO не містить protected field | forged owner/role/status field у regular update | дозволений content може змінитися, але protected state — ні | `200`, title змінено, status New, власник Аліса, Bob інцидент не бачить | `Update_WithForgedOwnerRoleAndStatus_IgnoresProtectedFields` |
| A-08b | добрий, альтернатива A-08a | Overposting: контракт відхиляє зайве або заборонене поле | authenticated user; контракт явно відхиляє поле | той самий forged request | `400` або `403` за контрактом; весь protected state без змін | не виконувалось: обрано A-08a | — |
| A-09 | добрий | Аудиторська подія (audit event) | denied privileged request | знайти authorization.denied у виводі API | безпечна подія без секретів | подія `authorization.denied` з subject, action, resource, result, UTC-часом і traceId відповіді; без пароля, cookie, тексту note | `Triage_Denied_WritesSafeAuditEventWithResponseTraceId`, рядок журналу API нижче |
| A-10 | добрий, лише політика `404` | Нерозрізнюваність `404` | той самий authenticated client; identifier чужого й identifier неіснуючого ресурсу | виконати два однакові за структурою запити, змінивши лише id | однакові status, `Content-Type` і тіло після вилучення полів кореляції запиту (`traceId`, `requestId`) | однакові `404`, `application/problem+json` і тіло після вилучення traceId | `Details_ForForeignAndUnknownId_AreIndistinguishable` |
| A-11 | відмінний | Власні endpoint-и ЛР 1–2 | Alice; власні endpoint-и з ЛР 1–2 | виконати `severity-summary` і `search` | результат обмежений дозволеною множиною | summary Alice: Medium 1, Low 2; search O'Brien для Alice — `[]`, для Bob — 1 запис | `SeveritySummary_AsReporter_CountsOnlyOwnIncidents`, `Search_AsReporter_DoesNotReturnForeignMatch` |
| A-12 | добрий | Child resource та internal comment | Alice (`Reporter`); прямий id коментаря Morgan до інциденту Bob (comment-2 у seed-identifiers.json) | запросити `GET /api/incident-comments/{id}` і перевірити details-проєкцію для `Reporter` | direct child access — deny за зафіксованою політикою; internal comment не потрапляє у відповідь `Reporter` | коментар до інциденту Bob — `404`; internal comment — `404` і відсутній у details Alice; Morgan бачить його | `CommentAccessTests` (4 тести) |

## Основний finding і залишковий ризик (усі рівні)
1. Контекст: ресурс Incident, дія details/read (і далі update, delete, triage, коментар). Суб'єкти: alice, bob (Reporter), morgan (Analyst).
2. Гіпотеза: Alice прочитає інцидент Bob, якщо замінить у запиті лише id.
3. Стан до: `GET /api/incidents/20000000-0000-0000-0000-000000000002` з cookie Alice дав `200` і дані Bob. Vulnerable commit `8c45d2d`.
4. Першопричина: `IncidentQueries.GetDetailsAsync` шукав запис лише за `incident.Id == id` і не порівнював
   власника з principal; маршрут не вимагав навіть входу. Нові PUT, DELETE, triage і коментар мали ту саму форму:
   «пошук за id → дія», без рішення про право. Update DTO приймав `status`.
5. Виправлення: handler `IncidentAuthorizationHandler` ухвалює рішення до відповіді й до будь-якої зміни;
   list, search і summary обмежені `VisibleTo` у SQL; DTO звужено до content-полів; група вимагає входу.
6. Retest: той самий запит дає `404` з тим самим тілом, що й неіснуючий id; змінювальні запити не змінюють стан Bob.
7. Позитивна регресія: Alice читає й змінює власний інцидент, видаляє власний New; Morgan читає інцидент Alice і виконує triage.
8. Залишковий ризик:
   - CSRF для cookie-mutation (PUT, DELETE, triage, POST): до ЛР 6 це задокументований навчальний борг ЛР 3.
   - Batch і export маршрутів у проєкті немає, тому не перевірялись.
   - Одночасний triage двома аналітиками не перевірявся: викладач не давав сценарію, захисту версії запису немає.
   - Роль у cookie живе до наступного login: якщо адміністратор БД забере роль Analyst, стара cookie ще дає права.

Схема рішення:
запит Alice з id → cookie формує перевірений principal → завантаження фактів інциденту (власник, статус) →
handler: правило owner/role/status → дозвіл або `404` (невидимий) / `403` (видимий, дія заборонена) → відповідь;
зміна стану лише після дозволу.

## Browser flow (усі рівні)
Login alice на `auth-lab.html` → головна сторінка → клік по «Підозрілий лист із вкладенням» → у Console
`loadIncidentDetails("20000000-0000-0000-0000-000000000002")`.
До виправлення клієнт показав деталі Bob. Після виправлення: «Помилка: Інцидент не знайдено».
`app.js` пише текст лише через `createTextElement(...)` і `detailsElement.textContent`; `innerHTML` немає
(тест `ClientScript_DoesNotUseDangerousInnerHtmlSink`). Клієнт не приховує нічого сам: відмову повертає сервер.

## Добрий рівень: роль, transition, child, audit (також для відмінного)
- Triage: дозволено лише Analyst і лише зі статусу New. Порядок у `IncidentCommands.TriageAsync`: tracked entity →
  рішення handler → при відмові audit і `403`/`404` → перевірка поточного статусу з БД (`409`, якщо не New) →
  сервер ставить Triaged і пише рядок історії → `SaveChangesAsync`.
- Чому status у JSON не дозвіл: клієнт може вписати будь-яке значення. Сервер бере роль з cookie, а статус з БД.
- Child: коментар Morgan до інциденту Bob для Alice — `404`. Internal comment Alice не бачить ні прямим запитом,
  ні в details власного інциденту; Morgan бачить після рішення `Read` для батьківського інциденту.
- Audit з журналу API (без пароля, cookie, тіла запиту):
  `authorization.denied subject=10000000-0000-0000-0000-000000000001 action=incident.triage resource=incident/20000000-0000-0000-0000-000000000005 result=denied:role at=2026-10-29T09:33:18.3168710+00:00 traceId=00-43e47dfc8822b5867788a7c3e830d0f8-32531847d88bf06d-00`.
  Той самий traceId прийшов клієнтові у відповіді `403`.

## Відмінний рівень (додатково до всього доброго)
**Handler.** Усі рішення для одного інциденту в одному класі `IncidentAuthorizationHandler`. Endpoint-и лише
викликають `IAuthorizationService.AuthorizeAsync` з потрібною операцією. Правило видимості для вибірок
(`VisibleTo`) і операція `Read` у handler спираються на ту саму умову: власник або Analyst.

**Finding A-11.**
1. Контекст: наявні з ЛР 1–2 `GET /api/incidents/severity-summary` і `GET /api/incidents/search`.
2. Гіпотеза: навіть після захисту details Alice дізнається про чужі інциденти з агрегатів і пошуку.
3. Стан до: severity-summary для Alice дав High 1, Medium 1, Low 3, хоча в Alice немає жодного High.
   Так Alice бачить, що в системі є чужий інцидент High. Search `O'Brien` повернув інцидент Bob …0004 з описом.
4. Першопричина: обидва запити будувались від `db.Incidents` без умови за principal; маршрути не вимагали входу.
5. Виправлення: `VisibleTo(user)` перед `GroupBy` у summary і перед сортуванням та `Take(50)` у search.
   Фільтр іде в SQL, рядки в пам'ять не тягнуться. Нових маршрутів не створено.
6. Після: summary для Alice — Medium 1, Low 2; search `O'Brien` для Alice — `[]`, для Bob — 1 запис;
   для Morgan summary рахує всі 5 записів.

**Least privilege.** Reporter має лише те, що потрібно автору: створити, читати, правити свій запис і прибрати
його, поки ніхто не почав аналіз. Analyst читає все, бо без цього не оцінить інцидент, але вміст чужих записів не
змінює і переводить лише New -> Triaged. Administrator у 2-A не має опублікованих операцій, тому не має й прав
поза власними записами.

**Видимість ресурсу.** Для Reporter чужий інцидент і неіснуючий виглядають однаково (`404`, detail без id).
Так перебір id нічого не каже про наявність записів. `403` з'являється лише там, де запис уже видимий
(власник без права triage, Analyst без права змінювати чужий вміст).

**Охоплення.** Перевірено list, details, search, severity-summary, update, delete, triage, коментар і
проєкцію internal comment у details. Непрямий шлях через id коментаря закритий через батьківський інцидент.
Batch і export у проєкті відсутні: «не застосовується». Concurrent transitions: «не застосовується»,
сценарію від викладача не було.

## Перевірки, відтворення і подання (усі рівні)
Середовище: macOS 15, .NET SDK 10.0.302, PostgreSQL 18 у Docker (compose з `infra/`).
Команди з кореня SecureLab:

```
docker compose --env-file infra/.env.example -f infra/compose.yaml up -d --wait
dotnet run --project src/SecureLab.Api -- --reset-database
dotnet test tests/SecureLab.Api.Tests/SecureLab.Api.Tests.csproj --configuration Release
dotnet run --project src/SecureLab.Api --launch-profile http
```

Результат: reset друкує «Локальні навчальні дані очищено та повторно заповнено seed-значеннями.»;
тести `Passed! - Failed: 0, Passed: 88` (61 успадкований і 27 нових).
Після `RequireAuthorization` на групі `/api/incidents` 22 старі анонімні тести впали з 401, як і описано в методичці;
їх переведено на login (Morgan для search і summary, бо Analyst за матрицею бачить усе), а `GetList_ReturnsSeededIncidents`
замінено перевіркою A-05.

Hashes: vulnerable `8c45d2d`; fixed і тег `v0.4.0` записано в наступному commit.

## Використання ШІ (довільна форма; журнал — за бажанням)
Не використовував ШІ.
