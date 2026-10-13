# ADR 0004: Camera lifetime and packaging

- Status: accepted
- Date: 2026-10-12
- Decided by: product owner ("Teams loses the camera when switching drones")

## Context

The first version created the virtual camera when a stream started and removed it when the stream
stopped. Teams then lost the camera on every switch between drones and had to select it again.

The conference PC is set up by non-developers. The app needs FFmpeg, .NET 10 Desktop Runtime and
a system-wide COM registration, and later versions must be installable over existing ones.

## Decision

- **The camera lives as long as the app.** It is created at program start and removed only on
  exit. Stopping and starting a transmission only writes frames or "Kein Signal" into the existing
  frame store; Teams keeps the camera. Only a changed output size (HD / Full HD) needs a new
  camera session.
- **One ZIP** built by `scripts/BuildPackage.ps1` with `Install.ps1`, the application files,
  licence notes, a manifest with SHA256 hashes (format 2, with `Paketstand`) and **one bundled
  `ffmpeg.exe`**. `Install.ps1` installs FFmpeg to `FFmpeg\` in the install folder only if none
  is found (configured path, PATH, `C:\Program Files\FFmpeg`), and stores it relative to the EXE.
- **Updates with the same script:** it detects an existing installation, stops a running app
  after asking, stops the Frame Server, backs up the old installation and restores it if copying
  or registration fails. `installation.json` remembers the delivered files so that later updates
  remove files no longer shipped; `configuration.json` is never overwritten.
- New packages default to `http://localhost:4444`; the development configuration keeps the test
  hub.

## Consequences

- The package grows to about 60 MB because of FFmpeg; FFmpeg is GPLv3, so the licence text and
  the build configuration are shipped in `licenses/`.
- The .NET 10 Desktop Runtime stays a separate prerequisite.
- Installations made before `installation.json` existed get no file cleanup on their first
  update.
