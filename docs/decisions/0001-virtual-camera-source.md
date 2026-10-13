# ADR 0001: Virtual camera source

- Status: accepted
- Date: 2026-10-10

## Context

Teams and other video conferencing apps only accept uncompressed frames (NV12, RGB32) from a
camera. The app has to appear as a webcam on a Windows 11 PC, without a kernel driver and without
admin rights at runtime. AGENTS.md asks to evaluate the options and allows
[smourier/VCamNetSample](https://github.com/smourier/VCamNetSample).

## Options

| Option                                    | Notes                                                                                                                                 |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| **MFCreateVirtualCamera** (Windows 11)    | Official API. Needs a COM media source registered system-wide, loaded by the Windows Frame Server (svchost, Local Service). No driver. |
| DirectShow filter (OBS-style)             | Not seen by apps that only use Media Foundation; Teams support has varied over time.                                                  |
| Kernel driver / AVStream                  | Driver signing, admin rights, much larger scope.                                                                                      |
| Own media source from scratch             | COM activation, stream descriptors and Frame Server behaviour are a large, error-prone surface.                                       |

## Decision

- **MFCreateVirtualCamera** with session lifetime and access for the current user.
- **VCamNetSample (MIT) as source code** in `vendor/VCamNetSample/`, pinned to commit
  `5f2e41c8173ffdd62a964d0231aaa782577951c2`, with its own CLSID
  (`c7e7fc74-f2c4-4b11-b47d-01642a9ce7a6`) and the sample frame generator replaced by a reader
  for the shared frame store (ADR 0003). Changes are listed in `vendor/VCamNetSample/PROVENANCE.md`.
- The media source reports fixed 1280 × 720 or 1920 × 1080 at 30 fps as RGB32 and NV12, built
  in system memory; D3D allocators are not enabled.
- Path and output size reach the source through custom `IMFVirtualCamera` attributes.

## Consequences

- Windows 11 22H2 or later is required.
- The COM source is registered once with admin rights (`scripts/RegisterCamera.ps1`, or
  `Install.ps1` for the package). The Frame Server locks its DLLs while loaded, so builds and
  updates must stop it first.
- The app and the COM source must use the same DLLs; the app checks the registered folder and
  SHA256 hashes before it creates the camera.
