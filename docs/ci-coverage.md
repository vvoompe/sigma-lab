# Перевірки CI та достовірність покриття — Z2

`build-and-test` запускає Release-збірку, тести з PostgreSQL і перевірку покриття ядра.
Поріг — **80%**, точний склад ядра: `Lab.Domain`, `Lab.Application`, `Lab.Infrastructure`.
Рядок ідентифікується трійкою `(package/assembly, відносний шлях усередині assembly, номер)`.
Короткий, повний та Windows-шлях одного файла зливаються через **max hits**; нульові hits
залишаються в знаменнику. Однакові шляхи різних assemblies не зливаються. Назва package,
а не підрядок у filename, визначає належність до ядра. XML без визначеної assembly
відхиляється, щоб не втрачати рядки мовчки.

```text
python -m unittest discover -s scripts -p "test_*.py" -v
dotnet build LabStem.Server.slnf -c Release
dotnet test LabStem.Server.slnf -c Release --collect:"XPlat Code Coverage" --results-directory TestResults
python scripts/coverage-summary.py
```

Перед кожним збором перенесіть старий `TestResults` за межі цієї теки або працюйте в
чистому клоні. Команди Bash/PowerShell, встановлення локального `dotnet-ef` та підключення
до **окремої** тестової PostgreSQL описані в [README](../README.md). Пропущені PG-тести
не є підтвердженням перевірки PostgreSQL. Артефакт CI `coverage` містить XML та підсумок.

## Захист main: перевірено 19.10.2026

GitHub API `branches/main` повертає `protected=true`, `enforcement_level=everyone` та
чотири чинні required checks від GitHub Actions:

- `changes`;
- `build-and-test`;
- `server-sdk-10.0.100 (ubuntu-latest)`;
- `server-sdk-10.0.100 (windows-latest)`.

Issue #7 закрито. Опис відсутніх required checks у перевірці **12.10.2026** був
історичним станом; він більше не описує main. Залишкові правки ведуться в #30.

## Агрегований gate для MAUI

Новий `ci-gate` залежить від усіх чотирьох jobs, запускається з `if: always()` і читає
їхні результати через `toJSON(needs)`. Сервер і фільтр шляхів мають завершитися успішно.
Якщо `changes.outputs.maui=true`, приймається лише успішний `maui-build`; при `false`
допускається штатний `skipped`. Failure/cancelled та відсутні дані завершують gate помилкою.
Path filter збережено, серверні jobs не встановлюють MAUI workloads.

**Дія власника @vvoompe після перевірки змін до #30:** додати `ci-gate` (GitHub Actions,
app id 15368) до поточного списку required checks, **залишивши всі чотири наявні**,
правило рев'ю та решту налаштувань. Акаунт DiXer-code має push, але не admin/maintain;
тому код gate не означає, що налаштування вже застосовано.

Після додавання власник має перевірити на тестовому PR: невдалий MAUI дає червоний gate
і заборону merge, нерелевантні зміни зі skipped MAUI дають зелений gate. Локальні тести
перевіряють також скасування, невідомий результат фільтра й падіння серверних jobs.
До підтвердження Settings адміністративний пункт #30 залишається відкритим.
