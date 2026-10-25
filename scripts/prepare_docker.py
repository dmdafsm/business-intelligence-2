"""Create local Docker inputs without replacing credentials or printing them."""

import os
from pathlib import Path
import secrets
import sys


ROOT = Path(__file__).resolve().parents[1]


def prepare(root):
    directory = root / ".artifacts/docker"
    if directory.is_symlink():
        raise ValueError("Docker input directory must not be a symlink")
    directory.mkdir(parents=True, exist_ok=True, mode=0o700)
    directory.chmod(0o700)
    password = directory / "postgres-password"
    if password.is_symlink():
        raise ValueError("Docker password input must not be a symlink")
    try:
        descriptor = os.open(password, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o444)
    except FileExistsError:
        if not password.is_file() or not 1 <= password.stat().st_size <= 1024:
            raise ValueError("Existing Docker password input is invalid; it was not replaced") from None
    else:
        with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
            stream.write(secrets.token_urlsafe(48) + "\n")
    # Bind-mounted secrets must be readable by the non-root container UID.
    # The enclosing host directory is private (0700), so other host users cannot traverse it.
    password.chmod(0o444)
    reports = directory / "verification"
    if reports.is_symlink():
        raise ValueError("Docker reports directory must not be a symlink")
    reports.mkdir(exist_ok=True)
    # Container UID differs from the host UID; the private parent protects reports on the host.
    reports.chmod(0o777)


def main():
    try:
        prepare(ROOT)
    except (OSError, ValueError) as error:
        print(f"Docker input preparation failed: {error}", file=sys.stderr)
        return 1
    print("Docker inputs ready under ignored .artifacts/docker; existing credentials preserved")
    return 0


if __name__ == "__main__":
    sys.exit(main())
