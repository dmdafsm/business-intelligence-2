"""Local Docker credentials must be private and survive repeated setup."""

from pathlib import Path
import stat
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import prepare_docker


class DockerInputTests(unittest.TestCase):
    def test_repeated_setup_preserves_credentials_and_private_parent(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            prepare_docker.prepare(root)
            target = root / ".artifacts/docker"
            password = (target / "postgres-password").read_bytes()
            self.assertGreaterEqual(len(password), 48)
            prepare_docker.prepare(root)
            self.assertEqual(password, (target / "postgres-password").read_bytes())
            self.assertEqual(0o700, stat.S_IMODE(target.stat().st_mode))
            self.assertEqual(0o444, stat.S_IMODE((target / "postgres-password").stat().st_mode))

    def test_invalid_existing_secret_is_preserved_and_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            target = root / ".artifacts/docker"
            target.mkdir(parents=True)
            password = target / "postgres-password"
            password.write_bytes(b"")
            with self.assertRaises(ValueError):
                prepare_docker.prepare(root)
            self.assertEqual(b"", password.read_bytes())

    def test_secret_symlink_is_never_followed(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            target = root / ".artifacts/docker"
            target.mkdir(parents=True)
            other = root / "untouched"
            other.write_text("keep", encoding="utf-8")
            (target / "postgres-password").symlink_to(other)
            with self.assertRaises(ValueError):
                prepare_docker.prepare(root)
            self.assertEqual("keep", other.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
