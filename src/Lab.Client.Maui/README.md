# MAUI-клієнт зони Z4

Застосунок .NET MAUI для Android і Windows. Локальний доменний доступ використовує
LabDbContext і Fluent-конфігурації з Lab.Infrastructure; окремі доменні моделі клієнт не створює.

## Залежності та запуск

Потрібен .NET SDK 10, MAUI workloads для .NET 10, Android SDK API 36,
build-tools 36.0.0 і JDK 21. Наявність workload у списку не гарантує, що встановлено
SDK потрібної версії: компоненти MAUI для .NET 8/9 не збирають цей проєкт.

    dotnet workload list
    dotnet workload install maui-windows maui-android
    dotnet restore src/Lab.Client.Maui/Lab.Client.Maui.csproj
    dotnet build src/Lab.Client.Maui/Lab.Client.Maui.csproj -c Release -f net10.0-windows10.0.19041.0
    dotnet build src/Lab.Client.Maui/Lab.Client.Maui.csproj -c Release -f net10.0-android
    dotnet run --project src/Lab.Client.Maui/Lab.Client.Maui.csproj -f net10.0-windows10.0.19041.0

Відсутні залежності Android встановлюються стандартною ціллю SDK:

    dotnet build src/Lab.Client.Maui/Lab.Client.Maui.csproj -t:InstallAndroidDependencies -f net10.0-android -p:AndroidSdkDirectory=C:/android-sdk -p:JavaSdkDirectory=C:/jdk -p:AcceptAndroidSdkLicenses=True

Шляхи AndroidSdkDirectory і JavaSdkDirectory можна вибрати у власному каталозі;
для наступних збірок передавайте ті самі властивості. Короткі шляхи до NuGet-кешу
й Android SDK запобігають помилкам читання архівів на Windows.

Клієнт зберігає SQLite файл у приватному каталозі застосунку та застосовує наявні міграції
з Lab.Migrations.Sqlite. Екран показує майданчики з локальної БД і стан черги синхронізації.
Спільна модель Experiment уже додана до main у PR #26. Підключення екрана серій до неї
залишається наступною частиною #9 (ЧП2); локальна копія моделі не потрібна.

## Черга синхронізації

Операції мають UUID, тип ідентифікованого ресурсу, JSON payload, час створення, кількість спроб,
останню помилку та один зі станів Pending, Sent, Failed. Додавання ідемпотентне за UUID.
Помилкову операцію можна повернути в Pending; статус Sent фіксується після відповіді сервера.

Черга зберігається атомарним JSON-файлом у приватному каталозі клієнта. Вона відокремлена від
доменного LabDbContext, бо поточний спільний контекст і Fluent-модель належать Z1; це не додає
клієнтської копії Experiment або міграцій до чужої зони. Рішення та межі гарантій
описані в docs/decisions-log.md, запис №8. Зараз реалізовано локальне сховище операцій;
мережеве відправлення й політика конфліктів належать наступному issue #14.

## Перевірки

Команди виконуються з кореня репозиторію. Серверні тести перевіряють також локальні
сервіси клієнта без MAUI workloads, але не запускають його UI.

Перед **кожним** збором покриття потрібен свіжий кореневий каталог `TestResults`.
Архів попереднього прогону має бути поза цим каталогом; не додавайте його в Git.
Скрипт зливає всі XML усередині `TestResults`, тому старі звіти можуть приховати регресію.

```text
dotnet build LabStem.Server.slnf -c Release
dotnet test LabStem.Server.slnf -c Release --collect:"XPlat Code Coverage" --results-directory TestResults
python scripts/coverage-summary.py
```

Якщо Python доступний як `python3`, використовуйте цю назву. Приклад збереження
старих результатів у PowerShell перед командою тестів:

```powershell
if (Test-Path -LiteralPath TestResults) {
    Rename-Item -LiteralPath TestResults -NewName ('TestResults.previous-' + [guid]::NewGuid().ToString('N'))
}
```

За замовчуванням PostgreSQL-сценарії позначаються пропущеними. Для повного прогону
підготуйте окрему тестову PostgreSQL 15 і задайте змінні перед тими самими командами.
Тести змінюють дані; роль потребує `CREATEDB` для ізольованих сценаріїв міграцій.
Приклади нижче використовують лише локальні демонстраційні реквізити dev-контейнера;
ім'я бази має вказувати на вашу окрему тестову базу.

PowerShell:

```powershell
$env:LAB_TEST_POSTGRES = '1'
$env:LAB_TEST_POSTGRES_CONNECTION = 'Host=localhost;Port=55432;Database=lab_readme_tests;Username=lab;Password=lab_dev_password'
```

Bash:

```bash
export LAB_TEST_POSTGRES=1
export LAB_TEST_POSTGRES_CONNECTION='Host=localhost;Port=55432;Database=lab_readme_tests;Username=lab;Password=lab_dev_password'
```

Для повернення до прогону без PostgreSQL приберіть `LAB_TEST_POSTGRES` зі свого
середовища (`Remove-Item Env:LAB_TEST_POSTGRES` у PowerShell або `unset LAB_TEST_POSTGRES`
у Bash). Фактичні пропуски зазначайте у звіті перевірки.

LocalDatabaseServiceTests застосовує спільні міграції SQLite й перевіряє читання
збереженого Site. SyncQueueStoreTests перевіряє ідемпотентність після повторного
відкриття, переходи станів, порожню чергу, паралельні записи та пошкоджений файл.
Тести цих сервісів виконуються без MAUI UI через підключення того самого вихідного коду.

MigrationFactoryTests перевіряє пріоритет рядків підключення й вибір відповідного набору
міграцій. MigrationLifecycleTests перевіряє відкат до нуля та повторне створення схеми
на обох СУБД. PostgreSQL-сценарій працює в окремій базі `sigma_migration_<UUID>`;
тестовий користувач має право створювати й видаляти цю базу. Спільна база не відкочується.

Історичний CI коміту `3f4cee5` від 12 жовтня 2026 року підтвердив 62 успішні тести
без пропусків та 100% рядків тодішнього звіту. Ці числа не описують поточний main
і не є покриттям MAUI UI. Нові результати звіряйте з конкретним комітом і свіжими XML.

[Звіт незалежної перевірки Z4 від 19.10.2026](verification/CHP1-2026-10-19.md)
містить коміт, результати нового клону та обмеження середовища.

## Межі цього етапу

У main є спільні User, Site, Instrument, Experiment, Measurement і ReferenceValue та
міграції обох СУБД від Z1. MAUI включено до LabStem.slnx у злитому PR #24;
окремий job maui-build збирає Windows і Android при змінах клієнта або його залежностей.
Екран зараз показує Site. Екран серій і перевірка запуску на пристроях залишаються
критеріями #9 для ЧП2. Мережеве відправлення (#14) і розширення воркера (#15) належать ЧП3.
Успішна компіляція не підтверджує приймання UI, тому весь #9 не закривається.

## Solution і CI

`LabStem.slnx` містить також клієнт. Повна Release-збірка на Windows із встановленими
workloads, Android SDK та JDK: `dotnet build LabStem.slnx -c Release`.
Для серверних проєктів і тестів без MAUI (зокрема на Linux) використовуйте
`LabStem.Server.slnf`; він фільтрує той самий solution.
У командах build/test/restore вказуйте файл явно.

CI завжди виконує `build-and-test` на Ubuntu із PostgreSQL. Job `changes` порівнює
всі змінені шляхи PR із базовим комітом, а push у main — із попереднім комітом.
`maui-build` на Windows запускається при зміні клієнта, Domain, Contracts, Application,
Infrastructure, SQLite-міграцій, solution, спільних параметрів SDK/пакетів або самого workflow.
Зміни лише документації, тестів, API чи Worker не встановлюють workloads.
Job збирає обидві платформи; запуск UI на пристроях перевіряється окремо.

Шляхи projects у серверному .slnf зберігаються зі зворотними слешами, екранованими
в JSON (`\\`). Це потрібно для сумісності з SDK 10.0.100 на Windows. CI окремо
перевіряє серверний фільтр на цьому SDK на Windows і Ubuntu без MAUI workloads.
