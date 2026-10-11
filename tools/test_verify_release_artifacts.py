import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('verify_artifacts', Path(__file__).with_name('verify-release-artifacts.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ArtifactValidationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.version, self.revision = '1.1.6', 'a' * 40

    def fixture(self, flavor='win-x64'):
        self.flavor = flavor
        self.channel = {'win-x64': 'win', 'osx-x64': 'osx', 'osx-arm64': 'osx-arm64'}[flavor]
        data = b'reviewed package bytes'
        self.package = 'FreeRealmsLegacyLauncher-1.1.6-full.nupkg'
        (self.root / self.package).write_bytes(data)
        self.feed = dict(Assets=[dict(PackageId='FreeRealmsLegacyLauncher', Version=self.version,
                         Type='Full', FileName=self.package, Size=len(data), SHA256=hashlib.sha256(data).hexdigest())])
        self.feed_path = self.root / f'releases.{self.channel}.json'
        self.feed_path.write_text(json.dumps(self.feed))
        listing = [dict(Type='Full', RelativeFileName=self.package), dict(Type='Portable', RelativeFileName='Portable.zip')]
        (self.root / 'Portable.zip').write_bytes(b'portable')
        if flavor == 'win-x64':
            listing.append(dict(Type='Installer', RelativeFileName='Setup.exe'))
            (self.root / 'Setup.exe').write_bytes(b'setup')
            (self.root / 'RELEASES').write_text('legacy feed')
        else:
            (self.root / f'FreeRealmsLegacyLauncher-{self.version}.dmg').write_bytes(b'disk image')
            (self.root / f'RELEASES-{self.channel}').write_text('legacy feed')
        (self.root / f'assets.{self.channel}.json').write_text(json.dumps(listing))

    def verify(self):
        return module.verify(self.root, self.version, self.channel, self.flavor, self.revision)

    def test_windows_receipt_binds_version_channel_source_and_all_files(self):
        self.fixture()
        result = self.verify()
        self.assertEqual(result['source_revision'], self.revision)
        self.assertEqual(result['channel'], 'win')
        self.assertEqual({f['name'] for f in result['files']}, {p.name for p in self.root.iterdir()})
        self.assertEqual(result['version'], '1.1.6')

    def test_both_mac_architectures_keep_separate_channels(self):
        for flavor in ('osx-x64', 'osx-arm64'):
            with self.subTest(flavor=flavor):
                with tempfile.TemporaryDirectory() as directory:
                    self.root = Path(directory)
                    self.fixture(flavor)
                    self.assertEqual(self.verify()['channel'], self.channel)

    def test_modified_package_rejected(self):
        self.fixture()
        (self.root / self.package).write_bytes(b'modified')
        with self.assertRaisesRegex(ValueError, 'hash and size'):
            self.verify()

    def test_incorrect_release_version_rejected(self):
        self.fixture()
        self.feed['Assets'][0]['Version'] = '1.1.5'
        self.feed_path.write_text(json.dumps(self.feed))
        with self.assertRaisesRegex(ValueError, 'identity or version'):
            self.verify()

    def test_stale_other_channel_feed_rejected(self):
        self.fixture()
        (self.root / 'releases.osx.json').write_text('{}')
        with self.assertRaisesRegex(ValueError, 'selected channel'):
            self.verify()

    def test_architecture_channel_mismatch_rejected(self):
        self.fixture('osx-arm64')
        self.channel = 'osx'
        with self.assertRaisesRegex(ValueError, 'flavor, channel'):
            self.verify()

    def test_feed_path_escape_rejected(self):
        self.fixture()
        self.feed['Assets'][0]['FileName'] = '../outside.nupkg'
        self.feed_path.write_text(json.dumps(self.feed))
        with self.assertRaisesRegex(ValueError, 'basename'):
            self.verify()

    def test_missing_downloadable_artifact_rejected(self):
        self.fixture()
        (self.root / 'Setup.exe').unlink()
        with self.assertRaisesRegex(ValueError, 'Missing or unsafe'):
            self.verify()

    def test_missing_mac_dmg_rejected(self):
        self.fixture('osx-x64')
        (self.root / f'FreeRealmsLegacyLauncher-{self.version}.dmg').unlink()
        with self.assertRaisesRegex(ValueError, 'Missing or unsafe'):
            self.verify()

    def test_repeated_verification_ignores_existing_receipt(self):
        self.fixture()
        before = self.verify()
        (self.root / 'build-receipt.json').write_text(json.dumps(before))
        self.assertEqual(self.verify(), before)


if __name__ == '__main__':
    unittest.main()
