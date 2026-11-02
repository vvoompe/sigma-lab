#!/usr/bin/env python3
"""Наскрізна перевірка окремого compose-стека; паролі й JWT не друкуються."""
import json
import os
import secrets
import subprocess
import urllib.error
import urllib.request
import uuid


def command(*args):
    return subprocess.run(["docker", "compose", "exec", "-T", *args],
                          check=True, capture_output=True, text=True, encoding="utf-8").stdout.strip()


def request(path, status, data=None, token=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    body = json.dumps(data).encode("utf-8") if data is not None else None
    req = urllib.request.Request(os.environ.get("LAB_API_URL", "http://127.0.0.1:5080") + path,
                                 data=body, headers=headers)
    try:
        response = urllib.request.urlopen(req, timeout=20)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        if response.status != status:
            raise RuntimeError(f"{path}: очікувався {status}, отримано {response.status}")
        text = response.read().decode("utf-8")
        return json.loads(text) if text.startswith("{") else text


def main():
    assert request("/health/ready", 200) == "Healthy"
    request("/api/sites", 401)
    auth = request("/api/auth/register", 201, {
        "name": "Дослідник Docker", "email": f"docker-{uuid.uuid4().hex}@example.test",
        "password": secrets.token_urlsafe(24),
    })
    token = auth["accessToken"]
    site = request("/api/sites", 201, {"name": "Польовий майданчик", "latitude": 50.45, "longitude": 30.52}, token)
    assert request(f"/api/sites/{site['id']}", 200, token=token)["name"] == "Польовий майданчик"
    request("/api/sites", 400, {"name": "Некоректний", "latitude": 200, "longitude": 30}, token)
    row = command("postgres", "psql", "-U", "lab", "-d", "lab", "-Atc",
                  f'SELECT "Name" FROM sites WHERE "Id"={int(site["id"])}')
    assert row == "Польовий майданчик"
    migrations = int(command("postgres", "psql", "-U", "lab", "-d", "lab", "-Atc", 'SELECT count(*) FROM "__EFMigrationsHistory"'))
    assert migrations >= 4
    assert command("api", "id", "-u") != "0"
    assert command("api", "dotnet", "--list-sdks") == ""
    print(f"PASS: readiness 200; anonymous 401; API create 201 / read 200 / validation 400; PostgreSQL row matches; {migrations} migrations; non-root runtime without SDK")


if __name__ == "__main__":
    main()
