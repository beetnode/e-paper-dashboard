# izBoard Firmware

## Overview

ESP32 firmware for the izBoard E-Paper dashboard device. Connects to an izBoard server, fetches rendered dashboard images on a configurable schedule, displays them on a 7.5" E-Paper screen, and deep-sleeps between updates to maximize battery life.

## Hardware

- **ESP32 Board**: [Waveshare E-Paper ESP32 Driver Board](https://www.waveshare.com/e-Paper-ESP32-Driver-Board.htm)
- **E-Paper Display**: [Waveshare 7.5inch e-Paper HAT (B)](https://www.waveshare.com/wiki/7.5inch_e-Paper_HAT_(B)_Manual) — 800×480, black/white/red

> **Note**: Other E-Paper displays or ESP32 boards can be supported with pin configuration and display driver adjustments.

## Features

- Scheduled dashboard image updates from the server
- Deep sleep between updates for long battery life
- Secure device pairing via captive portal
- OTA firmware updates
- Manual refresh and factory reset via reset button

## Setup

### First Boot

1. Power on the ESP32 board
2. The device shows a QR code for its password-protected `izBoard-XXXX` Wi-Fi access point and a six-character claim code
3. Scan the QR once and use the captive portal to enter the home Wi-Fi credentials and the server's `CLIENT_URL`
4. From any network that can reach the server, open Devices and enter the claim code
5. The device stays on home Wi-Fi, receives its credential, and begins fetching dashboards without rebooting

The claim code expires after ten minutes. Wi-Fi, server, and pending claim details are saved before the device leaves setup mode, so an interrupted device can resume the claim after power is restored.

HTTPS server certificates are checked against the trusted root bundle in `data/cert/x509_crt_bundle.bin`. Replace and regenerate that bundle when a deployment uses a private certificate authority. The device must be able to obtain network time before its first verified HTTPS connection.

### Signed OTA updates

The device installs an OTA image only when its SHA-256 matches the `X-Firmware-SHA256` header and the `X-Firmware-Signature` header holds a valid ECDSA P-256 signature from the release pipeline. The signature covers a manifest binding the release version to the image hash, so an older signed image cannot be offered as a newer release. Unsigned or tampered images are discarded before the update partition is marked bootable.

The public key is compiled in from `data/signing/firmware_signing_public.pem`; the build fails without it. To set up signing (once per repository, or for a fork with its own key):

```bash
./scripts/generate-signing-key.sh ~/izboard-firmware-signing.key
gh secret set FIRMWARE_SIGNING_KEY < ~/izboard-firmware-signing.key
```

Commit the generated public key and keep the private key offline. The release workflow signs `firmware-<version>.bin` with `scripts/sign-firmware.sh`, checks the signature against the committed public key, and publishes `firmware-<version>.bin.sig` next to the binary. Rotating the key requires shipping a firmware that trusts the new key, signed with the old one.

### Device Controls

| Action | How |
|---|---|
| Manual refresh | Press the reset button once |
| Factory reset | Hold the reset button for 10 seconds |

## Building and Flashing

This project uses [PlatformIO](https://platformio.org/) for building and flashing.

### Prerequisites

- PlatformIO (VS Code extension or CLI)
- ESP32 board connected via USB

### Build and Upload

Using PlatformIO CLI:

```bash
pio run -t upload
```

Using VS Code:

1. Open the `firmware` folder in VS Code
2. Click **Upload** in the PlatformIO toolbar

## Versioning

`FIRMWARE_VERSION` in `src/version.h` must match the release tag (for example `0.5.0` for `v0.5.0`). The server offers the firmware from the latest GitHub release, and a device updates over the air when that release is newer than its own version.

## Dependencies

Managed automatically by PlatformIO — see `platformio.ini` for the full list.

- **GxEPD2** — E-Paper display driver
- **QRCode** — QR code generation for the setup portal
