# Local Docker stack

Docker Desktop or Docker Engine with Compose is the only runtime prerequisite. Python 3.9+ prepares a generated local password and the report directory. No host .NET SDK or PostgreSQL is needed to run the application or the verification container.

```sh
python3 scripts/prepare_docker.py
docker compose up --build --detach --wait
docker compose ps --all
```

The portal is at <http://localhost:8080>, with [liveness](http://localhost:8080/health/live) and [readiness](http://localhost:8080/health/ready) endpoints. Set BI_HTTP_PORT before starting Compose to use another host port; the container still listens on 8080.

Services:

- postgres: pinned PostgreSQL 18 image with a health check and a persistent named volume. Its port is not published to the host.
- migrate: one-off command using the application image; it waits for PostgreSQL and migrates only the configured business_intelligence database. web starts only after it succeeds.
- web: pinned ASP.NET chiseled runtime, non-root, read-only root filesystem, bounded /tmp, dropped capabilities and a health probe. It binds only to host loopback.
- verify: optional profile with the SDK, Python and PostgreSQL server binaries; tests create their own disposable clusters.

The web process never applies migrations itself. This is local setup, not a production deployment procedure.

## Commands and data

```sh
docker compose logs --tail 100 web migrate postgres
docker compose exec -T postgres psql -U bi_local -d business_intelligence
docker compose stop
docker compose start --wait
docker compose down
```

`down` keeps the named database volume. Keep `.artifacts/docker/postgres-password` with that volume: rerunning preparation preserves the credential instead of rotating it, and deleting it while keeping the database causes a credential mismatch. Deleting the volume is a deliberate destructive reset.

The password is generated outside Git and excluded from the build context. Compose mounts it as a file, and the app reads it through Database__PasswordFile. Its host parent directory has mode 0700.

No data is seeded automatically. Import commands are documented with each import step.

## Verify everything in Linux

```sh
python3 scripts/prepare_docker.py
docker compose --profile verify build verify
docker compose --profile verify run --rm --no-deps verify
```

The verification image holds a disposable Git inventory of the build context, not your .git directory or credentials, and runs the same `scripts/verify.py`. Reports survive under `.artifacts/docker/verification/`. Building needs network access; tests do not call public data sources.

On macOS, if `docker` is not on PATH, prepend `/Applications/Docker.app/Contents/Resources/bin` for the shell session and start Docker Desktop.
