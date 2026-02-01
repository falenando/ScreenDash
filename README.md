# ScreenDash - Local Remote Support

This repository contains a simple local LAN remote support tool: a Host application (shares screen) and a Viewer application (connects to host and views screen stream).

Configuration
- Each application reads a JSON config file from its executable directory:
  - Host: `hostconfig.json`
  - Viewer: `viewerconfig.json`
- The config file currently supports:
  - `Port` (integer): TCP port used for connections. Default: 5050.
  - `Language` (string): two-letter language code (`en`, `pt`, `es`). Default: `en`.

Localization
- Each app looks for localized strings in the `locales` subdirectory in the application folder, e.g. `locales/en.json`, `locales/pt.json`, `locales/es.json`.
- If the locale file is not present, the app falls back to English.

Protocol
- The viewer connects to the host IP on the configured `Port` and sends the ASCII string `REQUEST_STREAM`.
- Host replies by streaming JPEG frames. Each frame is sent as an 8-digit ASCII length header (decimal, zero-padded), followed by the raw JPEG bytes.

Notes
- The system enforces that host and viewer are on the same /24 LAN network. Access codes encode last octet and are resolved using the viewer's network prefix.
- This tool is intended for LAN use only.

