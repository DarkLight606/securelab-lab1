# Матриця доступу ЛР 04 (Incident, baseline 2-A)

Рівень виконання: відмінний. Ролі беруться лише з role claim, який сервер записав у cookie під час login.
Ідентифікатор користувача: claim `NameIdentifier` перевіреного principal, ніколи не body, URL, query чи header.

## Політика відмови

- Анонім на будь-якому маршруті `/api/incidents*` і `/api/incident-comments/*`: `401` без HTML redirect.
- Інцидент, який користувач не має права бачити, і неіснуючий інцидент: однаковий `404` Problem Details
  («Інцидент не знайдено», detail без id). Відповіді відрізняються лише `traceId` (A-10).
  Причина: Reporter не має права навіть знати, що чужий запис існує. `403` для чужого id сказав би «запис є, але не ваш».
- Інцидент видимий, але дія для цієї ролі чи стану заборонена: `403` («Дію заборонено»). Тут існування запису
  вже відоме користувачеві, тож `403` нічого нового не розкриває.
- Недопустимий перехід статусу для ролі, якій перехід дозволено: `409`.
- Коментар, батьківський інцидент якого невидимий, або internal comment для Reporter: `404` («Коментар не знайдено»).

## Таблиця

| Дія | Рівень | Анонім | Reporter | Analyst | Administrator | Власник ресурсу має значення? | Стан реалізації |
|---|---|---:|---:|---:|---:|---:|---|
| Перегляд списку `GET /api/incidents` | добрий | deny, 401 | лише власні | усі інциденти | лише власні (окремих прав у 2-A немає) | Так для Reporter і Administrator | `VisibleTo` у `IncidentQueries.GetListAsync`; A-05 `ScopedReadPathTests.List_AsReporter_ContainsOnlyOwnIncidents` |
| Пошук `GET /api/incidents/search` | відмінний (A-11) | deny, 401 | лише власні | усі інциденти | лише власні | Так для Reporter і Administrator | `VisibleTo` до сортування і `Take(50)` у `Lab02Endpoints`; A-11 `Search_AsReporter_DoesNotReturnForeignMatch` |
| Зведення `GET /api/incidents/severity-summary` | відмінний (A-11) | deny, 401 | лише власні | усі інциденти | лише власні | Так для Reporter і Administrator | `VisibleTo` до `GroupBy`; A-11 `SeveritySummary_AsReporter_CountsOnlyOwnIncidents` |
| Перегляд деталей `GET /api/incidents/{id}` | достатній, головна action | deny, 401 | owner: allow; non-owner: deny, 404 | allow для аналізу будь-якого інциденту | owner: allow; інакше deny, 404 | Так | `IncidentAuthorizationHandler`, операція `Read`; A-02, A-03, A-03R, A-10 у `IncidentAccessTests` |
| Створення `POST /api/incidents` | успадкований baseline | deny, 401 | allow, власник = principal | allow, власник = principal | allow, власник = principal | Сервер призначає власника | ЛР 3, без змін; `IncidentCreateTests` |
| Update content `PUT /api/incidents/{id}` | добрий | deny, 401 | owner: allow (лише title, description, severity, occurredAtUtc); non-owner: deny, 404 | чужий: deny, 403 (читання не дає права змінювати); власний: allow | owner: allow; інакше deny, 404 | Так | handler, операція `Update`; A-04, A-08a у `IncidentMutationAccessTests` |
| Delete `DELETE /api/incidents/{id}` | добрий | deny, 401 | owner і статус New: allow; owner і статус не New: deny, 403; non-owner: deny, 404 | чужий: deny, 403 | як Reporter | Так | handler, операція `Delete`; A-04 `Delete_AsNonOwner_*`, `Delete_AsOwnerOfTriagedIncident_*` |
| Triage `POST /api/incidents/{id}/triage` (New -> Triaged) | добрий | deny, 401 | видимий інцидент: deny, 403; невидимий: deny, 404 | allow лише зі статусу New; інший статус: 409 | як Reporter | Ні: право дає роль Analyst, контекст = поточний статус New з БД | handler, операція `Triage`, статус перевіряє `IncidentCommands.TriageAsync`; A-06, A-07, A-09 у `IncidentTriageTests` |
| Зміна severity/status через update | добрий | deny, 401 | deny: status не входить до DTO, ігнорується | deny: лише через triage | deny | Ні | `UpdateIncidentRequest` без status/owner/role; A-08a |
| Перегляд коментаря `GET /api/incident-comments/{id}` | добрий | deny, 401 | allow, якщо видимий батьківський інцидент і коментар не internal; інакше deny, 404 | allow після рішення `Read` для батьківського інциденту | як Reporter | Так, через батьківський інцидент | `IncidentQueries.GetCommentAsync`; A-12 у `CommentAccessTests` |
| Перегляд internal comment (direct і в details) | добрий | deny, 401 | deny, 404; у details не показується навіть власнику | allow після рішення `Read` для батьківського інциденту | deny, 404 | Ні для правила internal; доступ до батьківського інциденту перевіряється окремо | операція `ReadInternalComments`; A-12 |
| Export/audit, batch | відмінний, якщо flow існує | не застосовується: таких маршрутів у проєкті немає | | | | | |

Administrator: у картці 2-A немає опублікованих адміністративних операцій над інцидентами,
тому роль не дає жодного права поза власними записами.
