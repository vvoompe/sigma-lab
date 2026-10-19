# Lab STEM - «Польова лабораторія» (команда «Сігма»)

Скелет командного STEM-проєкту з дисципліни «Мультиплатформне програмування»
(КНУ ім. Тараса Шевченка, ФІТ, кафедра програмних систем і технологій).

Процес роботи, домовленості щодо стилю коду та зони відповідальності описані у файлі
[`CONTRIBUTING.md`](CONTRIBUTING.md). Поточні задачі - у розділі Issues цього репозиторію,
розподілені за трьома контрольними точками.

## Команда «Сігма»

| Учасник | Зона відповідальності | Директорії |
|---|---|---|
| Олександр Маркграф ([@vvoompe](https://github.com/vvoompe)) | Z1 - домен, схема БД, індекси, міграції на дві СУБД, розрахункове ядро | `src/Lab.Domain`, `src/Lab.Infrastructure`, `src/Lab.Migrations.*` |
| Артем Захарченко ([@DiXer-code](https://github.com/DiXer-code)) | Z2 - контракти, сервісний шар, API, CI, Docker | `src/Lab.Contracts`, `src/Lab.Application`, `src/Lab.Api`, `docker/`, `.github/` |
| Іван Клебанський ([@llkbb](https://github.com/llkbb)) | Z3 - автентифікація, ролі, політики, володіння ресурсом, аудит | `src/Lab.Api/Security`, політики, журнал аудиту |
| Юрій Петровський ([@otmepush](https://github.com/otmepush)) | Z4 - MAUI-клієнт, локальна база, черга синхронізації, фоновий обробник | `src/Lab.Client.Maui`, `src/Lab.Worker` |

Деталі процесу, рівні складності зон і приймальні кейси - у [`CONTRIBUTING.md`](CONTRIBUTING.md).

## Що вже працює в цьому скелеті

- шарувата структура: домен → контракти → прикладний шар → інфраструктура → API / Worker;
- доступ до даних через EF Core з перемиканням СУБД **конфігурацією** (SQLite і PostgreSQL);
- окремі проєкти міграцій для SQLite і PostgreSQL (щоб набори не перебивали один одного);
- модель User/Site/Instrument/Experiment/Measurement/ReferenceValue, міграції двох СУБД і математичне ядро;
- наскрізний API ресурсу `Site`: DTO, фільтри, пагінація, сервіс, контролер, HTTP/OpenAPI-тести;
- тести: правила домену, тест архітектури, сервіс на SQLite у пам'яті, тести проти реальної PostgreSQL;
- CI (GitHub Actions): збірка без попереджень, тести з покриттям, PostgreSQL як сервіс;
- `docker compose` для локальної PostgreSQL і `.env.example` без секретів;
- `scripts/coverage-summary.py` - зведення покриття з усіх тестових проєктів.

## Запуск з нуля

Потрібні: .NET SDK 10 (версія зафіксована в `global.json`), Docker, Python 3 (для скриптів).
Усі команди виконуються з кореня клону. Локальний manifest `dotnet-tools.json` фіксує
`dotnet-ef` 10.0.12, сумісний з EF Core 10.0.12 у проєктах міграцій.

```text
git clone https://github.com/vvoompe/sigma-lab.git
cd sigma-lab
dotnet --version
dotnet tool restore
dotnet ef --version
```

Нижче — **Bash**. Для Windows PowerShell використайте окремий блок нижче.
Серверний фільтр не потребує MAUI workloads.

```bash
# 1. Збірка (попередження вважаються помилками)
dotnet build LabStem.Server.slnf -c Release

# 2. Тести без PostgreSQL (тести PG позначаться пропущеними)
dotnet test LabStem.Server.slnf -c Release --no-build

# 3. Локальна PostgreSQL
docker compose -f docker/dev-postgres.yml up -d
docker compose -f docker/dev-postgres.yml ps      # чекаємо стан healthy

# 4. Окрема тестова БД: не використовуйте базу з потрібними даними
docker exec lab-postgres-dev createdb -U lab lab_readme_tests
export LAB_TEST_POSTGRES=1
export LAB_TEST_POSTGRES_CONNECTION="Host=localhost;Port=55432;Database=lab_readme_tests;Username=lab;Password=lab_dev_password"
dotnet test LabStem.Server.slnf -c Release --no-build

# 5. Покриття
if [ -d TestResults ]; then mv TestResults "TestResults.previous-$(date +%Y%m%d-%H%M%S)"; fi
dotnet test LabStem.Server.slnf -c Release --collect:"XPlat Code Coverage" --results-directory TestResults
python3 scripts/coverage-summary.py

# 6. Згенерувати локальний ключ JWT і запустити API на SQLite
export Jwt__Key="$(openssl rand -base64 32)"
dotnet run --project src/Lab.Api

# 7. API на PostgreSQL
export LAB_DB_PROVIDER=Postgres
export ConnectionStrings__Postgres="Host=localhost;Port=55432;Database=lab;Username=lab;Password=lab_dev_password"
dotnet run --project src/Lab.Api
```

`TestResults` має бути свіжим перед **кожним** збором покриття: скрипт зливає всі XML
усередині каталогу, тому старі прогони можуть приховати регресію. Архів попереднього
прогону зберігайте поза `TestResults` і не комітьте. Поріг ядра — 80%.

Еквівалент перевірки в **PowerShell** після клонування й `dotnet tool restore`:

```powershell
dotnet build LabStem.Server.slnf -c Release
dotnet test LabStem.Server.slnf -c Release --no-build
docker compose -f docker/dev-postgres.yml up -d
docker compose -f docker/dev-postgres.yml ps # дочекайтеся healthy
docker exec lab-postgres-dev createdb -U lab lab_readme_tests
$env:LAB_TEST_POSTGRES = '1'
$env:LAB_TEST_POSTGRES_CONNECTION = 'Host=localhost;Port=55432;Database=lab_readme_tests;Username=lab;Password=lab_dev_password'
if (Test-Path TestResults) { Rename-Item -LiteralPath TestResults -NewName ('TestResults.previous-' + (Get-Date -Format yyyyMMdd-HHmmss)) }
dotnet test LabStem.Server.slnf -c Release --collect:"XPlat Code Coverage" --results-directory TestResults
python scripts/coverage-summary.py
```

Якщо `lab_readme_tests` вже існує, виберіть **нову** назву і в `createdb`, і в
`LAB_TEST_POSTGRES_CONNECTION`. Тести змінюють дані цієї бази; також створюють тимчасові
бази для перевірки міграцій, тому локальна тестова роль потребує `CREATEDB`.
Пароль у прикладі — лише для локального dev-контейнера з `docker/dev-postgres.yml`.

Запуск API в **PowerShell** (зупиняти `Ctrl+C` перед наступним запуском):

```powershell
$env:LAB_DB_PROVIDER = 'Sqlite'
dotnet run --project src/Lab.Api
$env:LAB_DB_PROVIDER = 'Postgres'
$env:ConnectionStrings__Postgres = 'Host=localhost;Port=55432;Database=lab;Username=lab;Password=lab_dev_password'
dotnet run --project src/Lab.Api
```

Міграції застосовуються під час старту API (`Database:MigrateOnStartup = true`). Окремо їх
можна застосувати командами:

```bash
dotnet ef database update --project src/Lab.Migrations.Sqlite
dotnet ef database update --project src/Lab.Migrations.Postgres
dotnet ef migrations list   --project src/Lab.Migrations.Sqlite
```

Додати нову сутність (правильний порядок):

```bash
# 1. сутність у src/Lab.Domain (без EF-атрибутів)
# 2. Fluent-конфігурація у src/Lab.Infrastructure/Persistence/Configurations
# 3. DbSet у LabDbContext
dotnet build LabStem.Server.slnf -c Release
dotnet ef migrations add НазваЗміни --project src/Lab.Migrations.Sqlite
dotnet ef migrations add НазваЗміни --project src/Lab.Migrations.Postgres
dotnet ef database update --project src/Lab.Migrations.Sqlite
dotnet ef database update --project src/Lab.Migrations.Postgres
```

## Явні рядки підключення (коли треба вказати свій шлях, порт або базу)

Застосунок читає рядки підключення з конфігурації, тому їх можна перекрити змінними середовища:

```bash
# API на конкретному файлі SQLite (увага: Windows-шлях, не /c/...)
ConnectionStrings__Sqlite="Data Source=C:/шлях/до/lab.db" dotnet run --project src/Lab.Api

# API на PostgreSQL з іншого порту
LAB_DB_PROVIDER=Postgres \
ConnectionStrings__Postgres="Host=localhost;Port=55433;Database=lab;Username=lab;Password=lab_dev_password" \
dotnet run --project src/Lab.Api

# `dotnet ef` з тим самим рядком, що й застосунок
LAB_POSTGRES_CONNECTION="Host=localhost;Port=55433;Database=lab;Username=lab;Password=lab_dev_password" \
dotnet ef database update --project src/Lab.Migrations.Postgres

LAB_SQLITE_CONNECTION="Data Source=C:/шлях/до/lab.db" \
dotnet ef database update --project src/Lab.Migrations.Sqlite
```

`LAB_POSTGRES_CONNECTION` і `LAB_SQLITE_CONNECTION` читають лише фабрики часу проєктування
(тобто команди `dotnet ef`); застосунок використовує `ConnectionStrings__*`.
API потребує `Jwt__Key` у змінному середовищі навіть до додавання endpoint-ів входу;
локально створіть випадкове значення командою `openssl rand -base64 32`. Не додавайте
його до файлів конфігурації, репозиторію або журналу комітів.

Для PowerShell змінні задаються окремо перед командою:

```powershell
$env:LAB_SQLITE_CONNECTION = 'Data Source=C:/шлях/до/lab.db'
dotnet ef database update --project src/Lab.Migrations.Sqlite
$env:LAB_POSTGRES_CONNECTION = 'Host=localhost;Port=55432;Database=lab;Username=lab;Password=lab_dev_password'
dotnet ef database update --project src/Lab.Migrations.Postgres
```

## Діагностика

| Симптом | Причина і що робити |
|---|---|
| `dotnet: command not found`, код 127 | `DOTNET_ROOT` має бути Windows-шляхом (`C:/Users/<user>/.dotnet`), а в `PATH` - MSYS-форма (`/c/Users/<user>/.dotnet`): bash ріже `PATH` по двокрапці |
| `dotnet ef` пише «SDK not found» або не бачить рантайм | Не заданий `DOTNET_ROOT`, або в `PATH` інший runtime |
| `dotnet ef migrations add` у новому проєкті міграцій падає з «doesn't reference Microsoft.EntityFrameworkCore.Design» | У проєкті міграцій потрібні пакет `Microsoft.EntityFrameworkCore.Design` **і** власна `*DesignTimeDbContextFactory` |
| Збірка падає з попередженнями в згенерованих міграціях | Зауваження аналізаторів на генерованому коді гасяться `<NoWarn>` у проєкті міграцій, а не вимкненням аналізу в solution |
| SQLite кидає `NotSupportedException` на `DateTimeOffset` | SQLite не сортує й не агрегує `DateTimeOffset`: використовуйте `DateTime` в UTC |
| Порядок `NULL` у сортуванні різний на двох СУБД | SQLite ставить `NULL` першими, PostgreSQL - останніми; тести сортування це враховують |
| Тести PG пропущені в CI | Не виставлена `LAB_TEST_POSTGRES=1` або сервіс `postgres` не піднявся |
| У рядку підключення до SQLite шлях `/c/...` | Усередині застосунку потрібен Windows-шлях (`C:/...`); MSYS-форма працює лише в оболонці |

## Структура репозиторію

```
src/Lab.Domain             сутності й правила предметної області (без EF, без залежностей)
src/Lab.Contracts          DTO і моделі запитів, які бачить клієнт
src/Lab.Application        сервіси, абстракції (IAppDbContext), розрахункове ядро (STEM-математика)
src/Lab.Infrastructure     LabDbContext, Fluent-конфігурації, вибір провайдера
src/Lab.Migrations.Sqlite  міграції для SQLite + фабрика часу проєктування
src/Lab.Migrations.Postgres міграції для PostgreSQL + фабрика часу проєктування
src/Lab.Api                контролери, обробка помилок, OpenAPI
src/Lab.Worker             фоновий обробник
src/Lab.Client.Maui        клієнт MAUI (створює власник зони Z4, див. README у теці)
tests/Lab.Domain.Tests     правила домену і тест архітектури
tests/Lab.Application.Tests сервіси на SQLite у пам'яті
tests/Lab.Postgres.Tests   тести проти реальної PostgreSQL
docs/                      архітектура, приймальні кейси, лог рішень, декларація ШІ
docker/                    локальна PostgreSQL
scripts/                   службові скрипти (зведення покриття)
```

## Що робити далі

Фільтри, параметри пагінації та новий формат відповіді `GET /api/sites` описані у
[`docs/api-sites.md`](docs/api-sites.md). Перевірки порога покриття й налаштування
обов'язкового CI — у [`docs/ci-coverage.md`](docs/ci-coverage.md).

Задачі розподілені в розділі Issues за трьома контрольними точками; план перших задач і зони
відповідальності - у `CONTRIBUTING.md`. Автентифікація належить Z3. Модель Z1 вже в main;
підключення екрана серій до неї — окрема задача Z4, а серверного API серій — Z2.

## Повна збірка з MAUI

Серверні команди вище використовують `LabStem.Server.slnf` і працюють без MAUI workloads.
Повний `LabStem.slnx` також містить клієнт Android/Windows. На Windows встановіть
`dotnet workload install maui-android maui-windows`, Android SDK і JDK за
[інструкцією клієнта](src/Lab.Client.Maui/README.md), потім виконайте:

```powershell
dotnet build LabStem.slnx -c Release
dotnet test LabStem.Server.slnf -c Release
```

У CI серверні тести виконуються завжди; клієнт збирається окремим Windows job лише при
змінах клієнта, його спільних залежностей або параметрів збірки.
