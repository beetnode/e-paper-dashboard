#include "firmware_signature.h"
#include <mbedtls/base64.h>
#include <mbedtls/pk.h>

extern const char firmware_signing_public_pem[] asm("_binary_data_signing_firmware_signing_public_pem_start");

namespace FirmwareSignature {
namespace {
constexpr size_t MaxSignatureBytes = 128;

String toHex(const uint8_t* data, size_t length)
{
  static const char digits[] = "0123456789abcdef";
  String hex;
  hex.reserve(length * 2);
  for (size_t i = 0; i < length; ++i)
  {
    hex += digits[data[i] >> 4];
    hex += digits[data[i] & 0x0f];
  }
  return hex;
}

// Must stay byte-for-byte identical to the manifest built by firmware/scripts/sign-firmware.sh.
String buildManifest(const String& version, const uint8_t imageSha256[Sha256Bytes])
{
  return "izboard-firmware\nversion=" + version + "\nsha256=" + toHex(imageSha256, Sha256Bytes) + "\n";
}

int hexValue(char c)
{
  if (c >= '0' && c <= '9') return c - '0';
  if (c >= 'a' && c <= 'f') return c - 'a' + 10;
  if (c >= 'A' && c <= 'F') return c - 'A' + 10;
  return -1;
}
}

ImageHasher::ImageHasher()
{
  mbedtls_md_init(&_context);
  const mbedtls_md_info_t* info = mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
  _ok = info != nullptr
      && mbedtls_md_setup(&_context, info, 0) == 0
      && mbedtls_md_starts(&_context) == 0;
}

ImageHasher::~ImageHasher()
{
  mbedtls_md_free(&_context);
}

void ImageHasher::update(const uint8_t* data, size_t length)
{
  if (_ok && mbedtls_md_update(&_context, data, length) != 0) _ok = false;
}

bool ImageHasher::finish(uint8_t digest[Sha256Bytes])
{
  if (_ok && mbedtls_md_finish(&_context, digest) != 0) _ok = false;
  return _ok;
}

bool parseSha256Hex(const String& hex, uint8_t digest[Sha256Bytes])
{
  if (hex.length() != Sha256Bytes * 2) return false;
  for (size_t i = 0; i < Sha256Bytes; ++i)
  {
    const int high = hexValue(hex[i * 2]);
    const int low = hexValue(hex[i * 2 + 1]);
    if (high < 0 || low < 0) return false;
    digest[i] = static_cast<uint8_t>((high << 4) | low);
  }
  return true;
}

bool verify(const String& version, const uint8_t imageSha256[Sha256Bytes],
            const String& signatureBase64, String& error)
{
  uint8_t signature[MaxSignatureBytes];
  size_t signatureLength = 0;
  if (signatureBase64.length() == 0
      || mbedtls_base64_decode(signature, sizeof(signature), &signatureLength,
                               reinterpret_cast<const unsigned char*>(signatureBase64.c_str()),
                               signatureBase64.length()) != 0)
  {
    error = "malformed signature";
    return false;
  }

  const String manifest = buildManifest(version, imageSha256);
  uint8_t manifestHash[Sha256Bytes];
  if (mbedtls_md(mbedtls_md_info_from_type(MBEDTLS_MD_SHA256),
                 reinterpret_cast<const unsigned char*>(manifest.c_str()), manifest.length(),
                 manifestHash) != 0)
  {
    error = "could not hash manifest";
    return false;
  }

  mbedtls_pk_context key;
  mbedtls_pk_init(&key);
  // The embedded PEM is null-terminated, and mbedtls expects the terminator in the length.
  const int parsed = mbedtls_pk_parse_public_key(
      &key, reinterpret_cast<const unsigned char*>(firmware_signing_public_pem),
      strlen(firmware_signing_public_pem) + 1);
  bool valid = false;
  if (parsed != 0 || !mbedtls_pk_can_do(&key, MBEDTLS_PK_ECDSA))
  {
    error = "invalid embedded signing key";
  }
  else if (mbedtls_pk_verify(&key, MBEDTLS_MD_SHA256, manifestHash, sizeof(manifestHash),
                             signature, signatureLength) != 0)
  {
    error = "signature does not match";
  }
  else
  {
    valid = true;
  }
  mbedtls_pk_free(&key);
  return valid;
}
}
