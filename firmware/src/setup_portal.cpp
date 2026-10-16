#include "setup_portal.h"
#include "constants.h"
#include <WiFi.h>
#include <WebServer.h>
#include <DNSServer.h>
#include <ArduinoJson.h>
#include <esp_system.h>

namespace {
String randomFromAlphabet(size_t length, const char* alphabet)
{
  String result;
  const size_t alphabetLength = strlen(alphabet);
  result.reserve(length);
  for (size_t i = 0; i < length; ++i)
  {
    result += alphabet[esp_random() % alphabetLength];
  }
  return result;
}

String generateClaimCode()
{
  return randomFromAlphabet(6, "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ");
}

String generateRegistrationToken()
{
  return randomFromAlphabet(32, "0123456789abcdef");
}

String generateAccessPointPassword()
{
  return randomFromAlphabet(12, "23456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz");
}

bool parsePort(const String& value, int& port)
{
  if (value.length() == 0 || value.length() > 5) return false;
  for (unsigned int i = 0; i < value.length(); ++i)
  {
    if (!isDigit(value[i])) return false;
  }
  const long parsed = value.toInt();
  if (parsed <= 0 || parsed > 65535) return false;
  port = static_cast<int>(parsed);
  return true;
}

// Accepts the server address shown in the dashboard ("host[:port]"). A pasted http:// or
// https:// prefix is ignored: the device always uses TLS on the device port.
bool parseServerAddress(String rawUrl, String& host, int& port, String& basePath)
{
  rawUrl.trim();
  if (rawUrl.indexOf(' ') >= 0 || rawUrl.indexOf('\t') >= 0
      || rawUrl.indexOf('@') >= 0 || rawUrl.indexOf('?') >= 0 || rawUrl.indexOf('#') >= 0)
  {
    return false;
  }

  if (rawUrl.startsWith("https://")) rawUrl = rawUrl.substring(8);
  else if (rawUrl.startsWith("http://")) rawUrl = rawUrl.substring(7);
  if (rawUrl.indexOf("://") >= 0) return false;

  const int slashIdx = rawUrl.indexOf('/');
  if (slashIdx >= 0)
  {
    basePath = rawUrl.substring(slashIdx);
    rawUrl = rawUrl.substring(0, slashIdx);
    while (basePath.length() > 1 && basePath.endsWith("/"))
    {
      basePath.remove(basePath.length() - 1);
    }
    if (basePath == "/") basePath = "";
  }

  port = DeviceServer::DefaultDevicePort;
  if (rawUrl.startsWith("["))
  {
    const int bracketIdx = rawUrl.indexOf(']');
    if (bracketIdx < 2) return false;
    host = rawUrl.substring(1, bracketIdx);
    if (bracketIdx + 1 < static_cast<int>(rawUrl.length()))
    {
      if (rawUrl[bracketIdx + 1] != ':'
          || !parsePort(rawUrl.substring(bracketIdx + 2), port)) return false;
    }
  }
  else
  {
    const int colonIdx = rawUrl.lastIndexOf(':');
    if (colonIdx > 0)
    {
      if (rawUrl.indexOf(':') != colonIdx
          || !parsePort(rawUrl.substring(colonIdx + 1), port)) return false;
      host = rawUrl.substring(0, colonIdx);
    }
    else
    {
      host = rawUrl;
    }
  }

  return host.length() > 0;
}
}

SetupPortal::SetupPortal(Logger& logger, ConfigStore& configStore, DisplayManager& display,
                         Network& network, DeviceApi& deviceApi)
    : _logger(logger), _configStore(configStore), _display(display),
      _network(network), _deviceApi(deviceApi) {}

DeviceConfig SetupPortal::run()
{
  IPAddress apIP(192, 168, 4, 1);
  IPAddress gateway(192, 168, 4, 1);
  IPAddress subnet(255, 255, 255, 0);
  WiFi.softAPConfig(apIP, gateway, subnet);
  String macAddress = WiFi.macAddress();
  String apName = "izBoard-" + macAddress.substring(macAddress.length() - 5);
  apName.replace(":", "");
  const String apPassword = generateAccessPointPassword();
  const String claimCode = generateClaimCode();
  const String registrationToken = generateRegistrationToken();
  WiFi.softAP(apName.c_str(), apPassword.c_str());
  apIP = WiFi.softAPIP();
  _logger.print("AP IP address: ");
  _logger.println(apIP);
  _logger.print("MAC address: ");
  _logger.println(macAddress);

  _display.showWelcomePage(apIP, apName, apPassword, claimCode);

  const byte DNS_PORT = 53;
  DNSServer dnsServer;
  dnsServer.start(DNS_PORT, "*", apIP);

  WebServer server(80);
  const char *htmlForm = R"rawliteral(
    <!DOCTYPE html>
    <html lang="en">

    <head>
        <meta charset="UTF-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>izBoard Setup</title>
        <style>
            *, *::before, *::after { box-sizing: border-box; }
            .container { max-width: 960px; margin-right: auto; margin-left: auto; padding-right: 12px; padding-left: 12px; }
            .mt-5 { margin-top: 3rem !important; }
            .text-center { text-align: center !important; }
            .card { position: relative; display: flex; flex-direction: column; min-width: 0; word-wrap: break-word; background-color: #fff; background-clip: border-box; border: 1px solid rgba(0,0,0,.125); border-radius: .25rem; }
            .card-header { padding: .75rem 1.25rem; margin-bottom: 0; background-color: rgba(0,0,0,.03); border-bottom: 1px solid rgba(0,0,0,.125); font-weight: 500; }
            .card-body { flex: 1 1 auto; padding: 1.25rem; }
            .mb-3 { margin-bottom: 1rem !important; }
            .form-label { margin-bottom: .5rem; font-weight: 500; display: inline-block; }
            .form-control { display: block; width: 100%; padding: .375rem .75rem; font-size: 1rem; line-height: 1.5; color: #212529; background-color: #fff; background-clip: padding-box; border: 1px solid #ced4da; border-radius: .25rem; transition: border-color .15s ease-in-out,box-shadow .15s ease-in-out; }
            .form-control:focus { color: #212529; background-color: #fff; border-color: #86b7fe; outline: 0; box-shadow: 0 0 0 .25rem rgba(13,110,253,.25); }
            .form-select { display: block; width: 100%; padding: .375rem 2.25rem .375rem .75rem; font-size: 1rem; line-height: 1.5; color: #212529; background-color: #fff; border: 1px solid #ced4da; border-radius: .25rem; transition: border-color .15s ease-in-out,box-shadow .15s ease-in-out; }
            .btn { display: inline-block; font-weight: 400; line-height: 1.5; color: #fff; text-align: center; text-decoration: none; vertical-align: middle; cursor: pointer; background-color: #0d6efd; border: 1px solid #0d6efd; padding: .375rem .75rem; font-size: 1rem; border-radius: .25rem; transition: color .15s ease-in-out,background-color .15s ease-in-out,border-color .15s ease-in-out,box-shadow .15s ease-in-out; }
            .btn-primary { color: #fff; background-color: #0d6efd; border-color: #0d6efd; }
            .btn-primary:hover { color: #fff; background-color: #0b5ed7; border-color: #0a58ca; }
            .btn-secondary { color: #fff; background-color: #6c757d; border-color: #6c757d; }
            .btn-secondary:hover { color: #fff; background-color: #5c636a; border-color: #565e64; }
            .w-100 { width: 100% !important; }
            .d-flex { display: flex; }
            .gap-2 { gap: .5rem; }
            .flex-grow-1 { flex-grow: 1; }
            .spinner { display: inline-block; width: 1rem; height: 1rem; border: 2px solid #fff; border-right-color: transparent; border-radius: 50%; animation: spin .6s linear infinite; vertical-align: middle; margin-right: .5rem; }
            .icon { display: block; margin: 0 auto 1rem; width: 64px; height: 64px; }
            .form-text { display: block; margin-top: .25rem; font-size: .875em; color: #6c757d; }
            @keyframes spin { to { transform: rotate(360deg); } }
        </style>
    </head>

    <body>

        <div class="container mt-5">
            <svg class="icon" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512"><defs><filter id="dropShadow" x="-50%" y="-50%" width="200%" height="200%"><feDropShadow dx="0" dy="2" stdDeviation="4" flood-opacity="0.2" flood-color="#000000"/><feDropShadow dx="0" dy="4" stdDeviation="8" flood-opacity="0.14" flood-color="#000000"/></filter><filter id="insetShadow"><feGaussianBlur in="SourceGraphic" stdDeviation="3"/><feOffset dx="0" dy="1.5" result="offsetblur"/><feComponentTransfer><feFuncA type="linear" slope="0.3"/></feComponentTransfer></filter><linearGradient id="grad1" x1="0%" y1="0%" x2="0%" y2="100%"><stop offset="0%" style="stop-color:white;stop-opacity:0.03"/><stop offset="100%" style="stop-color:black;stop-opacity:0.02"/></linearGradient><radialGradient id="highlight" cx="35%" cy="20%" r="65%"><stop offset="0%" style="stop-color:white;stop-opacity:0.02"/><stop offset="100%" style="stop-color:white;stop-opacity:0"/></radialGradient><pattern id="scanlines" x="0" y="0" width="2" height="2" patternUnits="userSpaceOnUse"><rect x="0" y="0" width="2" height="1" fill="#000000" opacity="0.02"/></pattern></defs><rect x="51" y="64" width="410" height="384" rx="20" fill="#212529" filter="url(#dropShadow)"/><rect x="71" y="84" width="370" height="344" rx="8" fill="#f8f9fa" filter="url(#dropShadow)"/><rect x="91" y="104" width="90" height="96" rx="4" fill="#084298" filter="url(#dropShadow)"/><rect x="91" y="104" width="90" height="96" rx="4" fill="url(#grad1)" pointer-events="none"/><rect x="91" y="104" width="90" height="96" rx="4" fill="url(#highlight)" pointer-events="none"/><rect x="91" y="212" width="90" height="196" rx="4" fill="#0b5ed7" filter="url(#dropShadow)"/><rect x="91" y="212" width="90" height="196" rx="4" fill="url(#grad1)" pointer-events="none"/><rect x="91" y="212" width="90" height="196" rx="4" fill="url(#highlight)" pointer-events="none"/><rect x="193" y="104" width="134" height="96" rx="4" fill="#0d6efd" filter="url(#dropShadow)"/><rect x="193" y="104" width="134" height="96" rx="4" fill="url(#grad1)" pointer-events="none"/><rect x="193" y="104" width="134" height="96" rx="4" fill="url(#highlight)" pointer-events="none"/><rect x="339" y="104" width="82" height="96" rx="4" fill="#31a8ff" filter="url(#dropShadow)"/><rect x="339" y="104" width="82" height="96" rx="4" fill="url(#grad1)" pointer-events="none"/><rect x="339" y="104" width="82" height="96" rx="4" fill="url(#highlight)" pointer-events="none"/><defs><clipPath id="middleClip"><rect x="193" y="212" width="228" height="96" rx="4"/></clipPath></defs><g clip-path="url(#middleClip)" filter="url(#dropShadow)"><polygon points="193,212 327,212 277,308 193,308" fill="#75c7ff"/></g><rect x="193" y="212" width="228" height="96" rx="4" fill="url(#grad1)" pointer-events="none" clip-path="url(#middleClip)"/><rect x="193" y="212" width="228" height="96" rx="4" fill="url(#highlight)" pointer-events="none" clip-path="url(#middleClip)"/><g clip-path="url(#middleClip)" filter="url(#dropShadow)"><polygon points="339,212 421,212 421,308 289,308" fill="#54b6ff"/></g><rect x="193" y="212" width="228" height="96" rx="4" fill="url(#grad1)" pointer-events="none" clip-path="url(#middleClip)"/><rect x="193" y="212" width="228" height="96" rx="4" fill="url(#highlight)" pointer-events="none" clip-path="url(#middleClip)"/><rect x="193" y="320" width="84" height="88" rx="4" fill="#31a8ff" filter="url(#dropShadow)"/><rect x="193" y="320" width="84" height="88" rx="4" fill="url(#grad1)" pointer-events="none"/><rect x="193" y="320" width="84" height="88" rx="4" fill="url(#highlight)" pointer-events="none"/><rect x="289" y="320" width="132" height="88" rx="4" fill="#54b6ff" filter="url(#dropShadow)"/><rect x="289" y="320" width="132" height="88" rx="4" fill="url(#grad1)" pointer-events="none"/><rect x="289" y="320" width="132" height="88" rx="4" fill="url(#highlight)" pointer-events="none"/><rect x="71" y="84" width="370" height="344" rx="8" fill="url(#scanlines)" pointer-events="none"/></svg>
            <h2 class="text-center">izBoard Setup</h2>

            <form action="/submit" method="post">
                <div class="card mb-3">
                    <div class="card-header">WLAN Setup</div>
                    <div class="card-body">
                        <div class="mb-3">
                            <label for="ssid" class="form-label">Network</label>
                            <div class="d-flex gap-2">
                                <select class="form-select flex-grow-1" name="ssid" id="ssid">
                                    <option value="">Scanning...</option>
                                </select>
                                <button type="button" class="btn btn-secondary" id="scanBtn" onclick="scanNetworks()">Scan</button>
                            </div>
                        </div>
                        <div class="mb-3">
                            <label for="password" class="form-label">Password</label>
                            <input type="password" class="form-control" name="password" id="password"
                                placeholder="Enter password ...">
                        </div>
                    </div>
                </div>

                <div class="card mb-3">
                    <div class="card-header">Dashboard</div>
                    <div class="card-body">
                        <div class="mb-3">
                            <label for="server_url" class="form-label">Server address</label>
                            <input type="text" class="form-control" name="server_url" id="server_url"
                                placeholder="e.g. 192.168.1.100:8129">
                            <div class="form-text">As shown in the dashboard when adding a device</div>
                        </div>
                        <div class="form-text">After applying, use any network that can reach the dashboard and enter the claim code shown on the display.</div>
                    </div>
                </div>

                <button type="submit" class="btn btn-primary w-100">Apply</button>
            </form>
        </div>

        <script>
            function scanNetworks() {
                var btn = document.getElementById('scanBtn');
                var sel = document.getElementById('ssid');
                btn.disabled = true;
                btn.innerHTML = '<span class="spinner"></span>';
                sel.innerHTML = '<option value="">Scanning...</option>';
                fetch('/scan').then(function(r) { return r.json(); }).then(function(networks) {
                    sel.innerHTML = '';
                    if (networks.length === 0) {
                        sel.innerHTML = '<option value="">No networks found</option>';
                    } else {
                        for (var i = 0; i < networks.length; i++) {
                            var opt = document.createElement('option');
                            opt.value = networks[i].ssid;
                            opt.textContent = networks[i].ssid + ' (' + networks[i].rssi + ' dBm)';
                            sel.appendChild(opt);
                        }
                    }
                    btn.disabled = false;
                    btn.textContent = 'Scan';
                }).catch(function() {
                    sel.innerHTML = '<option value="">Scan failed</option>';
                    btn.disabled = false;
                    btn.textContent = 'Scan';
                });
            }
            scanNetworks();
        </script>
    </body>

    </html>
    )rawliteral";

  server.on("/", [&server, htmlForm]()
            { server.send(200, "text/html", htmlForm); });

  server.on("/scan", HTTP_GET, [&server]()
            {
    int n = WiFi.scanNetworks();
    String json = "[";
    for (int i = 0; i < n; i++) {
      String ssid = WiFi.SSID(i);
      if (ssid.length() == 0) continue;
      bool isDuplicate = false;
      for (int j = 0; j < i; j++) {
        if (WiFi.SSID(j) == ssid) { isDuplicate = true; break; }
      }
      if (isDuplicate) continue;
      if (json.length() > 1) json += ",";
      json += "{\"ssid\":\"";
      for (unsigned int c = 0; c < ssid.length(); c++) {
        if (ssid[c] == '"') json += "\\\"";
        else if (ssid[c] == '\\') json += "\\\\";
        else json += ssid[c];
      }
      json += "\",\"rssi\":" + String(WiFi.RSSI(i)) + "}";
    }
    json += "]";
    WiFi.scanDelete();
    server.send(200, "application/json", json); });

  const int STATE_IDLE = 0;
  const int STATE_CONNECTING_WIFI = 1;
  const int STATE_REGISTERING = 2;
  const int STATE_SUCCESS = 3;
  const int STATE_FAILED = 4;

  int pairingState = STATE_IDLE;
  String pairingError;
  DeviceConfig pendingConfig;
  int wifiRetries = 0;

  const char *progressHtml = R"rawliteral(
    <!DOCTYPE html>
    <html lang="en">
    <head>
        <meta charset="UTF-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>izBoard Setup</title>
        <style>
            *, *::before, *::after { box-sizing: border-box; }
            .container { max-width: 960px; margin-right: auto; margin-left: auto; padding-right: 12px; padding-left: 12px; }
            .mt-5 { margin-top: 3rem !important; }
            .text-center { text-align: center !important; }
            .card { position: relative; display: flex; flex-direction: column; min-width: 0; word-wrap: break-word; background-color: #fff; background-clip: border-box; border: 1px solid rgba(0,0,0,.125); border-radius: .25rem; }
            .card-header { padding: .75rem 1.25rem; margin-bottom: 0; background-color: rgba(0,0,0,.03); border-bottom: 1px solid rgba(0,0,0,.125); font-weight: 500; }
            .card-body { flex: 1 1 auto; padding: 1.25rem; }
            .btn { display: inline-block; font-weight: 400; line-height: 1.5; color: #fff; text-align: center; text-decoration: none; vertical-align: middle; cursor: pointer; background-color: #6c757d; border: 1px solid #6c757d; padding: .375rem .75rem; font-size: 1rem; border-radius: .25rem; }
            .spinner { display: inline-block; width: 1rem; height: 1rem; border: 2px solid #0d6efd; border-right-color: transparent; border-radius: 50%; animation: spin .6s linear infinite; vertical-align: middle; margin-right: .5rem; }
            @keyframes spin { to { transform: rotate(360deg); } }
        </style>
    </head>
    <body>
        <div class="container mt-5">
            <h2 class="text-center">izBoard Setup</h2>
            <div class="card">
                <div class="card-header">Checking connection</div>
                <div class="card-body text-center" id="content">
                    <div><span class="spinner"></span>Testing home Wi-Fi and server access.</div>
                    <p>Keep this page open. You can correct the settings here if either connection fails.</p>
                </div>
            </div>
        </div>
        <script>
          function checkStatus() {
            fetch('/pairing-status').then(function(r) { return r.json(); }).then(function(status) {
              if (status.state === 'failed') {
                var content = document.getElementById('content');
                content.innerHTML = '<div style="color:#dc3545;font-weight:500">Connection failed</div>';
                var detail = document.createElement('p');
                detail.textContent = String(status.error || 'Check the settings and try again.');
                content.appendChild(detail);
                var retry = document.createElement('a');
                retry.className = 'btn';
                retry.href = '/';
                retry.textContent = 'Back to setup';
                content.appendChild(retry);
                return;
              }
              setTimeout(checkStatus, 750);
            }).catch(function() {
              document.getElementById('content').innerHTML =
                '<div style="color:#198754;font-weight:500">&#10003; Server reached successfully.</div>' +
                '<p>From any network that can reach the dashboard, enter the claim code shown on the display.</p>';
            });
          }
          setTimeout(checkStatus, 500);
        </script>
    </body>
    </html>
    )rawliteral";

  server.on("/submit", HTTP_POST, [this, &server, htmlForm, progressHtml,
      &pairingState, &pairingError, &pendingConfig, &wifiRetries,
      &claimCode, &registrationToken]()
            {
    const String ssidParam{ "ssid" };
    const String passParam{ "password" };
    const String urlParam{ "server_url" };

    if (!server.hasArg(ssidParam) || !server.hasArg(passParam) || !server.hasArg(urlParam)) {
      server.send(400, "text/html", htmlForm);
      return;
    }

    const String ssid{ server.arg(ssidParam) };
    const String pass{ server.arg(passParam) };
    String rawUrl{ server.arg(urlParam) };

    String host;
    String basePath;
    int port = 0;
    if (!parseServerAddress(rawUrl, host, port, basePath))
    {
      server.send(400, "text/plain",
                  "Invalid server address. Enter it as shown in the dashboard, e.g. 192.168.1.100:8129.");
      return;
    }

    pendingConfig = { ssid, pass, host, port, basePath, "", claimCode, registrationToken, "" };
    _configStore.save(pendingConfig);
    _logger.println("Received configuration, starting WiFi connection...");
    _logger.print("Server: ");
    _logger.print(host);
    _logger.print(":");
    _logger.println(port);

    wifiRetries = 0;
    pairingState = STATE_CONNECTING_WIFI;

    server.send(200, "text/html", progressHtml);

    WiFi.mode(WIFI_AP_STA);
    WiFi.begin(pendingConfig.ssid.c_str(), pendingConfig.password.c_str()); });

  server.on("/pairing-status", HTTP_GET, [&server,
      &pairingState, &pairingError,
      STATE_IDLE, STATE_CONNECTING_WIFI, STATE_REGISTERING, STATE_SUCCESS, STATE_FAILED]()
            {
    String state;
    if (pairingState == STATE_CONNECTING_WIFI) state = "connecting_wifi";
    else if (pairingState == STATE_REGISTERING) state = "registering";
    else if (pairingState == STATE_SUCCESS) state = "success";
    else if (pairingState == STATE_FAILED) state = "failed";
    else state = "idle";

    JsonDocument doc;
    doc["state"] = state;
    if (pairingState == STATE_FAILED)
    {
      doc["error"] = pairingError;
    }
    String json;
    serializeJson(doc, json);
    server.send(200, "application/json", json); });

  auto redirectToRoot = [&server]()
  {
    server.sendHeader("Location", "/", true);
    server.send(302, "text/plain", "Redirecting to setup page...");
  };

  server.on("/generate_204", redirectToRoot);
  server.on("/hotspot-detect.html", redirectToRoot);
  server.on("/ncsi.txt", redirectToRoot);
  server.onNotFound([&server, htmlForm, &redirectToRoot]()
                    {
    if (server.uri() == "/submit") {
      server.send(404, "text/plain", "Not found");
      return;
    }
    redirectToRoot(); });

  server.begin();
  _logger.println("HTTP server started");

  constexpr int maxWifiRetries = 40; // 20 seconds at 500ms intervals

  while (true)
  {
    dnsServer.processNextRequest();
    server.handleClient();

    if (pairingState == STATE_CONNECTING_WIFI)
    {
      if (WiFi.status() == WL_CONNECTED)
      {
        _logger.println("WiFi connected in AP+STA mode");
        _logger.print("STA IP: ");
        _logger.println(WiFi.localIP());
        _logger.println("Registering with server...");
        pairingState = STATE_REGISTERING;

        unsigned long expiresInSeconds = 0;
        String registrationError;
        auto announceResult = _deviceApi.announceDevice(
            pendingConfig.pairingCode, pendingConfig.registrationToken,
            pendingConfig.dashboardUrl, pendingConfig.devicePort, pendingConfig.serverFingerprint,
            pendingConfig.dashboardBasePath, expiresInSeconds, registrationError);
        if (announceResult != PairingAttemptResult::Success)
        {
          _logger.println("Server connection check failed");
          pairingState = STATE_FAILED;
          pairingError = registrationError.length() > 0
              ? registrationError
              : "Could not reach the pairing service";
          continue;
        }

        // Keeps the certificate pinned by the announcement for every later connection.
        _configStore.save(pendingConfig);
        dnsServer.stop();
        server.stop();
        WiFi.softAPdisconnect(true);
        WiFi.mode(WIFI_STA);
        _display.showSuccess(
            "Ready to Claim",
            "Reconnect your phone to home WiFi.\nEnter this code in the dashboard:",
            pendingConfig.pairingCode);

        String apiKey;
        auto pairingResult = _deviceApi.waitForClaim(
            pendingConfig.pairingCode, pendingConfig.registrationToken,
            pendingConfig.dashboardUrl, pendingConfig.devicePort, pendingConfig.serverFingerprint,
            pendingConfig.dashboardBasePath, expiresInSeconds, apiKey, registrationError);
        if (pairingResult == PairingAttemptResult::Success)
        {
          pendingConfig.dashboardApiKey = apiKey;
          pendingConfig.pairingCode = "";
          pendingConfig.registrationToken = "";
          _configStore.save(pendingConfig);
          _logger.println("Registration successful, API key received!");
          pairingState = STATE_SUCCESS;
          _display.showSuccess(
              "Paired Successfully",
              "The device will fetch and display\nassigned dashboards automatically.",
              "Press the button to refresh manually.");
          return pendingConfig;
        }
        else
        {
          _logger.println("Registration failed");
          pairingState = STATE_FAILED;
          pairingError = registrationError.length() > 0 ? registrationError : "Registration failed";
          const bool retryable = pairingResult == PairingAttemptResult::RetryableFailure;
          if (!retryable)
          {
            _configStore.clear();
          }
          _display.showSuccess(
              retryable ? "Pairing Paused" : "Pairing Failed",
              pairingError,
              retryable ? "Retrying automatically..." : "Restarting setup...");
          delay(5000);
          ESP.restart();
        }
      }
      else if (++wifiRetries >= maxWifiRetries)
      {
        _logger.println("WiFi connection failed");
        pairingState = STATE_FAILED;
        pairingError = "WiFi connection failed";
        WiFi.mode(WIFI_AP);
        WiFi.softAP(apName.c_str(), apPassword.c_str());
      }
    }

    delay(pairingState == STATE_CONNECTING_WIFI ? 500 : 2);
  }
}
