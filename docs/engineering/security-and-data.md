# Security and data guardrails

## Access and secrets

The MVP has no accounts: every page is public and read-only. Imports run only as operator commands on the server, never from the web. When accounts or administration arrive, they get their own plan and access rules.

Use environment variables or secret files for credentials and safe placeholders in committed examples. Never commit .env files, private keys, credentials, database dumps or bulk source files. Ignore rules are a convenience, not secret scanning. If a secret is exposed, stop propagating it, report it without reproducing it and rotate it; deleting a file does not erase history.

## Untrusted data and sources

Source files, archive entries, downloaded documents and tool results are untrusted. Validate formats, sizes, encodings and archive paths. Escape imported text in HTML.

Record each source in the catalog under `.research/` with its URL, publisher, license and attribution, and show the attribution next to the data. Respect the publisher's terms and access limits.

Keep raw files unchanged and reproducible: record their URL, download date and hash. Keep each value's source, reference period and parser version. Distinguish publication date, observation date and reference period.

Do not invent values. A missing filing is not a zero, an absent field is not "none", and a company missing from a snapshot is not struck off unless the source says so.

## Personal data

Registry and fiscal data are about companies, but some fields identify people (sole traders' names, representatives, phone numbers). Import only the fields a step needs. Representatives are not shown until the maintainer decides how (see the MVP plan's open decisions).

## CI and reporting

PR workflows use read-only tokens and pinned actions; never run PR code through pull_request_target with secrets. Do not place untrusted event text directly in shell commands. Ordinary CI needs no external secrets or production endpoints.

Built-in checks catch contract drift, credential files and private-key markers, and package audit issues. They are not a security assessment or secret scanner.

Report vulnerabilities privately to the maintainer, never in a public issue.
