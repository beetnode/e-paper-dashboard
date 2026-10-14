# GitHub Actions Workflows

Automated CI/CD workflows for building firmware, Docker images, and exporting 3D models.

## Workflows

- **`release.yml`** - Creates releases from `v*` git tags with all artifacts
- **`ci-cd.yml`** - Runs on push/PR to master; builds only the components whose files changed
- **`firmware-build.yml`** - Builds ESP32 firmware
- **`dotnet-unit-tests.yml`** - Runs the .NET unit tests, including PostgreSQL integration tests
- **`docker-build.yml`** - Builds multi-arch Docker images (amd64/arm64) for the standalone app and the Home Assistant add-on
- **`playwright-component-build.yml`** - Builds the optional rendering component archives
- **`firmware-release.yml`** - Publishes a firmware release for `firmware-v*` tags
- **`freecad-export.yml`** - Exports STL files from FreeCAD

## Versions

The application, the firmware and the rendering component are versioned independently; bump only what changed.

| Component | Version source | Release tag |
|---|---|---|
| Application and add-on | `<Version>` in `software/EPaperDashboard/EPaperDashboard.csproj` | `v<version>` |
| Firmware | `FIRMWARE_VERSION` in `firmware/src/version.h` | `firmware-v<version>` |
| Rendering component | `RenderingComponentInfo.Version` in `software/RenderingComponentProtocol.cs` | `rendering-v<version>`, published by application releases |

Bump `RenderingComponentInfo.Version` for every change to the component payload: worker code, the Playwright package, the
browser or the native library collection. Also bump `RenderingComponentInfo.ProtocolVersion` when the request or response
contract changes incompatibly. The application accepts any installed component speaking its protocol and replaces it in the
background with the release it was built with.

## Creating an Application Release

```bash
git tag v0.6.0
git push origin v0.6.0
```

The release workflow automatically:
- Builds the Docker images, the rendering component and the STL files
- Publishes the rendering component release `rendering-v<component version>` before the application image, unless that version was already published by an earlier application release
- Creates a GitHub release with all files attached; it is marked latest
- Pushes `izdevdigital/e-paper-dashboard` and `izdevdigital/izboard-addon` images to Docker Hub

## Creating a Firmware Release

Bump `FIRMWARE_VERSION`, merge, then tag the commit; the workflow fails when the tag does not match:

```bash
git tag firmware-v0.5.1
git push origin firmware-v0.5.1
```

The firmware release carries `firmware-<version>.bin` (for OTA) and a zip with the ELF file. It is not marked latest. Servers
offer devices the highest `firmware-v*` release; application releases up to v0.5.0, which still carried firmware, remain
candidates.

## Build Numbers

Builds are versioned `<Version>.<build>`, where `<build>` is the commit count of the built commit (`git rev-list --count HEAD`). It only grows along master and is the same for every workflow, so a release tagged on a master commit gets the same build number as that commit's dev build. The workflows check out full history (`fetch-depth: 0`) to compute it.

Master builds publish the rendering component to a single `rendering-latest` release that is deleted and recreated on every build, like the `dev` release, and the `latest` images install from it. Only release builds create version-addressed `rendering-v<component version>` component releases.

Pull request builds count the temporary merge commit GitHub creates for the PR, so their build number can repeat a master build's number or be skipped by master later. They never publish images, component releases, or the dev release, so the numbers do not collide with published builds; do not distribute PR build artifacts as versioned builds.

## Setup

**Required - Docker Hub:** Add repository secrets to push Docker images:
1. Go to Repository Settings → Secrets and Variables → Actions
2. Add secrets:
   - `DOCKERHUB_USERNAME` - Your Docker Hub username
   - `DOCKERHUB_TOKEN` - Your Docker Hub access token

## Usage

### Download Artifacts
- **Releases**: [github.com/beetnode/izBoard/releases](https://github.com/beetnode/izBoard/releases)
- **Actions runs**: Actions tab → Select workflow run → Artifacts

### Docker Images
```bash
docker pull izdevdigital/e-paper-dashboard:0.5.0
docker pull izdevdigital/e-paper-dashboard:latest
docker pull izdevdigital/izboard-addon:0.5.0
```

### Flash Firmware
```bash
esptool.py --port /dev/ttyUSB0 write_flash 0x10000 firmware-{version}.bin
```
