#include "network.h"
#include "version.h"

namespace {
String hostHeader(const String& host, int port)
{
  const String formattedHost = host.indexOf(':') >= 0 ? "[" + host + "]" : host;
  return formattedHost + ":" + String(port);
}

constexpr size_t MaxHeaderLineBytes = 512;
constexpr int MaxHeaderLines = 64;

// Matches "Name: value" case-insensitively, as HTTP header names are.
bool headerValue(const char* line, const char* name, String& value)
{
  const size_t nameLength = strlen(name);
  if (strncasecmp(line, name, nameLength) != 0 || line[nameLength] != ':') return false;
  value = line + nameLength + 1;
  value.trim();
  return true;
}

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
}

Network::Network(Logger& logger) : _logger(logger) {}

bool Network::connectToWiFi(const String& ssid, const String& password)
{
  _logger.println("Found stored configuration!");
  constexpr int maxAttempts = 2;
  for (int attempt = 1; attempt <= maxAttempts; ++attempt)
  {
    _logger.printf("Connecting to WiFi (attempt %d/%d)\n", attempt, maxAttempts);
    if (waitForWiFi(ssid, password))
    {
      _logger.println("WiFi connected");
      _logger.print("IP address: ");
      _logger.println(WiFi.localIP());
      return true;
    }
    WiFi.disconnect(true);
    delay(1000);
  }
  return false;
}

bool Network::waitForWiFi(const String& ssid, const String& password)
{
  constexpr int maxRetries = 20;
  int retries = 0;
  WiFi.begin(ssid.c_str(), password.c_str());
  while (WiFi.status() != WL_CONNECTED && retries < maxRetries)
  {
    ++retries;
    delay(500);
  }
  return WiFi.status() == WL_CONNECTED;
}

bool Network::sendGetRequest(const String& url, DeviceConfig& config)
{
  if (!connectTo(config.dashboardUrl, config.devicePort, config.serverFingerprint))
  {
    _logger.println("Failed to connect to the remote server...");
    return false;
  }
  _logger.println("Successfully connected to the remote server!");
  _logger.println("Sending request...");

  auto& c = _client;
  c.println("GET " + config.dashboardBasePath + url + " HTTP/1.1");
  c.print("X-Api-Key: ");
  c.println(config.dashboardApiKey);
  c.print("Host: ");
  c.println(hostHeader(config.dashboardUrl, config.devicePort));
  c.print("X-Device-Firmware-Version: ");
  c.println(FIRMWARE_VERSION);
  c.println("Connection: close");
  c.println();
  return true;
}

ResponseHeaders Network::readResponseHeaders()
{
  _logger.println("Reading headers...");
  ResponseHeaders headers;
  char line[MaxHeaderLineBytes + 1];
  for (int lineCount = 0; lineCount < MaxHeaderLines; ++lineCount)
  {
    if (!_client.connected() && !_client.available()) break;

    size_t length = _client.readBytesUntil('\n', line, MaxHeaderLineBytes);
    if (length == 0) break;  // timed out or closed before the header block ended
    if (length == MaxHeaderLineBytes)
    {
      _logger.println("Response header line too long, discarding response");
      break;
    }
    line[length] = '\0';
    if (line[length - 1] == '\r') line[--length] = '\0';

    if (length == 0)
    {
      _logger.print("HTTP status: ");
      _logger.println(headers.statusCode);
      headers.isSuccess = headers.statusCode == 200;
      return headers;
    }

    if (lineCount == 0)
    {
      const char* space = strchr(line, ' ');
      if (strncmp(line, "HTTP/", 5) != 0 || space == nullptr) break;
      headers.statusCode = atoi(space + 1);
      continue;
    }

    String value;
    if (headerValue(line, "X-Firmware-Version", value)) headers.firmwareVersion = value;
    else if (headerValue(line, "X-Firmware-SHA256", value)) headers.firmwareSha256 = value;
    else if (headerValue(line, "X-Firmware-Signature", value)) headers.firmwareSignature = value;
    else if (headerValue(line, "Content-Length", value)) headers.contentLength = value.toInt();
    else if (headerValue(line, "Retry-After", value)) headers.retryAfterSeconds = value.toInt();
  }

  _logger.println("Incomplete response headers");
  return ResponseHeaders{};
}

bool Network::connectTo(const String& host, int port, String& serverFingerprint)
{
  _identityMismatch = false;
  // The chain is not validated: the pinned fingerprint is the only trust anchor, which works
  // for the server's self-signed certificate on a LAN and in hosted deployments alike.
  _client.setInsecure();
  if (!_client.connect(host.c_str(), port)) return false;

  uint8_t fingerprint[32];
  if (!_client.getFingerprintSHA256(fingerprint))
  {
    _client.stop();
    return false;
  }

  const String observed = toHex(fingerprint, sizeof(fingerprint));
  if (serverFingerprint.length() == 0)
  {
    serverFingerprint = observed;
    _logger.print("Pinned server certificate SHA-256: ");
    _logger.println(observed);
    return true;
  }

  if (!serverFingerprint.equalsIgnoreCase(observed))
  {
    _logger.print("Server certificate changed, refusing connection. Seen SHA-256: ");
    _logger.println(observed);
    _identityMismatch = true;
    _client.stop();
    return false;
  }
  return true;
}

void Network::send(const String& data)
{
  _client.print(data);
}

void Network::setTimeout(int timeoutMs)
{
  _client.setTimeout(timeoutMs);
}

void Network::close()
{
  _client.stop();
}

bool Network::connected()
{
  return _client.connected();
}

int Network::available()
{
  return _client.available();
}

size_t Network::read(uint8_t* buffer, size_t size)
{
  return _client.read(buffer, size);
}

String Network::readStringUntil(char terminator)
{
  return _client.readStringUntil(terminator);
}

String Network::readString()
{
  return _client.readString();
}

WiFiClient& Network::client()
{
  return _client;
}
