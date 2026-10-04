# Lab STEM - «Польова лабораторія» (команда «Сігма»)

Скелет командного STEM-проєкту з дисципліни «Мультиплатформне програмування»
(КНУ ім. Тараса Шевченка, ФІТ, кафедра програмних систем і технологій).

**Правила роботи команди - у файлі [`AGENTS.md`](AGENTS.md).** Його читає кожен учасник і
кожен ШІ-агент перед першою зміною в коді. Зони відповідальності, визначення готовності,
приймальні кейси та вимоги до PR - там.

## Що вже працює в цьому скелеті

- шарувата структура: домен → контракти → прикладний шар → інфраструктура → API / Worker;
- доступ до даних через EF Core з перемиканням СУБД **конфігурацією** (SQLite і PostgreSQL);
- окремі проєкти міграцій для SQLite і PostgreSQL (щоб набори не перебивали один одного);
- один наскрізний приклад ресурсу (`Site`): сутність → DTO → сервіс → контролер → тести;
- тести: правила домену, тест архітектури, сервіс на SQLite у пам'яті, тести проти реальної PostgreSQL;
- CI (GitHub Actions): збірка без попереджень, тести з покриттям, PostgreSQL як сервіс;
- `docker compose` для локальної PostgreSQL і `.env.example` без секретів;
- `scripts/coverage-summary.py` - зведення покриття з усіх тестових проєктів.

## Запуск з нуля

Потрібні: .NET SDK 10 (версія зафіксована в `global.json`), Docker, Python 3 (для скриптів).

```bash
# 1. Збірка (попередження вважаються помилками)
dotnet build LabStem.slnx -c Release

# 2. Тести без PostgreSQL (тести PG позначаться пропущеними)
dotnet test LabStem.slnx

# 3. Локальна PostgreSQL
docker compose -f docker/dev-postgres.yml up -d
docker compose -f docker/dev-postgres.yml ps      # чекаємо стан healthy

# 4. Тести проти реальної PostgreSQL
export LAB_TEST_POSTGRES=1
export ConnectionStrings__Postgres="Host=localhost;Port=55432;Database=lab;Username=lab;Password=lab_dev_password"
dotnet test LabStem.slnx

# 5. Покриття
dotnet test LabStem.slnx -c Release --collect:"XPlat Code Coverage" --results-directory TestResults
python3 scripts/coverage-summary.py

# 6. API на SQLite
dotnet run --project src/Lab.Api

# 7. API на PostgreSQL
export LAB_DB_PROVIDER=Postgres
export ConnectionStrings__Postgres="Host=localhost;Port=55432;Database=lab;Username=lab;Password=lab_dev_password"
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
dotnet build LabStem.slnx -c Release
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

Порядок перших задач - у `AGENTS.md`, розділ 19 («Стартовий беклог»). Перші три:
створити репозиторій і перенести скелет, зафіксувати зони відповідальності, затвердити
приймальні кейси. Автентифікацію (зона Z3) і клієнт MAUI (зона Z4) скелет свідомо не містить:
це робота відповідних зон, а не заготовка.
