"""Meaningful release gates: no mixed identity, future fixture, damaged payload or path escape."""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("prepare_release", Path(__file__).with_name("prepare-release.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ReleaseValidation(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.artifacts = self.root / "artifacts"
        self.installer = self.artifacts / "installer"
        self.installer.mkdir(parents=True)
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            archive.writestr("LICENSE", "Test license")
        data = stream.getvalue()
        (self.artifacts / "GuideDeck-windows-x64.zip").write_bytes(data)
        (self.artifacts / "SHA256SUMS.txt").write_text(hashlib.sha256(data).hexdigest() + "  GuideDeck-windows-x64.zip\n")
        (self.installer / "GuideDeck.Desktop-win-x64-Setup.exe").write_bytes(b"MZtest setup")
        self.package = self.installer / "GuideDeck.Desktop-0.5.0-full.nupkg"
        self.package.write_bytes(b"test package")
        self.asset = {"PackageId": "GuideDeck.Desktop", "Version": "0.5.0", "Type": "Full", "FileName": self.package.name,
                      "SHA256": hashlib.sha256(self.package.read_bytes()).hexdigest(), "Size": self.package.stat().st_size}
        with zipfile.ZipFile(self.artifacts / "GuideDeck.msix", "w") as archive:
            archive.writestr("AppxManifest.xml", '<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"><Identity Version="0.5.0.0"/></Package>')
            archive.writestr("Companion/GuideDeck.exe", b"MZtest executable")
        self.sha = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()

    def tearDown(self):
        self.temp.cleanup()

    def prepare(self, assets=None):
        (self.installer / "releases.win-x64.json").write_text(json.dumps({"Assets": assets if assets is not None else [self.asset]}))
        module.prepare(self.root, self.root / "out", self.sha, "123", "v0.5.0")

    def test_valid_same_build(self):
        self.prepare()
        self.assertEqual((self.root / "out" / self.package.name).read_bytes(), b"test package")
        self.assertIn("GuideDeck-Setup.exe", (self.root / "out/SHA256SUMS.txt").read_text())

    def test_same_size_corruption(self):
        self.package.write_bytes(b"fake package")
        with self.assertRaisesRegex(ValueError, "checksum mismatch"):
            self.prepare()

    def test_future_fixture(self):
        self.asset["Version"] = "0.5.1"
        with self.assertRaisesRegex(ValueError, "identity/version"):
            self.prepare()

    def test_wrong_app(self):
        self.asset["PackageId"] = "OtherApp"
        with self.assertRaisesRegex(ValueError, "identity/version"):
            self.prepare()

    def test_path_escape(self):
        self.asset["FileName"] = "../GuideDeck.Desktop-0.5.0-full.nupkg"
        with self.assertRaisesRegex(ValueError, "package path"):
            self.prepare()

    def test_not_full_package(self):
        self.asset["Type"] = "Delta"
        with self.assertRaisesRegex(ValueError, "full update"):
            self.prepare()

    def test_mixed_feed(self):
        with self.assertRaisesRegex(ValueError, "exactly one"):
            self.prepare([self.asset, dict(self.asset, Version="0.5.1")])

    def test_invalid_installer(self):
        next(self.installer.glob("*Setup.exe")).write_bytes(b"invalid")
        with self.assertRaisesRegex(ValueError, "Invalid installer"):
            self.prepare()


if __name__ == "__main__":
    unittest.main()
