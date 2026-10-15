# GitHub Actions Workflows

Automated CI/CD workflows for building firmware, Docker images, and exporting 3D models.

## Workflows

- **`release.yml`** - Creates releases from `v*` git tags with all artifacts
- **`ci-cd.yml`** - Runs on push/PR to master; builds only the components whose files changed
- **`firmware-build.yml`** - Builds ESP32 firmware
- **`dotnet-unit-tests.yml`** - Runs the .NET unit tests, including PostgreSQL integration tests
- **`docker-build.yml`** - Builds multi-arch Docker images (amd64/arm64) for the standalone app and the Home Assistant add-on
- **`playwright-component-build.yml`** - Builds the optional rendering component archives
- **`freecad-export.yml`** - Exports STL files from FreeCAD

## Creating a Release

Bump the version first; the tag must match it:
- `<Version>` in `software/EPaperDashboard/EPaperDashboard.csproj` and `software/EPaperDashboard.PlaywrightComponent/EPaperDashboard.PlaywrightComponent.csproj` (Docker image tags are read from the app project)
- `FIRMWARE_VERSION` in `firmware/src/version.h` (devices update when the latest release is newer than their firmware)

```bash
# Create and push a tag
git tag v0.5.0
git push origin v0.5.0
```

The release workflow automatically:
- Builds all artifacts (firmware, Docker images, STL files)
- Creates the `v<version>` GitHub release with the rendering component attached before the application images are pushed, so images never point at a missing component
- Completes that release with the firmware, STL files and release notes, and marks it latest
- Pushes `izdevdigital/e-paper-dashboard` and `izdevdigital/izboard-addon` images to Docker Hub

## Build Numbers

Builds are versioned `<Version>.<build>`, where `<build>` is the commit count of the built commit (`git rev-list --count HEAD`). It only grows along master and is the same for every workflow, so a release tagged on a master commit gets the same build number as that commit's dev build. The workflows check out full history (`fetch-depth: 0`) to compute it.

Master builds publish the rendering component to a single `rendering-latest` release that is deleted and recreated on every build, like the `dev` release, and the `latest` images install from it. Release builds attach the component to the application release instead; there are no separate component releases.

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
