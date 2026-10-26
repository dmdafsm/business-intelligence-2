"""Run every import in order inside the Docker stack, then rebuild the search tables.

The ANAF status harvest (`anaf harvest`, about 8 hours of web service calls) is not part of it; start it separately.

Each step is idempotent: an imported file is skipped, so the script can be rerun after a failure. The raw files must be
under .research/raw (see the import guides); the container sees them read-only at /data/raw.
"""

import subprocess
import sys
import time


RAW = "/data/raw"
STEPS = [
    ["registry", "import", "onrc-n-caen-2026-10-19", f"{RAW}/onrc-2026-10-19/N_CAEN.CSV"],
    ["registry", "import", "onrc-n-stare-firma-2026-10-19", f"{RAW}/onrc-2026-10-19/N_STARE_FIRMA.CSV"],
    ["registry", "import", "ins-siruta-s1-2024", f"{RAW}/siruta-s1-2024/siruta_s1_2024.xlsx"],
    ["registry", "import", "onrc-firme-2026-10-19", f"{RAW}/onrc-2026-10-19/OD_FIRME.CSV"],
    ["registry", "import", "onrc-stare-firma-2026-10-19", f"{RAW}/onrc-2026-10-19/OD_STARE_FIRMA.CSV"],
    ["registry", "import", "onrc-caen-autorizat-2026-10-19", f"{RAW}/onrc-2026-10-19/OD_CAEN_AUTORIZAT.CSV"],
    ["registry", "import", "onrc-reprezentanti-legali-2026-10-19", f"{RAW}/onrc-2026-10-19/od_reprezentanti_legali.csv"],
    ["registry", "import", "onrc-reprezentanti-if-2026-10-19", f"{RAW}/onrc-2026-10-19/od_reprezentanti_if.csv"],
    ["registry", "history", RAW],
    ["financials", "import-all", RAW],
    ["fiscal", "import-all", RAW],
    ["procurement", "import-all", RAW],
    ["funds", "import-all", RAW],
    ["licences", "import-all", RAW],
    ["registry", "import", "boundaries-ancpi-unitate-administrativa-judet", f"{RAW}/boundaries/ancpi-2024/unitate_administrativa_judet.zip"],
    ["registry", "import", "boundaries-ancpi-unitate-administrativa-uat", f"{RAW}/boundaries/ancpi-2024/unitate_administrativa_uat.zip"],
    ["search", "build"],
]


def main():
    for step in STEPS:
        started = time.monotonic()
        print(f"RUN: {' '.join(step)}", flush=True)
        result = subprocess.run(["docker", "compose", "--profile", "import", "run", "--rm", "--no-TTY", "import", *step], check=False)
        print(f"{'DONE' if result.returncode == 0 else 'FAIL'}: {' '.join(step)} in {time.monotonic() - started:.0f} s", flush=True)
        if result.returncode != 0:
            return result.returncode
    return 0


if __name__ == "__main__":
    sys.exit(main())
