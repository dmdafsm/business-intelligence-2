"""Profile the yearly ONRC snapshots in .research/raw/onrc-history with the same layout rules the importer uses.

Usage: python3 -I .research/tools/inspect_onrc_history.py <onrc-history-dir>
Rules: invalid UTF-8 bytes become U+FFFD; part files of one kind are read in order as one stream; header lines are skipped wherever they appear; a short line
continues the previous record's last field unless it starts a new record (addresses can contain line breaks); surrounding quotes are removed.
"""

import collections
import json
import pathlib
import re
import sys

KINDS = [("neradiate", "fara"), ("radiate", "fara"), ("neradiate", "cu"), ("radiate", "cu")]


def kind_of(name):
    name = name.lower().replace("_", "")
    struck = "neradiate" not in name
    office = "farasediu" not in name.replace("-", "") and "fara" not in name
    if "nomenclator" in name:
        return None
    return ("radiate" if struck else "neradiate", "cu" if office else "fara")


def starts_record(fields):
    number = fields[2].strip().strip('"') if len(fields) >= 3 else ""
    return len(number) > 3 and number[0] in "JFC" and number[1].isdigit()


def clean(fields, width):
    # 2022-2024 rows often end with an extra empty field (a trailing separator).
    while len(fields) > width and fields[-1].strip() == "":
        fields.pop()
    return [field.strip().strip('"').strip() for field in fields]


def records(paths, separator, width):
    held = None
    pending = None
    for path in paths:
        # 2022-2024 contain a few dozen fields cut inside a multi-byte character by the publisher.
        with open(path, encoding="utf-8-sig", errors="replace") as stream:
            for raw in stream:
                line = raw.rstrip("\r\n").lstrip("\ufeff")
                if line.startswith("DENUMIRE") or not line:
                    continue
                text = line if pending is None else pending + " " + line
                fields = text.split(separator)
                if len(fields) >= width:
                    pending = None
                    if held is not None:
                        yield clean(held, width)
                    held = fields
                elif pending is None and held is not None and not starts_record(fields):
                    held[-1] += " " + line  # an address broken across lines
                else:
                    pending = text
    if held is not None:
        yield clean(held, width)


def main(root):
    report = {}
    for directory in sorted(pathlib.Path(root).iterdir()):
        files = sorted(path for path in directory.iterdir() if path.suffix == ".csv")
        if any(path.name == "od_firme.csv" for path in files):
            report[directory.name] = "current OD_FIRME layout"
            continue
        sample = next(path for path in files if "cu" in str(kind_of(path.name)))
        first = open(sample, encoding="utf-8-sig").readline()
        separator = "^" if first.count("^") > first.count("|") else "|"
        result = {"separator": separator}
        for kind in KINDS:
            parts = [path for path in files if kind_of(path.name) == kind]
            header = next((open(path, encoding="utf-8-sig").readline().strip() for path in parts
                           if open(path, encoding="utf-8-sig").readline().startswith("DENUMIRE")), None)
            width = len(header.split(separator)) if header else (6 if kind[1] == "cu" else 4)
            counts = collections.Counter()
            keys = set()
            codes = collections.Counter()
            for fields in records(parts, separator, width):
                counts["rows"] += 1
                if len(fields) != width:
                    counts["width_errors"] += 1
                    continue
                keys.add(fields[2])
                status = fields[4] if header and "EUID" in header else fields[3]
                for code in filter(None, status.split(",")):
                    codes[code.strip()] += 1
                cui = fields[1]
                counts["cui_blank_or_zero" if cui.strip("0") == "" else "cui_long" if len(cui.lstrip("0")) > 10 else "cui_numeric" if cui.isdigit() else "cui_other"] += 1
            result["-".join(kind)] = {"files": [path.name for path in parts], "header": header, "width": width,
                                      **counts, "registry_numbers": len(keys), "top_codes": dict(codes.most_common(4))}
        report[directory.name] = result
    print(json.dumps(report, ensure_ascii=False, indent=1))


if __name__ == "__main__":
    main(sys.argv[1])
