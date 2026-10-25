"""Install the pinned official PowerShell archive in the disposable verification image."""

import hashlib
from pathlib import Path
import sys
import tarfile
import urllib.request


def main():
    architecture, destination = sys.argv[1:]
    variants = {
        "arm64": ("arm64", "ba465efdfb0984c1c5dc5ed9eb0b79a7e91a1b52c45f99377058c0659b84d3f1"),
        "amd64": ("x64", "6896f9e3a6b0a354bf470835a96da76d2f801cd50dcf047ad7c4563bf93bf431"),
    }
    suffix, expected = variants[architecture]
    archive = Path("/tmp/powershell.tar.gz")
    url = f"https://github.com/PowerShell/PowerShell/releases/download/v7.6.7/powershell-7.6.7-linux-{suffix}.tar.gz"
    urllib.request.urlretrieve(url, archive)
    if hashlib.sha256(archive.read_bytes()).hexdigest() != expected:
        raise RuntimeError("PowerShell archive checksum mismatch")
    target = Path(destination)
    target.mkdir(parents=True, exist_ok=True)
    with tarfile.open(archive) as package:
        package.extractall(target, filter="data")
    (target / "pwsh").chmod(0o755)
    archive.unlink()


if __name__ == "__main__":
    main()
