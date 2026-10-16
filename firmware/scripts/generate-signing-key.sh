#!/usr/bin/env bash
# Generates the ECDSA P-256 key pair used to sign OTA firmware images.
#
# The public key is written into the firmware sources and compiled into every build.
# The private key must stay outside the repository; store it as the FIRMWARE_SIGNING_KEY
# secret of the `release` GitHub environment, e.g.
# `gh secret set FIRMWARE_SIGNING_KEY --env release < <private-key.pem>`.
#
# Usage: generate-signing-key.sh <private-key-out>
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "Usage: $0 <private-key-out>" >&2
  exit 2
fi

PRIVATE_KEY=$1
PUBLIC_KEY="$(cd "$(dirname "$0")/.." && pwd)/data/signing/firmware_signing_public.pem"

if [ -e "$PRIVATE_KEY" ]; then
  echo "Refusing to overwrite existing $PRIVATE_KEY" >&2
  exit 1
fi

(umask 077 && openssl ecparam -name prime256v1 -genkey -noout -out "$PRIVATE_KEY")
openssl ec -in "$PRIVATE_KEY" -pubout -out "$PUBLIC_KEY"

echo "Private key: $PRIVATE_KEY (keep secret, never commit)"
echo "Public key:  $PUBLIC_KEY (commit this)"
