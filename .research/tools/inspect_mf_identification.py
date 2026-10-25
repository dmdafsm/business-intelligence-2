"""Profile the MF taxpayer identification files of one release, without printing taxpayer values.

Usage: python3 -I .research/tools/inspect_mf_identification.py <file-or-zip> [...] > profile.json
Streams each file (zip entries too). Reports encoding, delimiter, header, field-count distribution, blanks per column,
value shapes, top values of categorical columns only, CUI checksum states and how often a CUI repeats.
"""

import collections
import csv
import io
import json
import re
import sys
import zipfile

CATEGORICAL = re.compile(r"^(TIP_|STARE|TVA|IMP|ACCIZE|CONT|JUDET$|SECTOR|SECT$|BILANTURI$)", re.IGNORECASE)
CUI_COLUMNS = ("COD_FISCAL", "CUI", "COD")
WEIGHTS = (7, 5, 3, 2, 1, 7, 5, 3, 2)


def shape(value):
    return re.sub(r"[^\W\d_]", "a", re.sub(r"\d", "9", value))[:24]


def cui_state(value):
    digits = value.strip()
    if not digits:
        return "missing"
    if not digits.isdigit():
        return "non-numeric"
    if len(digits) == 13:
        return "personal-code-length"
    if len(digits) < 2 or len(digits) > 10:
        return "bad-length"
    body, check = digits[:-1].rjust(9, "0"), int(digits[-1])
    total = sum(int(d) * w for d, w in zip(body, WEIGHTS)) * 10 % 11
    return "valid" if total % 10 == check else "bad-checksum"


def decode(raw):
    try:
        return raw.decode("utf-8"), "utf-8"
    except UnicodeDecodeError:
        pass
    try:
        return raw.decode("cp1250"), "cp1250"
    except UnicodeDecodeError:
        return raw.decode("utf-8", errors="replace"), "invalid"


def streams(paths):
    for path in paths:
        if path.endswith(".zip"):
            with zipfile.ZipFile(path) as archive:
                for info in archive.infolist():
                    if not info.is_dir():
                        with archive.open(info) as stream:
                            yield f"{path}!{info.filename}", info.file_size, stream
        else:
            with open(path, "rb") as stream:
                yield path, None, stream


def records(stream, delimiter):
    """Yields (fields, encoding, trailing) per record; comma files are quoted CSV, the others split on the delimiter."""
    lines = (decode(raw) for raw in stream)
    if delimiter == ",":
        encodings = collections.Counter()

        def text():
            for line, encoding in lines:
                encodings[encoding] += 1
                yield line
        for fields in csv.reader(text()):
            yield fields, None, False
        records.encodings = encodings
        return
    for line, encoding in lines:
        line = line.rstrip("\r\n")
        if line:
            yield line.split(delimiter), encoding, line.endswith(delimiter)


def profile(name, size, stream):
    first = stream.readline()
    text, _ = decode(first.lstrip(b"\xef\xbb\xbf"))
    header_line = text.rstrip("\r\n")
    delimiter = max("^|;,\t", key=header_line.count)
    first_fields = next(csv.reader([header_line])) if delimiter == "," else header_line.split(delimiter)
    has_header = not first_fields[0].strip().strip('"').isdigit()
    header = [column.strip().strip('"') for column in first_fields] if has_header else [f"c{i}" for i in range(len(first_fields))]
    widths = collections.Counter()
    encodings = collections.Counter()
    blanks = collections.Counter()
    shapes = {column: collections.Counter() for column in header}
    top = {column: collections.Counter() for column in header if CATEGORICAL.match(column)}
    cui_column = next((column for column in header if column.upper() in CUI_COLUMNS), None if has_header else "c0")
    cui_index = header.index(cui_column) if cui_column else None
    cui_states = collections.Counter()
    seen = collections.Counter()
    rows = 0
    trailing_delimiter = 0
    body = stream if has_header else _prepend(first, stream)
    for fields, encoding, trailing in records(body, delimiter):
        if encoding:
            encodings[encoding] += 1
        rows += 1
        widths[len(fields)] += 1
        trailing_delimiter += trailing
        if len(fields) < len(header):
            continue
        for column, value in zip(header, fields):
            value = value.strip().strip('"').strip()
            if not value:
                blanks[column] += 1
                continue
            if len(shapes[column]) < 50 or shape(value) in shapes[column]:
                shapes[column][shape(value)] += 1
            if column in top and (len(top[column]) < 60 or value in top[column]):
                top[column][value] += 1
        if cui_index is not None:
            value = fields[cui_index].strip().strip('"')
            cui_states[cui_state(value)] += 1
            if value:
                seen[value] += 1
    if delimiter == ",":
        encodings = getattr(records, "encodings", encodings)
    repeats = collections.Counter(min(count, 5) for count in seen.values())
    return {
        "file": name,
        "uncompressed_bytes": size,
        "delimiter": delimiter,
        "has_header": has_header,
        "header": header if has_header else len(header),
        "rows": rows,
        "field_counts": dict(widths.most_common(8)),
        "rows_with_trailing_delimiter": trailing_delimiter,
        "line_encodings": dict(encodings),
        "blank_per_column": {column: blanks[column] for column in header},
        "shapes": {column: dict(counter.most_common(6)) for column, counter in shapes.items()},
        "top_values": {column: dict(counter.most_common(12)) for column, counter in top.items()},
        "cui_column": cui_column,
        "cui_states": dict(cui_states),
        "distinct_cuis": len(seen),
        "cui_repeat_histogram": {str(key): value for key, value in sorted(repeats.items())},
    }


def _prepend(first, stream):
    yield first.lstrip(b"\xef\xbb\xbf")
    yield from stream


def main(paths):
    json.dump([profile(*item) for item in streams(paths)], sys.stdout, indent=1, ensure_ascii=False)
    sys.stdout.write("\n")


if __name__ == "__main__":
    main(sys.argv[1:])
