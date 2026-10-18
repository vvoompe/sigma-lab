# Передача контракту серій від Z2 до Z3 — #6 / #8

18.10.2026. Вихідні версії: Z1 у main `ce219f2`, гілка Z3
`feature/8-user-credentials` / `172ccbb`. Це конкретна пропозиція Z2 для review
власником #8, а не заява про вже реалізовані endpoint-и чи погодження всією командою.
ЧП1 вимагає каркас API; повний наскрізний сценарій Experiment належить ЧП2.
Кейси 7, 10, 11–20 не стають виконаними від появи цього документа.

## Розподіл реалізації

| Учасник | Що робить |
|---|---|
| Z2 / #6 | DTO, єдиний `IExperimentService`/`ExperimentService`, контролер, пагінація, виклик математичного ядра, HTTP-помилки й тести, OpenAPI. |
| Z3 / #8 | `ICurrentUser`, правила ролей та доступу до серій/приладів/майданчиків, підготовка запису аудиту підпису. Не створює паралельний ExperimentService або контролер. |
| Z1 | Доменні методи, модель зв'язку менеджера з майданчиком, конфігурації/міграції; review змін `LabDbContext`. |
| Z4 | Sync та ідемпотентність ClientId. Тут не реалізуються. |

Власність визначається через перевірений `ICurrentUser.Id`, не через OwnerId з HTTP.
JWT, credentials, ролі й таблиці аудиту не переписуємо в Z2. `User` лишається профілем.
Зміни сервісного інтерфейсу та `LabDbContext` вносимо узгоджено: додати потрібні
`Experiments`, `Instruments`, `Measurements`, `ReferenceValues` до наявного
`IAppDbContext`, зберігши Users/UserCredentials/AuditEntries із гілки Z3.
Контролер не залежить від Infrastructure.

## HTTP/DTO — пропозиція для наступного PR #6

| Запит | Вхід | Успіх |
|---|---|---|
| `GET /api/experiments` | `page=1`, `pageSize=20`, необов'язкові `siteId`, `status`; page≥1, pageSize≥1, обмеження до 100 | 200 `PagedResult<ExperimentDto>` |
| `GET /api/experiments/{id}` | Id | 200 `ExperimentDto` |
| `POST /api/experiments` | `CreateExperimentRequest`: name, siteId, instrumentId, необов'язковий referenceValueId | 201 + Location + `ExperimentDto` |
| `PUT /api/experiments/{id}` | `RenameExperimentRequest`: name, expectedVersion | 200 `ExperimentDto` з поточною Version |
| `DELETE /api/experiments/{id}?expectedVersion=...` | Очікувана додатна Version, лише чернетка | 204 |
| `POST /api/experiments/{id}/measurements` | `AddMeasurementRequest`: clientId, value, measuredAt, expectedVersion | 201 + Location + `MeasurementMutationDto` |
| `GET /api/experiments/{id}/measurements/{measurementId}` | Id серії й виміру; цей шлях є Location створеного виміру | 200 `MeasurementDto` |
| `PUT /api/experiments/{id}/measurements/{measurementId}` | `UpdateMeasurementRequest`: value, measuredAt, expectedVersion серії | 200 `MeasurementMutationDto` |
| `DELETE /api/experiments/{id}/measurements/{measurementId}?expectedVersion=...` | Очікувана Version серії | 200 `{ experimentVersion }` |
| `POST /api/experiments/{id}/sign` | `SignExperimentRequest`: expectedVersion; без signedById/signedAt | 200 `ExperimentDto`; кейс 19 |
| `GET /api/experiments/{id}/statistics?confidenceLevel=0.95` | Рівень довіри (0,1), доступ до серії | 200 `ExperimentStatisticsDto` |

Усі поля вхідних DTO, крім явно необов'язкових, мають бути присутніми й не null.
Не підміняти відсутні value/expectedVersion/дату значеннями 0/default.
Name після Trim — 1–100 символів за DomainRules; Id/Version додатні, ClientId не порожній,
value скінченний, measuredAt — UTC з явним зміщенням, приведеним до UTC.
Одиниця не приймається як довірене поле: сервіс бере `Experiment.Instrument.Unit`.
Публічне додавання виміру не є endpoint-ом синхронізації: збіг UUID повертає 409 без
чужих даних, а не `duplicate=true` без перевірок власника/серії/payload.

`ExperimentDto`: id, name, siteId, instrumentId, referenceValueId?, status,
createdAt, signedAt?, signedById?, version, unit. OwnerId задає сервіс; змінювати
власника, прилад, майданчик чи еталон через Rename не дозволяється.
Status у JSON — стабільні рядки `Draft`/`SignedOff`, не довільні числові enum.
`MeasurementDto`: id, experimentId, clientId, value, measuredAt, version, unit.
`MeasurementMutationDto`: measurement (`MeasurementDto`), experimentVersion.
`ExperimentStatisticsDto`: experimentId, experimentVersion, count, unit, mean,
sampleStandardDeviation, standardError, confidenceInterval {lower, upper, confidenceLevel}.
Мінімум два виміри; недостатня вибірка або задокументована чисельна межа ядра —
400 ProblemDetails із зрозумілим кодом, не NaN/Infinity і не 500.
Результати не кешуються у БД; регресію/еталон додаємо окремим явним контрактом,
не вигадуємо X-координату з HTTP чи часу виміру.

## Контракт виклику правил Z3

Z2 передає Z3 вже завантажений ресурс (Id, OwnerId, SiteId, Status) та операцію:
Read / Rename / Delete / AddMeasurement / UpdateMeasurement / RemoveMeasurement / Sign.
Z3 читає поточного користувача через ICurrentUser і повертає рішення Allow/Deny;
не змінює Experiment, не виконує SaveChanges і не записує HTTP-відповідь.
Назви конкретного інтерфейсу/enum погоджуються в PR перед кодом, щоб не дублювати типи.

Для списку Z3 надає область доступних серій як SQL-трансльований LINQ-предикат/запит.
Z2 застосовує цю область **до** Count/Skip/Take; не завантажує чужі записи в пам'ять
і не включає їх до totalCount. Те саме правило діє для статистики й читання виміру.

| Випадок | Відповідь Z2 |
|---|---|
| Відсутній/невалідний токен | 401 від шару автентифікації Z3 |
| Читання відсутнього або недоступного ресурсу | 404 без пояснення про чужого власника |
| Створення серії з недоступним/чужим приладом | 404, як у кейсі 10; перевірка до конструктора Experiment |
| Мутація відсутнього ресурсу | 404 |
| Мутація наявного ресурсу без дозволу, зокрема підпис іншою роллю | 403 |
| Дозволена дія зі старою Version, над SignedOff або з конфліктним UUID | 409 |
| Некоректні DTO, одиниця/діапазон/дата/вибірка | 400 |

Після базової перевірки DTO сервіс спочатку перевіряє доступ, потім стан і Version:
заборонений клієнт не отримує 409, що розкриває статус/версію чужої серії.
Усі прикладні помилки — ProblemDetails/ValidationProblemDetails без SQL і стеку.

## Відкрите рішення Z3 + Z1: «свій майданчик» LabManager

Специфікація дозволяє Researcher вести власні серії, LabManager — читати й підписувати
серії свого майданчика, Administrator — керувати користувачами/майданчиками/приладами.
Але поточний `Site.OwnerId` позначає **дослідника, який створив Site**;
це не модель призначення керівника. Самої ролі LabManager недостатньо для доступу
до всіх серій, а LegacyOwnerId не є доказом володіння.

Пропозиція Z2: окремий явний зв'язок User–Site для призначень керівників (наприклад,
SiteManager), без зміни сенсу OwnerId. Z3 погоджує правило, Z1 — кардинальність,
FK та міграції. Без підтвердженого призначення підпис/читання чужої серії за роллю
LabManager має бути заборонений. Не додаємо глобальний admin bypass для серій,
якого немає у вимогах. Це питання для конкретного рішення команди, не прихована
реалізація або твердження, що такий зв'язок уже існує.

## Підпис, конкуренція та аудит: одна операція збереження

1. Z2 завантажує tracked Experiment та потрібний граф. Для мутацій не використовує
   AsNoTracking; для одиниці — Instrument; для окремого Measurement — його Experiment
   та Instrument; для операцій колекції — Measurements. Перевіряє належність виміру серії.
2. Викликає правило Z3. Після Allow порівнює expectedVersion із завантаженою Version.
   Неспівпадіння — 409; інакше викликає доменний метод. Не змінює приватні setter,
   Entry.Property чи ExecuteUpdate/Delete для обходу інваріантів.
3. Для Sign тільки сервер визначає actorId та DateTime.UtcNow і передає їх у
   `Experiment.Sign`. Z3 готує `AuditEntry(actorId, "experiment.signed", "Experiment",
   experiment.Id, signedAt)`; Z2 додає його в той самий IAppDbContext. Ніякого окремого
   SaveChanges у writer аудиту. Видалення чернетки аналогічно має аудит
   `experiment.deleted`, якщо контракт видалення вводиться в цьому інкременті.
4. Один SaveChanges зберігає стан і аудит атомарно. EF перевіряє завантажену
   оригінальну Version; DbUpdateConcurrencyException стає 409, запис аудиту теж
   відкочується. Після помилки контекст не використовується для повторного save.
   Сліпого retry або перезапису даних немає. Редагування різних вимірів тієї самої
   серії також конфліктує за Version серії.
5. У відповіді — нова Version, інакше клієнт не може коректно виконати наступну дію.

## Що перевірити перед merge наступного #6/#8

- Реальні HTTP-тести обох користувачів: приховане читання, заборонена мутація,
  чужий прилад, фільтрація totalCount, missing/null DTO, OpenAPI.
- Менеджер із підтвердженим призначенням підписує свою серію майданчика; інша роль
  і менеджер іншого майданчика отримують 403. Без призначення — deny.
- SignedOff відхиляє Rename/додавання/редагування/видалення вимірів/видалення серії
  як 409 для авторизованого на цю дію користувача.
- Два контексти редагують різні виміри: перший save успішний, другий — 409;
  окремо гонка підпису з редагуванням. Застаріла Version від клієнта також дає 409.
- Успішний підпис має рівно один аудит з серверним actor/time; конфлікт або відмова
  не залишає нового аудиту. Тести на SQLite та PostgreSQL.
- Статистика використовує unit із серії, n−1 і Student t; чисті math-тести не
  заміняють перевірку HTTP-інтеграції. UUID-конфлікт не розкриває чужого payload.

## Текст для агента Z3

Прочитай AGENTS.md, CONTRIBUTING.md, #8 та цей контракт Z2. Працюй у
feature/8-user-credentials з урахуванням PR Z2, спрямованого в цю гілку.
ExperimentService/контролер/DTO не дублюй: їх робить Z2 у #6. Підготуй правила
доступу та аудит, які викликає один сервіс Z2; аудит і підпис мають один SaveChanges.
Спершу дай конкретне рішення щодо прив'язки LabManager до Site й погодь його із Z1;
OwnerId/LegacyOwnerId або сама роль не підміняють призначення. Перевір 404/403/409
за таблицею, не позначай кейси серій готовими без HTTP-тестів. Credentials/міграції
передай Z1 на окреме review. Попередні зауваження PR #27 до конфігурації ключа й
доступності OpenAPI опрацюй у його гілці та перенеси залежність звичайним merge.
