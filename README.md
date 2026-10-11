<p align="center" style="margin:0 0 12px 0;">
  <img src="software/EPaperDashboard/frontend/public/icon.svg" alt="izBoard icon" width="140" height="140" style="max-width:140px;" />
</p>

<h1 align="center">izBoard</h1>

<p align="center" style="margin:0 0 20px 0; font-weight:600;">Dashboards for battery-powered e-paper displays, updated on your schedule</p>
<p align="center" style="margin:0 0 16px 0;">
  <a href="software/README.md">Software</a> ·
  <a href="firmware/README.md">Firmware</a> ·
  <a href="packaging/README.md">Packaging</a>
</p>

**izBoard** shows dashboards on a battery-powered e-paper device. Design a layout from widgets fed by your connected data, such as [Home Assistant](https://www.home-assistant.io/) or Google Calendar & Tasks, or show an existing Home Assistant dashboard. The device fetches an updated image from the server at the times you choose and deep-sleeps in between, so a single battery charge can last weeks or even months. See examples of the device and real dashboards below.

<div style="display:flex;gap:8px;flex-wrap:wrap;align-items:center; margin:12px 0 20px 0;">
  <img src="device_setup.jpeg" alt="Device" style="width:32%;max-width:320px;border-radius:6px;" />
  <img src="device_graph.jpeg" alt="Device" style="width:32%;max-width:320px;border-radius:6px;" />
  <img src="device_todo.jpeg" alt="Device" style="width:32%;max-width:320px;border-radius:6px;" />
</div>

## Features

- Drag-and-drop designer for custom layouts built from widgets
- Connections that feed widgets with data from Home Assistant and Google Calendar & Tasks
- Existing Home Assistant dashboards, captured by the optional rendering component
- AI generated dashboards and widget content from a plain-language prompt, using an OpenAI-compatible provider or a Home Assistant conversation agent
- E-paper optimized image processing
- Configurable update schedules with deep sleep for long battery life
- Secure device pairing and OTA firmware updates
- Deploy as standalone Docker or Home Assistant Add-on
- Multi-user support with administration (standalone mode)
- LiteDB storage by default, or PostgreSQL for hosted deployments

## How It Works

izBoard has three components that work together:

**1. [Firmware](firmware/)** – Runs on the ESP32 device. Connects to the server, fetches dashboard images on a schedule, displays them on the e-paper screen, and deep-sleeps to save battery. Handles OTA firmware updates, secure device pairing, and initial setup via a captive portal.

**2. [Software](software/)** – ASP.NET Core server application with an Angular web interface. Renders dashboards as e-paper optimized images, manages devices, schedules and data connections, and captures Home Assistant dashboards through an optional rendering component. Runs in Docker or as a Home Assistant Add-on.

**3. [Packaging](packaging/)** – 3D-printable enclosure designs for the device hardware.

## Getting Started

1. **Deploy the server** using Docker (see [Software README](software/README.md)) or the [Home Assistant Add-on](https://github.com/beetnode/hass-add-ons/tree/master/e-paper-dashboard)
2. **Flash the firmware** to your ESP32 device with an e-paper display (see [Firmware README](firmware/README.md))
3. **Pair the device** — scan the setup Wi-Fi QR, enter Wi-Fi and server details, then claim the display code from any network that can reach the server
4. **Connect your data** on the Connections page, such as Home Assistant or Google Calendar & Tasks
5. **Create a dashboard** — design a custom layout, or show a Home Assistant dashboard view (enable it first under **Settings → Rendering**)
6. **Assign the dashboard** to your device and configure the update schedule

See the individual component READMEs for detailed setup instructions.

## License

This project is licensed under the GNU Affero General Public License v3.0 (AGPL-3.0) — see the [LICENSE](LICENSE) file for details.

### Third-Party License Notice

This software uses [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp), which is subject to the [Six Labors Split License](https://www.sixlabors.com/pricing/). Commercial entities with gross annual revenue exceeding $1M USD may require a separate commercial license from Six Labors. See https://sixlabors.com/pricing/ for details.
