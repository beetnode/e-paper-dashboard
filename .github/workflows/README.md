# GitHub Actions Workflows

Automated CI/CD workflows for building firmware, Docker images, and exporting 3D models.

## Workflows

- **`release.yml`** - Creates releases from git tags with all artifacts
- **`ci-cd.yml`** - Runs on push/PR to master branch
- **`firmware-build.yml`** - Builds ESP32 firmware
- **`docker-build.yml`** - Builds multi-arch Docker images (amd64/arm64)
- **`freecad-export.yml`** - Exports STL files from FreeCAD

## Creating a Release

```bash
# Create and push a tag
git tag v1.0.0
git push origin v1.0.0
```

The release workflow automatically:
- Builds all artifacts (firmware, Docker images, STL files)
- Creates a GitHub release with all files attached
- Pushes Docker images to `izdevdigital/e-paper-dashboard` on Docker Hub

## Setup

**Required - Docker Hub:** Add repository secrets to push Docker images:
1. Go to Repository Settings → Secrets and Variables → Actions
2. Add secrets:
   - `DOCKERHUB_USERNAME` - Your Docker Hub username
   - `DOCKERHUB_TOKEN` - Your Docker Hub access token

## Usage

### Download Artifacts
- **Releases**: [github.com/beetnode/e-paper-dashboard/releases](https://github.com/beetnode/e-paper-dashboard/releases)
- **Actions runs**: Actions tab → Select workflow run → Artifacts

### Docker Images
```bash
docker pull izdevdigital/e-paper-dashboard:v1.0.0
docker pull izdevdigital/e-paper-dashboard:latest
```

### Flash Firmware
```bash
esptool.py --port /dev/ttyUSB0 write_flash 0x10000 firmware-{version}.bin
```
