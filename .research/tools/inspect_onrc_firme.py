"""Profile an ONRC OD_FIRME.CSV snapshot: structure, identifiers, dates, forms, duplicates, geography text.

Usage: python3 -I .research/tools/inspect_onrc_firme.py <OD_FIRME.CSV>
Streams the file; memory grows only with the number of distinct registry numbers.
"""

import collections
import hashlib
import json
import re
import sys
from datetime import date

CUI_KEY = "753217532"
HEADER = ["DENUMIRE", "CUI", "COD_INMATRICULARE", "DATA_INMATRICULARE", "EUID", "FORMA_JURIDICA", "ADR_TARA",
          "ADR_JUDET", "ADR_LOCALITATE", "ADR_DEN_STRADA", "ADR_NR_STRADA", "ADR_BLOC", "ADR_SCARA", "ADR_ETAJ",
          "ADR_APARTAMENT", "ADR_COD_POSTAL", "ADR_SECTOR", "ADR_COMPLETARE", "WEB", "TARA_FIRMA_MAMA"]


def cui_state(value):
    value = value.strip()
    if not value:
        return "missing"
    if not value.isdigit():
        return "non_numeric"
    if int(value) == 0:
        return "zero"
    digits = value.lstrip("0")
    if len(digits) > 10:
        return "too_long"
    if len(digits) < 2:
        return "too_short"
    body, check = digits[:-1].rjust(9, "0"), int(digits[-1])
    total = sum(int(a) * int(b) for a, b in zip(body, CUI_KEY)) * 10
    expected = total % 11 % 10
    return "valid" if expected == check else "bad_checksum"


def date_state(value):
    match = re.fullmatch(r"(\d{2})/(\d{2})/(\d{4})", value.strip())
    if not match:
        return "missing" if not value.strip() else "bad_format", None
    try:
        parsed = date(int(match[3]), int(match[2]), int(match[1]))
    except ValueError:
        return "impossible", None
    return "valid", parsed


def main(path):
    digest = hashlib.sha256()
    counters = collections.defaultdict(collections.Counter)
    rows_by_key = collections.Counter()
    first_row_by_key = {}
    conflicting = set()
    years = collections.Counter()
    width_errors = 0
    rows = 0
    size = 0
    with open(path, "rb") as stream:
        header_line = stream.readline()
        digest.update(header_line)
        size += len(header_line)
        bom = header_line.startswith(b"\xef\xbb\xbf")
        header = header_line.decode("utf-8-sig").rstrip("\r\n").split("^")
        if header != HEADER:
            raise SystemExit(f"unexpected header: {header}")
        for raw in stream:
            digest.update(raw)
            size += len(raw)
            rows += 1
            try:
                line = raw.decode("utf-8")
            except UnicodeDecodeError:
                counters["encoding"]["invalid_utf8"] += 1
                line = raw.decode("utf-8", "replace")
            if line.endswith("\r\n"):
                counters["line_ending"]["crlf"] += 1
            fields = line.rstrip("\r\n").split("^")
            if len(fields) != len(HEADER):
                width_errors += 1
                continue
            row = dict(zip(HEADER, fields))
            counters["cui"][cui_state(row["CUI"])] += 1
            state, parsed = date_state(row["DATA_INMATRICULARE"])
            counters["registration_date"][state] += 1
            if parsed:
                years[parsed.year] += 1
            counters["legal_form"][row["FORMA_JURIDICA"].strip() or "(blank)"] += 1
            counters["country"][row["ADR_TARA"].strip() or "(blank)"] += 1
            counters["county"][row["ADR_JUDET"].strip() or "(blank)"] += 1
            counters["web"]["present" if row["WEB"].strip() else "blank"] += 1
            counters["parent_country"]["present" if row["TARA_FIRMA_MAMA"].strip() else "blank"] += 1
            counters["name"]["blank" if not row["DENUMIRE"].strip() else "present"] += 1
            locality = row["ADR_LOCALITATE"].strip()
            shape = ("blank" if not locality else "placeholder" if locality.startswith("***")
                     else "sector" if re.match(r"Bucureşti Sectorul \d$|București Sectorul \d$", locality)
                     else "with_parent" if ", " in locality else "single")
            counters["locality_shape"][shape] += 1
            euid = row["EUID"].strip()
            key = row["COD_INMATRICULARE"].strip()
            counters["euid"]["matches_registry" if euid == "ROONRC." + key else ("blank" if not euid else "differs")] += 1
            if re.search(r"[ţşŢŞ]", row["DENUMIRE"] + locality + row["ADR_JUDET"]):
                counters["cedilla_letters"]["rows"] += 1
            rows_by_key[key] += 1
            fingerprint = hashlib.blake2b(line.encode("utf-8"), digest_size=8).digest()
            previous = first_row_by_key.setdefault(key, fingerprint)
            if previous != fingerprint:
                conflicting.add(key)
    repeated = sum(1 for count in rows_by_key.values() if count > 1)
    report = {
        "file": path.rsplit("/", 1)[-1],
        "bytes": size,
        "sha256": digest.hexdigest(),
        "utf8_bom": bom,
        "delimiter": "^",
        "rows": rows,
        "width_errors": width_errors,
        "distinct_registry_numbers": len(rows_by_key),
        "registry_numbers_with_repeated_rows": repeated,
        "registry_numbers_with_differing_rows": len(conflicting),
        "registration_years": {"min": min(years), "max": max(years)} if years else None,
    }
    for name, counter in counters.items():
        report[name] = dict(counter.most_common(60 if name in {"county", "legal_form"} else 15))
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main(sys.argv[1])
