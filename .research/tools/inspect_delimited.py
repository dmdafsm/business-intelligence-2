"""Profile a '^'-delimited ONRC file: header, rows, width errors, per-column blanks, value shapes and top values.

Usage: python3 -I .research/tools/inspect_delimited.py <file> [key-column ...]
Streams the file. Key columns additionally report how many distinct values occur and how many repeat.
"""

import collections
import hashlib
import json
import re
import sys

TRACKED = 200_000  # distinct values tracked per column before only shapes are counted


def shape(value):
    return re.sub(r"[A-Za-z]", "a", re.sub(r"\d", "9", value))[:40]


def main(path, keys):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        first = stream.readline()
        digest.update(first)
        header = first.decode("utf-8-sig").rstrip("\r\n").split("^")
        blanks = collections.Counter()
        shapes = {name: collections.Counter() for name in header}
        values = {name: collections.Counter() for name in header}
        key_counts = {name: collections.Counter() for name in keys}
        rows = width_errors = 0
        for raw in stream:
            digest.update(raw)
            rows += 1
            fields = raw.decode("utf-8").rstrip("\r\n").split("^")
            if len(fields) != len(header):
                width_errors += 1
                continue
            for name, value in zip(header, fields):
                value = value.strip()
                if not value:
                    blanks[name] += 1
                    continue
                shapes[name][shape(value)] += 1
                if len(values[name]) < TRACKED or value in values[name]:
                    values[name][value] += 1
                if name in key_counts:
                    key_counts[name][value] += 1
    report = {"file": path.rsplit("/", 1)[-1], "sha256": digest.hexdigest(), "header": header, "rows": rows,
              "width_errors": width_errors, "columns": {}}
    for name in header:
        report["columns"][name] = {
            "blank": blanks[name],
            "distinct_tracked": len(values[name]),
            "shapes": dict(shapes[name].most_common(8)),
            "top": dict(values[name].most_common(12)),
        }
    for name, counter in key_counts.items():
        report["columns"][name]["distinct"] = len(counter)
        report["columns"][name]["repeated_values"] = sum(1 for count in counter.values() if count > 1)
        report["columns"][name]["max_per_value"] = max(counter.values()) if counter else 0
    print(json.dumps(report, ensure_ascii=False, indent=1))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2:])
