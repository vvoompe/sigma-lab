#!/usr/bin/env python3
"""Підсумковий CI gate: сервер обов'язковий, MAUI залежить від змінених шляхів."""

import json
import os
import sys


def main() -> int:
    try:
        needs = json.loads(os.environ["NEEDS_JSON"])
        for job in ("changes", "build-and-test", "server-sdk-compatibility"):
            if needs[job]["result"] != "success":
                print(f"НЕ виконано: {job} = {needs[job]['result']}")
                return 1
        relevant = needs["changes"]["outputs"]["maui"]
        result = needs["maui-build"]["result"]
        if relevant not in ("true", "false"):
            raise ValueError("невідомий результат фільтра MAUI")
        accepted = ("success",) if relevant == "true" else ("success", "skipped")
        if result not in accepted:
            print(f"НЕ виконано: maui-build = {result}, релевантні зміни = {relevant}")
            return 1
    except (KeyError, TypeError, ValueError) as error:
        print(f"НЕ виконано: неповні або некоректні результати jobs ({error})")
        return 1
    print(f"CI gate виконано: сервер зелений; maui-build = {result}, релевантні зміни = {relevant}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
