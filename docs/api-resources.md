# API приладів, серій і вимірів — Z2, issue #6

Це реалізований контракт #6 поверх моделі Z1 та credentials/audit Z3 з PR #34.
OpenAPI: `GET /openapi/v1.json`. Усі наведені операції потребують чинного JWT.
Приклади використовують умовні ідентифікатори: спочатку створіть власний Site,
а Administrator має створити прилад для вашого User.

## Операції

| Метод і маршрут | Запит | Успіх |
|---|---|---|
| GET `/api/instruments` | `page`, `pageSize`, `name` | 200, сторінка `InstrumentDto` |
| GET `/api/instruments/{id}` | — | 200, `InstrumentDto` |
| POST `/api/instruments` | `CreateInstrumentRequest` | 201, DTO + Location |
| PUT `/api/instruments/{id}` | `{"name":"Нова назва"}` | 200, DTO |
| DELETE `/api/instruments/{id}` | — | 204 |
| GET `/api/experiments` | `page`, `pageSize`, `name`, `siteId`, `status` | 200, сторінка `ExperimentDto` |
| GET `/api/experiments/{id}` | — | 200, `ExperimentDto` |
| POST `/api/experiments` | `CreateExperimentRequest` | 201, DTO + Location |
| PUT `/api/experiments/{id}` | `{"name":"Нова назва","expectedVersion":1}` | 200, DTO з новою Version |
| DELETE `/api/experiments/{id}` | `?expectedVersion=1` | 204, атомарний аудит видалення |
| POST `/api/experiments/{id}/measurements` | `AddMeasurementRequest` | 201, `{measurement, experimentVersion}` + Location |
| GET `/api/experiments/{id}/measurements` | `page`, `pageSize` | 200, сторінка `MeasurementDto` |
| GET `/api/experiments/{id}/measurements/{measurementId}` | — | 200, `MeasurementDto` |
| GET `/api/experiments/{id}/statistics` | `confidenceLevel` (типово 0.95) | 200, `ExperimentStatisticsDto` |

PUT змінює **лише назву**: це весь дозволений контракт редагування цих ресурсів.
Власник, зв'язки, одиниці, діапазон/похибка приладу, статус, дати та Version не
приймаються для присвоєння з HTTP. Їх зміна потребуватиме окремого контракту й
доменних операцій. Прилад, використаний серією, видалити не можна (409).

## Створення й валідація

Administrator створює прилад для наявного користувача:

```json
{"ownerId":1,"name":"Лінійка","serialNumber":"Ruler-001","unit":"m","minValue":0,"maxValue":100,"absoluteError":0.1}
```

Усі сім полів обов'язкові. Назва й серійний номер — до 100 символів, одиниця —
до 32; порожні/пробільні значення відхиляються. Числа скінченні,
`minValue < maxValue`, `absoluteError >= 0`. `SerialNumber` після Trim глобально
унікальний і регістрозалежний: `Ruler-001` та `ruler-001` різні; дубль дає 409.

Researcher створює серію зі своїм майданчиком і своїм приладом:

```json
{"name":"Польова серія","siteId":1,"instrumentId":1,"referenceValueId":1}
```

Перші три поля обов'язкові, еталон необов'язковий. Ідентифікатори додатні;
відсутній/чужий Site або Instrument дає 404. Одиниця еталона має збігатися з
одиницею приладу, інакше 400. Нова серія має `status="Draft"`, `version=1`.
Власник береться з перевіреного поточного користувача, не з JSON.

Додавання виміру:

```json
{"clientId":"6d4c67ee-a1e9-4da7-8708-afaa3b0d4259","value":2.5,"measuredAt":"2026-10-19T10:00:00Z","expectedVersion":1}
```

Усі поля обов'язкові; пропущений `value` не перетворюється на нуль. Явний нуль
дозволений у межах приладу. UUID не порожній, число скінченне та належить включному
діапазону приладу. Час — UTC із суфіксом `Z`; дату без зони або з іншим offset
клієнт має спочатку перевести в UTC. Одиниця завжди береться із завантаженої серії.

## Доступ, конфлікти та атомарність

Прилади змінює тільки Administrator. Він читає всі прилади; інші автентифіковані
користувачі бачать лише свої. Серії створює й змінює Researcher; читання серій,
їхніх вимірів і статистики обмежено власником до підрахунку/пагінації.
Читання чужої серії — 404, спроба її зміни — 403. Administrator не отримує
прихованого обходу власності серій. Доступ LabManager до чужих серій залежить
від майбутнього призначення на Site у Z3/Z1 і тут не відкривається.

Кожна мутація наявної серії потребує додатної `expectedVersion`. Застаріла версія,
підписана серія або конфлікт EF concurrency повертає 409. Зміни навіть різних
вимірів однієї серії конкурують за Version серії. Після 409 клієнт перечитує стан;
сервер не перезаписує його мовчки. Використано tracked агрегат, доменні методи,
завантажений Instrument і повну Measurements перед колекційною операцією.

Видалення чернетки, каскад її вимірів і `AuditEntry` з action `experiment.deleted`
виконуються одним `SaveChangesAsync`. Конкурентний підпис/зміна скасовує також аудит.
Перевірки унікальності перед записом доповнюються обмеженнями БД: конкурентний
дубль або видалення пов'язаного ресурсу перетворюється на 409.

Повторний `ClientId` тут повертає **409**, без чужого запису/payload. Це звичайне
додавання, а не sync endpoint: ідемпотентність із перевіркою власника, серії та
payload і `duplicate=true` залишається окремою задачею синхронізації.

Валідація — 400, відсутній JWT — 401, заборонена дія — 403, недоступний ресурс —
404, конфлікт — 409. Сервісні помилки мають `application/problem+json`;
помилки моделі — ValidationProblemDetails. JWT challenge 401 може не мати тіла.

## Сторінки й статистика

Колекції повертають `{items,totalCount,page,pageSize}`. `page` і `pageSize` додатні,
типово 1 і 20; понад 100 нормалізується до 100. Далека сторінка порожня, навіть
за `Int32.MaxValue`; `totalCount` стосується тільки доступних і відфільтрованих даних.
Порядок приладів — Name/Id, серій і вимірів — Id. `name` шукає підрядок (до 100
символів), `status` приймає `Draft` або `SignedOff`, `siteId` додатний.
Для вимірів `name` не підтримується і повертає 400.

Статистика потребує щонайменше двох вимірів. Відповідь містить `experimentId`,
`experimentVersion`, `count`, `unit`, `mean`, `sampleStandardDeviation` (дільник
`n-1`), `standardError`, окрему `instrumentAbsoluteError`, довірчий інтервал
Student t (`lower`, `upper`, `confidenceLevel`), `outlierMeasurementIds` та
необов'язковий `referenceComparison` (`absoluteDeviation`, `absoluteTolerance`,
`withinTolerance`). Рівень довіри скінченний і строго між 0 та 1.

Версія, прилад, еталон і виміри читаються одним запитом. 3σ лише позначає викиди,
не вилучає їх із вибірки. Порівнюється середнє; абсолютний допуск включний.
Результат, який не можна подати скінченним double, дає 400. Одиниця з довільного
query/body не впливає на розрахунок. Регресійне ядро Z1 не змінено; HTTP-регресія,
підпис серії, редагування/видалення окремого виміру та sync не додані в #6.

## Еталони

| Метод | Шлях | Доступ |
|---|---|---|
| GET | `/api/reference-values` | будь-який автентифікований |
| GET | `/api/reference-values/{id}` | будь-який автентифікований |
| POST | `/api/reference-values` | лише `Administrator` |
| PUT | `/api/reference-values/{id}` | лише `Administrator` |
| DELETE | `/api/reference-values/{id}` | лише `Administrator` |

Поля: `name` (до 100), `unit` (до 32), `value` (скінченне число), `absoluteTolerance`
(скінченне, `>= 0`). Схема БД не змінювалась - таблиця `reference_values` створена
міграцією `DomainModel`, а серії посилаються на неї зовнішнім ключем.

`DELETE` використаного еталона відхиляється: `FK experiment.ReferenceValueId` має
політику `Restrict`, тому сервіс повертає **409**, а не приховує помилку.
Порівняння з еталоном у статистиці вимагає збігу одиниць еталона й приладу.

## Відтворення перевірки

```powershell
dotnet build LabStem.Server.slnf -c Release
dotnet test LabStem.Server.slnf -c Release
```

Для повного прогону задайте `LAB_TEST_POSTGRES=1` і явний
`LAB_TEST_POSTGRES_CONNECTION` до окремої тестової PostgreSQL. Нові HTTP-тести
створюють тимчасові бази `resource_api_<uuid>` і видаляють їх після перевірки;
тестовому користувачу потрібне право CREATEDB. Не використовуйте виробничу БД.
У CI використовується вже налаштований тестовий `ConnectionStrings__Postgres`.

`ResourceApiTests`, `ResourceRaceApiTests`, `ResourcePostgresApiTests` перевіряють
реальний ASP.NET-конвеєр, JWT, міграції, DTO/OpenAPI, власність, числові результати,
пагінацію, конфлікти версій/UUID/FK та відкат аудиту. SQLite працює без зовнішніх
сервісів; PostgreSQL-тести явно пропускаються без увімкненого тестового середовища.
