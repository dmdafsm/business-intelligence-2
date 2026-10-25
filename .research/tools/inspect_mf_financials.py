"""Profile the downloaded MF financial statement files: dictionary labels and data layout per year and family.

Usage: python3 -I .research/tools/inspect_mf_financials.py <mf-financials-dir> > report.json
"""

import collections
import json
import pathlib
import re
import sys


def decode(raw):
    for encoding in ("utf-8", "cp1250"):
        try:
            return raw.decode(encoding), encoding
        except UnicodeDecodeError:
            continue
    return raw.decode("latin-1"), "latin-1"


def dictionary(path):
    text, encoding = decode(path.read_bytes())
    rows = [line.rstrip("\r\n").split(";") for line in text.lstrip("﻿").splitlines() if line.strip()]
    return encoding, [(row[0].strip(), row[1].strip() if len(row) > 1 else "") for row in rows]


def data(path):
    widths = collections.Counter()
    blank_cells = zero_cells = negative = decimals = rows = 0
    header = None
    encoding = "utf-8"
    cuis = set()
    duplicate_cui = 0
    with open(path, "rb") as stream:
        for number, raw in enumerate(stream):
            line, encoding = decode(raw)
            line = line.lstrip("﻿").rstrip("\r\n")
            if not line:
                continue
            fields = [field.strip() for field in line.split(",")]
            if number == 0 and not fields[0].isdigit():
                header = fields
                continue
            rows += 1
            widths[len(fields)] += 1
            if fields[0] in cuis:
                duplicate_cui += 1
            cuis.add(fields[0])
            for value in fields[2:]:
                if value == "":
                    blank_cells += 1
                elif value in ("0", "0.00", "0.0"):
                    zero_cells += 1
                if value.startswith("-"):
                    negative += 1
                if re.search(r"[.]\d", value):
                    decimals += 1
    return {"header": header, "rows": rows, "widths": dict(widths), "blank_cells": blank_cells, "zero_cells": zero_cells,
            "negative_cells": negative, "decimal_cells": decimals, "duplicate_cui_rows": duplicate_cui, "encoding": encoding}


def main(root):
    report = {}
    for directory in sorted(pathlib.Path(root).iterdir()):
        for dict_path in sorted(directory.glob("*-dict-*")):
            family = dict_path.name.split("-")[0]
            data_path = next(directory.glob(f"{family}-data-*"))
            encoding, labels = dictionary(dict_path)
            report[f"{directory.name}/{family}"] = {"dictionary_encoding": encoding, "labels": labels, "data": data(data_path)}
    print(json.dumps(report, ensure_ascii=False, indent=1))


if __name__ == "__main__":
    main(sys.argv[1])
