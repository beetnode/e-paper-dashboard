#!/usr/bin/env bash
# Signs an OTA firmware image for izBoard devices.
#
# The signature covers a manifest that binds the release version to the image's SHA-256,
# so a validly signed older image cannot be relabelled as a newer release.
# The manifest format must stay identical to buildManifest() in src/firmware_signature.cpp.
#
# Usage: sign-firmware.sh <firmware.bin> <version> <private-key.pem> <public-key.pem> <signature-out>
set -euo pipefail

if [ "$#" -ne 5 ]; then
  echo "Usage: $0 <firmware.bin> <version> <private-key.pem> <public-key.pem> <signature-out>" >&2
  exit 2
fi

BINARY=$1
VERSION=$2
PRIVATE_KEY=$3
PUBLIC_KEY=$4
SIGNATURE_OUT=$5

MANIFEST=$(mktemp)
trap 'rm -f "$MANIFEST"' EXIT

SHA256=$(openssl dgst -sha256 -r "$BINARY" | cut -d' ' -f1)
printf 'izboard-firmware\nversion=%s\nsha256=%s\n' "$VERSION" "$SHA256" > "$MANIFEST"

openssl dgst -sha256 -sign "$PRIVATE_KEY" -out "$SIGNATURE_OUT" "$MANIFEST"

# Fails the build when the signing key does not match the public key compiled into the firmware.
openssl dgst -sha256 -verify "$PUBLIC_KEY" -signature "$SIGNATURE_OUT" "$MANIFEST"

echo "Signed v$VERSION (sha256 $SHA256) -> $SIGNATURE_OUT"
