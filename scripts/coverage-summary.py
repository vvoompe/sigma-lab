#!/usr/bin/env python3
"""Зведення покриття з усіх звітів cobertura, створених `dotnet test --collect`.

Навіщо: кожен тестовий проєкт пише власний `coverage.cobertura.xml`, і окремий прогін
показує лише ті рядки, які виконали його тести. Скрипт зливає всі звіти (рядок вважається
покритим, якщо його виконав хоч один прогін) і друкує числа по збірках.

Використання (з кореня репозиторію):
    dotnet test -c Release --collect:"XPlat Code Coverage" --results-directory TestResults
    python3 scripts/coverage-summary.py

Залежностей немає: тільки стандартна бібліотека Python.
Порогове значення для C3 - 80 % - перевіряється тут же: скрипт завершується кодом 1,
якщо покриття ядра нижче порога, і тоді CI червоніє.
"""

from __future__ import annotations

import glob
import os
import sys
import xml.etree.ElementTree as ElementTree

RESULTS_DIR = "TestResults"
BACKSLASH = chr(92)
CORE_PREFIXES = ("Lab.Domain/", "Lab.Application/", "Lab.Infrastructure/")
THRESHOLD_PERCENT = 80.0
ASSEMBLIES = (
    ("Lab.Domain", "Lab.Domain/"),
    ("Lab.Contracts", "Lab.Contracts/"),
    ("Lab.Infrastructure", "Lab.Infrastructure/"),
    ("Lab.Application", "Lab.Application/"),
    ("Lab.Api", "Lab.Api/"),
    ("Lab.Worker", "Lab.Worker/"),
    ("Lab.Migrations.Sqlite", "Lab.Migrations.Sqlite/"),
    ("Lab.Migrations.Postgres", "Lab.Migrations.Postgres/"),
)


def collect_hits() -> tuple[dict[tuple[str, int], int], int]:
    """Зливає рядки з усіх звітів покриття."""
    hits: dict[tuple[str, int], int] = {}
    reports = sorted(glob.glob(os.path.join(RESULTS_DIR, "**", "coverage.cobertura.xml"), recursive=True))
    for report in reports:
        root = ElementTree.parse(report).getroot()
        for cls in root.iter("class"):
            file_name = (cls.get("filename") or "").replace(BACKSLASH, "/")
            if not file_name:
                continue
            for line in cls.iter("line"):
                number = line.get("number")
                if not number:
                    continue
                key = (file_name, int(number))
                value = int(line.get("hits") or 0)
                # Рядок із нульовим покриттям теж треба внести, інакше він не потрапить
                # у знаменник і покриття завжди виглядатиме як 100 %.
                if key not in hits or value > hits[key]:
                    hits[key] = value
    return hits, len(reports)


def ratio(hits: dict[tuple[str, int], int], prefixes: tuple[str, ...]) -> tuple[int, int]:
    """(покриті, усього) для рядків, чий шлях починається з одного з префіксів."""
    total = covered = 0
    for (file_name, _), value in hits.items():
        if any(prefix in file_name for prefix in prefixes):
            total += 1
            covered += 1 if value > 0 else 0
    return covered, total


def main() -> int:
    if not os.path.isdir(RESULTS_DIR):
        print(f"! теку {RESULTS_DIR} не знайдено - спершу прогнати dotnet test з --collect")
        return 1

    hits, report_count = collect_hits()
    if not hits:
        print(f"! у теці {RESULTS_DIR} немає звітів coverage.cobertura.xml")
        return 1

    lines: list[str] = [f"Звітів злито: {report_count}", "", "Покриття по збірках:"]
    for name, prefix in ASSEMBLIES:
        covered, total = ratio(hits, (prefix,))
        if total:
            lines.append(f"  {name}: {covered}/{total} = {covered / total * 100:.2f} %")

    covered, total = ratio(hits, ("",))
    solution_percent = covered / total * 100 if total else 0.0
    lines.append(f"  РАЗОМ solution: {covered}/{total} = {solution_percent:.2f} %")

    core_covered, core_total = ratio(hits, CORE_PREFIXES)
    core_percent = core_covered / core_total * 100 if core_total else 0.0
    lines.append("")
    lines.append(
        "Ядро (домен + прикладний шар + інфраструктура): "
        f"{core_covered}/{core_total} = {core_percent:.2f} %"
    )
    lines.append(f"Поріг C3: {THRESHOLD_PERCENT:.0f} % - "
                 f"{'виконано' if core_percent >= THRESHOLD_PERCENT else 'НЕ виконано'}")

    output = "\n".join(lines)
    print(output)

    summary_path = os.path.join(RESULTS_DIR, "coverage-summary.txt")
    os.makedirs(RESULTS_DIR, exist_ok=True)
    with open(summary_path, "w", encoding="utf-8") as summary:
        summary.write(output + "\n")

    return 0 if core_percent >= THRESHOLD_PERCENT else 1


if __name__ == "__main__":
    sys.exit(main())
