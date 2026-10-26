# Security policy

## Reporting a vulnerability

Report vulnerabilities privately through GitHub: open the repository's **Security** tab and choose **Report a vulnerability**. Do not open a public issue, pull request or discussion for a suspected vulnerability.

Include what you found, the affected page, command or file, steps to reproduce and the impact you expect. Use synthetic data in the report; do not attach database dumps, credentials or personal data from the portal.

This is a project maintained by one person, without a bug bounty. Reports are acknowledged as soon as the maintainer can, and fixes are coordinated in the private advisory before anything is published.

## Supported versions

Only the current `main` branch is supported. There are no released versions to backport to.

## Scope

In scope:

- The web application: public pages, search exports and the operator pages for import jobs, including their token sign-in.
- The import commands and the import-jobs worker: handling of downloaded files, archives and imported text.
- Exposure of personal data beyond what the [security and data rules](docs/engineering/security-and-data.md) allow, such as representatives' birth dates or places of birth appearing on a page.
- The repository's build, CI workflow and container setup.

Out of scope:

- Wrong or outdated company data that matches the official source. The portal shows published data; corrections belong with the publisher.
- Vulnerabilities in the third-party sources themselves or in a deployment you run yourself with a changed configuration.
- Reports from automated scanners without a demonstrated impact.
