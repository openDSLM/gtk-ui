# gtk-ui

GTK4/Adwaita interface for the openDSLM capture stack. It drives the on-device camera daemon, previews the live feed, and provides quick access to capture controls, gallery browsing, and settings.

## Prerequisites

- .NET 8 SDK
- GTK 4, Adwaita, and GStreamer runtime libraries available on the host
- A running [`opendslm-deamon`](https://github.com/openDSLM/opendslm-deamon) instance for camera control (the UI is only a shell around the daemon)

> ℹ️ **Alpha notice**: the current release targets development hardware and assumes the daemon is built from the `main` branch. Behaviour may change as the capture pipeline stabilises.

## Build & Run

```bash
dotnet restore
dotnet run
```

Add `--fullscreen` after the `dotnet run --` separator to launch borderless and hide the title bar:

```bash
dotnet run -- --fullscreen
```

The UI copies its GTK templates and icon assets at build time, so no additional steps are required after publishing.

## Getting Started

1. Clone and build [`opendslm-deamon`](https://github.com/openDSLM/opendslm-deamon); follow its README to flash firmware and configure camera drivers.
2. Start the daemon (`opendslm-deamon --config <path>`). The UI expects the default RPC endpoint (`http://127.0.0.1:8400/`); override via the `OPENDSLM_DAEMON_URL` environment variable if you change the port/host.
3. Launch `dotnet run` from this repository. The UI will connect to the daemon, initialise the preview pipeline, and synchronise settings.

If the UI fails to connect, confirm the daemon is reachable and that the user running `dotnet run` has permission to access the camera device nodes.

## Features

- **Live view**
  - Scroll to zoom and drag to pan while zoomed (useful without focus peaking).
  - Toggle auto exposure, ISO, shutter speed, and capture RAWs directly.
- **Gallery**
  - Browse captures by page; adjust grid rows from the live HUD or Settings → Gallery.
  - Click a thumbnail to open the viewer, scroll to zoom, and drag to inspect edges.
- **Settings**
  - Configure output directory, metadata overrides, gallery thumbnail strategy, and diagnostics.

## Settings & Debugging

- **Gallery**: adjust grid rows using the `- NR +` buttons; optionally build thumbnails from RAW/DNG files (slower) instead of using embedded previews.
- **Debug**: use the “Exit Application” button if the GTK front end needs to be closed without killing the daemon process.

## Troubleshooting

| Symptom | Action |
| --- | --- |
| `Gtk-WARNING … Theme parser error` | Ensure GTK 4.8+ and libadwaita 1.2+ are installed. The UI injects CSS at runtime; older themes may not support the transforms required for zoom/pan. |
| UI launches but shows “No daemon” | Confirm `opendslm-deamon` is running locally or set `OPENDSLM_DAEMON_URL` to the correct host. |
| Gallery grid is slow to update | Disable “RAW decoding” in Settings → Gallery to use embedded thumbnails instead of regenerating previews. |

Keyboard shortcuts and additional capture features are implemented in the daemon; this project focuses on the GUI shell.
