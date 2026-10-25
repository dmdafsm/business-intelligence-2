"""Download the files of a manifest into .research/raw and record size and SHA-256 of what arrived.

Usage: python3 -I .research/tools/download_manifest.py <manifest.json> <target-dir> <result.json>
Manifest entries need "url" and "path" (relative to the target directory). Existing files are hashed, not refetched.
Only data.gov.ro is accepted, always over https.
"""

import hashlib
import json
import pathlib
import sys
import time
import urllib.parse
import urllib.request


def fetch(url, destination):
    parts = urllib.parse.urlsplit(url)
    if parts.scheme == "http" and parts.hostname == "data.gov.ro":
        # Older catalog entries still list http links; the same host serves them over https.
        url = urllib.parse.urlunsplit(parts._replace(scheme="https"))
        parts = urllib.parse.urlsplit(url)
    if parts.scheme != "https" or parts.hostname != "data.gov.ro":
        raise ValueError(f"refusing {url}")
    temporary = destination.with_suffix(destination.suffix + ".part")
    for attempt in range(4):
        try:
            with urllib.request.urlopen(url, timeout=120) as response, open(temporary, "wb") as output:
                while chunk := response.read(1 << 20):
                    output.write(chunk)
            temporary.replace(destination)
            return
        except Exception as error:  # network errors are retried with backoff
            print(f"retry {attempt + 1} for {url}: {error}", flush=True)
            time.sleep(5 * (attempt + 1))
    raise RuntimeError(f"failed {url}")


def main(manifest_path, target, result_path):
    entries = json.load(open(manifest_path, encoding="utf-8"))
    root = pathlib.Path(target)
    results = []
    for entry in entries:
        destination = root / entry["path"]
        destination.parent.mkdir(parents=True, exist_ok=True)
        if not destination.exists():
            fetch(entry["url"], destination)
        digest = hashlib.sha256()
        with open(destination, "rb") as stream:
            while chunk := stream.read(1 << 20):
                digest.update(chunk)
        results.append({**entry, "bytes": destination.stat().st_size, "sha256": digest.hexdigest()})
        print(f"{destination.stat().st_size:>12} {entry['path']}", flush=True)
    json.dump(results, open(result_path, "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    open(result_path, "a", encoding="utf-8").write("\n")


if __name__ == "__main__":
    main(*sys.argv[1:4])
