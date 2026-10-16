#pragma once

#include <WiFi.h>
#include <WiFiClientSecure.h>
#include "types.h"
#include "logger.h"

class Network {
public:
  explicit Network(Logger& logger);

  bool connectToWiFi(const String& ssid, const String& password);
  bool sendGetRequest(const String& url, DeviceConfig& config);
  ResponseHeaders readResponseHeaders();

  // Opens a TLS connection and checks the server certificate against serverFingerprint.
  // An empty serverFingerprint pins the certificate seen now (trust on first use).
  bool connectTo(const String& host, int port, String& serverFingerprint);
  // Whether the last connection was refused because the certificate differs from the pinned one.
  bool identityMismatch() const { return _identityMismatch; }
  void send(const String& data);
  void setTimeout(int timeoutMs);
  void close();

  bool connected();
  int available();
  size_t read(uint8_t* buffer, size_t size);
  String readStringUntil(char terminator);
  String readString();
  WiFiClient& client();

private:
  Logger& _logger;
  WiFiClientSecure _client;
  bool _identityMismatch = false;
  bool waitForWiFi(const String& ssid, const String& password);
};
