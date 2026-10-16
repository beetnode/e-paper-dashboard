# Firmware signing key

`firmware_signing_public.pem` is the ECDSA P-256 public key compiled into the firmware and used to verify OTA images. Generate it with `scripts/generate-signing-key.sh`; see the "Signed OTA updates" section of the firmware README.

Never put the private key in this directory.
