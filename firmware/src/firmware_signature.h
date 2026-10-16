#pragma once

#include <Arduino.h>
#include <mbedtls/md.h>

namespace FirmwareSignature {
constexpr size_t Sha256Bytes = 32;

// Incremental SHA-256 of an OTA image as it streams in.
class ImageHasher {
public:
  ImageHasher();
  ~ImageHasher();
  ImageHasher(const ImageHasher&) = delete;
  ImageHasher& operator=(const ImageHasher&) = delete;

  bool ok() const { return _ok; }
  void update(const uint8_t* data, size_t length);
  bool finish(uint8_t digest[Sha256Bytes]);

private:
  mbedtls_md_context_t _context;
  bool _ok = false;
};

bool parseSha256Hex(const String& hex, uint8_t digest[Sha256Bytes]);

// Checks the release signature: an ECDSA P-256 signature, made by the release pipeline
// (firmware/scripts/sign-firmware.sh), over a manifest binding the version to the image hash.
// Verified against the public key compiled into this firmware.
bool verify(const String& version, const uint8_t imageSha256[Sha256Bytes],
            const String& signatureBase64, String& error);
}
