# gtk-ui

GTK4/Adwaita interface for the openDSLM capture stack. It drives the on-device camera daemon, previews the live feed, and provides quick access to capture controls, gallery browsing, and settings.

## Prerequisites

- .NET 8 SDK
- GTK 4, Adwaita, and GStreamer runtime libraries available on the host
- A running `opendslm-deamon` instance for camera control

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

## Settings & Debugging

- **Gallery**: adjust grid rows using the `- NR +` buttons; optionally build thumbnails from RAW/DNG files (slower) instead of using embedded previews.
- **Debug**: use the “Exit Application” button if the GTK front end needs to be closed without killing the daemon process.

Keyboard shortcuts and additional capture features are implemented in the daemon; this project focuses on the GUI shell.
