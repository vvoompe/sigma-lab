#!/usr/bin/env python3
"""Зведення покриття з усіх звітів cobertura, створених `dotnet test --collect`.

Навіщо: кожен тестовий проєкт пише власний `coverage.cobertura.xml`, і окремий прогін
показує лише ті рядки, які виконали його тести. Скрипт зливає всі звіти (рядок вважається
покритим, якщо його виконав хоч один прогін) і друкує числа по збірках.

Використання (з кореня репозиторію):
    dotnet test -c Release --collect:"XPlat Code Coverage" --results-directory TestResults
    python3 scripts/coverage-summary.py

Залежностей немає: тільки стандартна бібліотека Python.
Порогове значення 80 % - перевіряється тут же: скрипт завершується кодом 1,
якщо покриття ядра нижче порога, і тоді CI червоніє.
"""

from __future__ import annotations

import glob
import os
import posixpath
import sys
import xml.etree.ElementTree as ElementTree

RESULTS_DIR = "TestResults"
BACKSLASH = chr(92)
CORE_ASSEMBLIES = frozenset(("Lab.Domain", "Lab.Application", "Lab.Infrastructure"))
THRESHOLD_PERCENT = 80.0
LineHits = dict[tuple[str, str, int], int]


def canonical_path(file_name: str, assembly: str, sources: list[str]) -> str:
    """Шлях усередині assembly; підтримує формати filename у звітах Coverlet."""
    path = posixpath.normpath(file_name.replace(BACKSLASH, "/"))
    # Абсолютний filename може починатися з каталогу source самого проєкту.
    for source in sorted(sources, key=len, reverse=True):
        prefix = posixpath.normpath(source.replace(BACKSLASH, "/")).rstrip("/") + "/"
        if path.startswith(prefix):
            path = path[len(prefix):]
            break
    parts = path.split("/")
    if assembly in parts:
        path = "/".join(parts[parts.index(assembly) + 1:])
    return path


def collect_hits() -> tuple[LineHits, int]:
    """Зливає рядки з усіх звітів покриття."""
    hits: LineHits = {}
    reports = sorted(glob.glob(os.path.join(RESULTS_DIR, "**", "coverage.cobertura.xml"), recursive=True))
    for report in reports:
        root = ElementTree.parse(report).getroot()
        sources = [source.text for source in root.findall("./sources/source") if source.text]
        classes_seen = 0
        for package in root.findall("./packages/package"):
            assembly = package.get("name")
            if not assembly:
                raise ValueError(f"{report}: package без назви assembly")
            for cls in package.findall("./classes/class"):
                classes_seen += 1
                file_name = cls.get("filename")
                if not file_name:
                    raise ValueError(f"{report}: class без filename")
                path = canonical_path(file_name, assembly, sources)
                for line in cls.iter("line"):
                    number = int(line.get("number") or 0)
                    value = int(line.get("hits") or 0)
                    if number <= 0 or value < 0:
                        raise ValueError(f"{report}: некоректний номер рядка або hits")
                    key = (assembly, path, number)
                    # Нульові hits теж залишаються у знаменнику.
                    hits[key] = max(hits.get(key, 0), value)
        if classes_seen != sum(1 for _ in root.iter("class")):
            raise ValueError(f"{report}: class поза packages/package/classes; assembly невідома")
    return hits, len(reports)


def ratio(hits: LineHits, assemblies: frozenset[str] | None = None) -> tuple[int, int]:
    """(покриті, усього) для точних назв assemblies або всього solution."""
    total = covered = 0
    for (assembly, _, _), value in hits.items():
        if assemblies is None or assembly in assemblies:
            total += 1
            covered += 1 if value > 0 else 0
    return covered, total


def main() -> int:
    if not os.path.isdir(RESULTS_DIR):
        print(f"! теку {RESULTS_DIR} не знайдено - спершу прогнати dotnet test з --collect")
        return 1

    try:
        hits, report_count = collect_hits()
    except (ValueError, ElementTree.ParseError) as error:
        print(f"! Некоректний звіт покриття: {error}")
        return 1
    if not hits:
        print(f"! у теці {RESULTS_DIR} немає звітів coverage.cobertura.xml")
        return 1

    lines: list[str] = [f"Звітів злито: {report_count}", "", "Покриття по збірках:"]
    for name in sorted({assembly for assembly, _, _ in hits}):
        covered, total = ratio(hits, frozenset((name,)))
        if total:
            lines.append(f"  {name}: {covered}/{total} = {covered / total * 100:.2f} %")

    covered, total = ratio(hits)
    solution_percent = covered / total * 100 if total else 0.0
    lines.append(f"  РАЗОМ solution: {covered}/{total} = {solution_percent:.2f} %")

    core_covered, core_total = ratio(hits, CORE_ASSEMBLIES)
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
