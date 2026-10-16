#include "network.h"
#include "version.h"
#include <time.h>

extern const uint8_t rootca_crt_bundle_start[] asm("_binary_data_cert_x509_crt_bundle_bin_start");

namespace {
bool ensureClockIsSet(Logger& logger)
{
  constexpr time_t MinimumValidTime = 1704067200;
  if (time(nullptr) >= MinimumValidTime) return true;

  logger.println("Synchronizing time for HTTPS verification...");
  configTime(0, 0, "pool.ntp.org", "time.nist.gov");
  for (int attempt = 0; attempt < 40; ++attempt)
  {
    if (time(nullptr) >= MinimumValidTime) return true;
    delay(250);
  }

  logger.println("Could not synchronize time; HTTPS connection refused");
  return false;
}

String hostHeader(const String& host, int port)
{
  const String formattedHost = host.indexOf(':') >= 0 ? "[" + host + "]" : host;
  return formattedHost + ":" + String(port);
}
}

Network::Network(Logger& logger) : _logger(logger) {}

WiFiClient& Network::activeClient()
{
  return _useSecure ? static_cast<WiFiClient&>(_secureClient) : _client;
}

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

bool Network::sendGetRequest(const String& url, const DeviceConfig& config)
{
  if (!connectTo(config.dashboardUrl, config.devicePort, config.useHttps))
  {
    _logger.println("Failed to connect to the remote server...");
    return false;
  }
  _logger.println("Successfully connected to the remote server!");
  _logger.println("Sending request...");

  auto& c = activeClient();
  c.println("GET " + config.dashboardBasePath + url + " HTTP/1.1");
  c.print("X-Api-Key: ");
  c.println(config.dashboardApiKey);
  c.print("Host: ");
  c.println(hostHeader(config.dashboardUrl, config.devicePort));
  c.print("X-Device-Firmware-Version: ");
  c.println(FIRMWARE_VERSION);
  c.print("X-Device-Id: ");
  c.println(WiFi.macAddress());
  c.println("Connection: close");
  c.println();
  return true;
}

ResponseHeaders Network::readResponseHeaders()
{
  _logger.println("Reading headers...");
  auto& c = activeClient();
  ResponseHeaders headers;
  while (c.connected() || c.available())
  {
    String line = c.readStringUntil('\n');
    _logger.println(line);

    if (!headers.isSuccess && line.startsWith("HTTP/"))
    {
      int spaceIdx = line.indexOf(' ');
      if (spaceIdx > 0)
      {
        int statusCode = line.substring(spaceIdx + 1).toInt();
        headers.isSuccess = (statusCode == 200);
      }
    }

    if (line.startsWith("X-Firmware-Version:"))
    {
      headers.firmwareVersion = line.substring(strlen("X-Firmware-Version:"));
      headers.firmwareVersion.trim();
    }

    if (line.startsWith("X-Firmware-SHA256:"))
    {
      headers.firmwareSha256 = line.substring(strlen("X-Firmware-SHA256:"));
      headers.firmwareSha256.trim();
    }

    if (line.startsWith("X-Firmware-Signature:"))
    {
      headers.firmwareSignature = line.substring(strlen("X-Firmware-Signature:"));
      headers.firmwareSignature.trim();
    }

    if (line.startsWith("Content-Length:"))
    {
      headers.contentLength = line.substring(strlen("Content-Length:")).toInt();
    }

    if (line == "\r")
    {
      break;
    }
  }
  return headers;
}

bool Network::connectTo(const String& host, int port, bool useHttps)
{
  _useSecure = useHttps;
  if (_useSecure)
  {
    if (!ensureClockIsSet(_logger)) return false;
    _secureClient.setCACertBundle(rootca_crt_bundle_start);
    return _secureClient.connect(host.c_str(), port);
  }
  return _client.connect(host.c_str(), port);
}

void Network::send(const String& data)
{
  activeClient().print(data);
}

void Network::setTimeout(int timeoutMs)
{
  activeClient().setTimeout(timeoutMs);
}

void Network::close()
{
  activeClient().stop();
  _useSecure = false;
}

bool Network::connected()
{
  return activeClient().connected();
}

int Network::available()
{
  return activeClient().available();
}

size_t Network::read(uint8_t* buffer, size_t size)
{
  return activeClient().read(buffer, size);
}

String Network::readStringUntil(char terminator)
{
  return activeClient().readStringUntil(terminator);
}

String Network::readString()
{
  return activeClient().readString();
}

WiFiClient& Network::client()
{
  return activeClient();
}
