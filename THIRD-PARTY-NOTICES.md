# Third-party notices

The application uses unmodified NuGet packages listed in Directory.Packages.props, with exact transitive versions in each packages.lock.json. Their licenses do not change this application's license.

| Component | Version | License | Source |
| --- | --- | --- | --- |
| Entity Framework Core | 10.0.13 | MIT | [dotnet/efcore](https://github.com/dotnet/efcore) |
| Npgsql Entity Framework Core provider | 10.0.3 | PostgreSQL License | [npgsql/efcore.pg](https://github.com/npgsql/efcore.pg) |
| ExcelDataReader | 3.9.0 | MIT | [ExcelDataReader/ExcelDataReader](https://github.com/ExcelDataReader/ExcelDataReader) |

Test-only packages (xUnit, Microsoft.NET.Test.Sdk, Microsoft.Playwright) are not distributed with the application.

## Material Icons

Icon paths in `src/BusinessIntelligence.Host/Components/Icons.cs` come from Google's Material Icons, Copyright Google LLC, under the [Apache License 2.0](licenses/Apache-2.0.txt). [Upstream](https://github.com/google/material-design-icons).

## Material Design

The visual design follows the public [Material 3](https://m3.material.io) guidelines and baseline color scheme. No Google code or font files are bundled.
