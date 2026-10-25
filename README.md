# Business Intelligence

Portal pentru găsirea firmelor din România și consultarea datelor publice despre ele: date generale, contact, activitate și istoricul financiar complet. Datele provin din surse oficiale deschise (ONRC, Ministerul Finanțelor, INS), importate local; paginile nu apelează sursele la afișare.

Scopul curent este [MVP-ul](.research/mvp-plan.md): căutare după denumire, CUI sau număr de înregistrare, grupare după CAEN, județ și localitate, și profilul firmei. Progresul pe pași este în [harta implementării](.research/implementation-map.json).

## Pornire

```sh
python3 scripts/prepare_docker.py
docker compose up --build --detach --wait
```

Portalul rulează la <http://localhost:8080>. Detalii în [ghidul Docker](docs/engineering/docker-local.md).

## Dezvoltare

- [Instrucțiuni pentru agenți](AGENTS.md) și [ghidul contribuțiilor](CONTRIBUTING.md).
- [Arhitectura](.research/architecture.md) și [design system-ul Material 3](docs/engineering/design-system.md).
- [Standarde de cod](docs/engineering/coding-standards.md), [testare](docs/engineering/testing-and-verification.md), [securitate și date](docs/engineering/security-and-data.md), [browser](docs/engineering/browser-testing.md), [code review](docs/engineering/code-review.md).
- [Branch-uri și PR-uri](docs/engineering/branch-policy.md). Instalare hooks: `python3 scripts/install_hooks.py`.

Verificare completă:

```sh
python3 scripts/verify.py
```
