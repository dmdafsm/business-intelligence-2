# Local Docker stack

Docker Desktop or Docker Engine with Compose is the only runtime prerequisite. Python 3.9+ prepares a generated local password and the report directory. No host .NET SDK or PostgreSQL is needed to run the application or the verification container.

```sh
python3 scripts/prepare_docker.py
docker compose up --build --detach --wait
docker compose ps --all
```

The portal is at <http://localhost:8090>, with [liveness](http://localhost:8090/health/live) and [readiness](http://localhost:8090/health/ready) endpoints. Set BI_HTTP_PORT before starting Compose to use another host port; the container still listens on 8080.

The stack is self-contained: Compose project `business-intelligence-2`, database volume `business-intelligence-2-postgres-data`, network `business-intelligence-2` and host port 8090.

Services:

- postgres: pinned PostgreSQL 18 image with a health check and a persistent named volume. Its port is not published to the host. It gets 3 GB of memory, 4 CPUs, 1 GB of shared memory and larger buffers, WAL and maintenance memory, which the national imports and index builds need.
- migrate: one-off command using the application image; it waits for PostgreSQL and migrates only the configured business_intelligence database. web starts only after it succeeds.
- web: pinned ASP.NET chiseled runtime, non-root, read-only root filesystem, bounded /tmp, dropped capabilities and a health probe. It binds only to host loopback. It reads `config/` (read-only) and the operator token for `/admin`.
- worker: the same image running `jobs worker`: the [import jobs](import-jobs.md) schedule and queue. It mounts `.research/raw` read-write, because it downloads new source files, and calls data.gov.ro.
- verify: optional profile with the SDK, Python and PostgreSQL server binaries; tests create their own disposable clusters.

The web process never applies migrations itself. This is local setup, not a production deployment procedure.

## Behind an HTTPS proxy

When the portal is published through a proxy that terminates TLS (for example a Cloudflare Tunnel to `web:8080`), set `Proxy__TrustForwardedProto=true` on web, in an untracked `compose.override.yaml`. The app then takes the scheme from X-Forwarded-Proto: redirects stay on https and the operator cookie is Secure. Only the scheme is trusted, and only enable it when the port is reachable solely through that proxy, since any client that reaches it directly can set the header. Put `/admin` behind the proxy's own access control as well.

## Commands and data

```sh
docker compose logs --tail 100 web migrate postgres
docker compose exec -T postgres psql -U bi_local -d business_intelligence
docker compose stop
docker compose start --wait
docker compose down
```

`down` keeps the named database volume. Keep `.artifacts/docker/postgres-password` with that volume: rerunning preparation preserves the credential instead of rotating it, and deleting it while keeping the database causes a credential mismatch. Deleting the volume is a deliberate destructive reset.

The password is generated outside Git and excluded from the build context. Compose mounts it as a file, and the app reads it through Database__PasswordFile. Its host parent directory has mode 0700. The operator token for <http://localhost:8090/admin/joburi> (`.artifacts/docker/admin-token`) is generated and kept the same way.

No data is seeded automatically. To load everything from the downloaded files in `.research/raw` (see the import guides):

```sh
python3 scripts/docker_import_all.py
```

That is the first full load. Later editions come through the [import jobs](import-jobs.md). The script runs each import in order through the `import` service, which mounts `.research/raw` read-only at `/data/raw`, then rebuilds the search tables. Every step is idempotent, so rerunning it after a failure continues where it stopped. A single command runs the same way:

```sh
docker compose --profile import run --rm import registry status
docker compose --profile import run --rm import fiscal show 2816464
```

## Verify everything in Linux

```sh
python3 scripts/prepare_docker.py
docker compose --profile verify build verify
docker compose --profile verify run --rm --no-deps verify
```

The verification image holds a disposable Git inventory of the build context, not your .git directory or credentials, and runs the same `scripts/verify.py`. Reports survive under `.artifacts/docker/verification/`. Building needs network access; tests do not call public data sources.

On macOS, if `docker` is not on PATH, prepend `/Applications/Docker.app/Contents/Resources/bin` for the shell session and start Docker Desktop.
