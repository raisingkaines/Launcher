"""Validate staged Velopack artifacts and record hashes; never uploads or installs."""
import hashlib
import json
from pathlib import Path
import re
import sys


def file_sha256(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def verify(directory, version, channel, flavor, revision):
    root = Path(directory)
    if not re.fullmatch(r'\d+\.\d+\.\d+', version):
        raise ValueError('Expected a stable three-part release version')
    channels = {'win-x64': 'win', 'osx-x64': 'osx', 'osx-arm64': 'osx-arm64'}
    if channels.get(flavor) != channel or not re.fullmatch(r'[0-9a-f]{40}', revision):
        raise ValueError('Unexpected flavor, channel or source revision')
    expected_feed = f'releases.{channel}.json'
    if [f.name for f in root.glob('releases.*.json')] != [expected_feed]:
        raise ValueError('Expected exactly the selected channel feed')
    feed = json.loads((root / expected_feed).read_text(encoding='utf-8-sig'))
    assets = feed['Assets']
    if len(assets) != 1 or assets[0]['Type'] != 'Full':
        raise ValueError('Fresh artifact build must contain exactly one full release')
    release = assets[0]
    if release['PackageId'] != 'FreeRealmsLegacyLauncher' or release['Version'] != version:
        raise ValueError('Wrong package identity or version')

    def local_file(name):
        if not name or '/' in name or '\\' in name or name in ('.', '..'):
            raise ValueError('Artifact filename must be a basename')
        path = root / name
        if not path.is_file() or path.is_symlink():
            raise ValueError(f'Missing or unsafe artifact: {name}')
        return path

    package = local_file(release['FileName'])
    digest = file_sha256(package)
    if package.stat().st_size != release['Size'] or digest != release['SHA256'].lower():
        raise ValueError('Full package does not match its feed hash and size')
    listings = json.loads((root / f'assets.{channel}.json').read_text(encoding='utf-8-sig'))
    for asset in listings:
        local_file(asset['RelativeFileName'])
    if not any(asset['Type'] == 'Full' and asset['RelativeFileName'] == package.name for asset in listings):
        raise ValueError('Asset listing omits the feed package')
    if not any(asset['Type'] == 'Portable' for asset in listings):
        raise ValueError('Portable artifact missing')
    if flavor == 'win-x64':
        if not any(asset['Type'] == 'Installer' for asset in listings):
            raise ValueError('Windows installer missing')
        local_file('RELEASES')
    else:
        local_file(f'FreeRealmsLegacyLauncher-{version}.dmg')
        local_file(f'RELEASES-{channel}')
    files = []
    for path in sorted(root.iterdir()):
        if path.name == 'build-receipt.json':
            continue
        if not path.is_file() or path.is_symlink():
            raise ValueError('Unexpected directory or symlink in release artifacts')
        files.append(dict(name=path.name, size=path.stat().st_size,
                          sha256=file_sha256(path)))
    return dict(source_revision=revision, version=version, flavor=flavor,
                channel=channel, files=files,
                acceptance='Artifact integrity only; not an install or gameplay test')


if __name__ == '__main__':
    if len(sys.argv) != 6:
        raise SystemExit('Usage: verify-release-artifacts.py DIRECTORY VERSION CHANNEL FLAVOR REVISION')
    receipt = verify(*sys.argv[1:])
    (Path(sys.argv[1]) / 'build-receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
    print(json.dumps(receipt, indent=2))
