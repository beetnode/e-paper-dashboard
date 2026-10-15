#pragma once

#include <WiFi.h>
#include <WiFiClientSecure.h>
#include "types.h"
#include "logger.h"

class Network {
public:
  explicit Network(Logger& logger);

  bool connectToWiFi(const String& ssid, const String& password);
  bool sendGetRequest(const String& url, const DeviceConfig& config);
  ResponseHeaders readResponseHeaders();

  bool connectTo(const String& host, int port, bool useHttps = false);
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
  WiFiClient _client;
  WiFiClientSecure _secureClient;
  bool _useSecure = false;
  WiFiClient& activeClient();
  bool waitForWiFi(const String& ssid, const String& password);
};
