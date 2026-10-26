"""Download a list of files (url, target path relative to .research/raw) and record size and SHA-256.

Usage: python3 -I download_list.py <list.json> <manifest-out.json>
The list is a JSON array of {"url": ..., "path": ..., ...}. Existing files are kept and only hashed. Failures are
recorded and the run continues.
"""

import hashlib
import json
import pathlib
import sys
import time
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1] / "raw"


def fetch(url, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    partial = target.with_suffix(target.suffix + ".part")
    request = urllib.request.Request(url, headers={"User-Agent": "business-intelligence-2 research download"})
    with urllib.request.urlopen(request, timeout=120) as response, open(partial, "wb") as out:
        while chunk := response.read(1 << 20):
            out.write(chunk)
    partial.rename(target)


def main():
    items = json.load(open(sys.argv[1], encoding="utf-8"))
    results = []
    for item in items:
        target = ROOT / item["path"]
        if target.resolve().parent != (ROOT / item["path"]).parent.resolve() or ".." in item["path"]:
            raise ValueError(item["path"])
        error = None
        if not target.exists():
            for attempt in range(3):
                try:
                    fetch(item["url"], target)
                    error = None
                    break
                except Exception as exception:  # recorded, the run continues
                    error = f"{type(exception).__name__}: {exception}"
                    time.sleep(5 * (attempt + 1))
        entry = dict(item)
        if target.exists():
            digest = hashlib.sha256()
            with open(target, "rb") as stream:
                while chunk := stream.read(1 << 20):
                    digest.update(chunk)
            entry.update(bytes=target.stat().st_size, sha256=digest.hexdigest())
        else:
            entry["error"] = error
        results.append(entry)
        print(f"{'OK ' if 'sha256' in entry else 'ERR'} {entry.get('bytes', 0):>12} {item['path']} {entry.get('error') or ''}", flush=True)
        with open(sys.argv[2], "w", encoding="utf-8") as out:
            json.dump(results, out, indent=1, ensure_ascii=False)
            out.write("\n")


if __name__ == "__main__":
    main()
