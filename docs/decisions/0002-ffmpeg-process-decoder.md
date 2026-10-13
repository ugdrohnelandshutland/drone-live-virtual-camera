# ADR 0002: FFmpeg as a separate decoder process

- Status: accepted
- Date: 2026-10-10

## Context

The hub forwards H.264 in FLV unchanged. The app has to decode it, scale it to the fixed camera
size and hand over raw frames, preferring hardware decoding with a software fallback. The
receive loop must never be blocked by decoding.

## Decision

- **`ffmpeg.exe` runs as a child process**; no native FFmpeg bindings. The app converts AVCC to
  Annex B, inserts SPS/PPS before keyframes and writes the stream to stdin; FFmpeg returns scaled
  BGRA frames on stdout (`scale` and `pad` keep the aspect ratio with black bars).
- **`-hwaccel auto`** prefers available hardware decoders and falls back to software;
  `Hardwaredekodierung` in `configuration.json` switches it off for problematic drivers.
- A new, different sequence header renews the decoder and waits for a keyframe, even without
  reconnecting. Close code 4001 discards the decoder and reconnects at once.
- The path to `ffmpeg.exe` comes from `configuration.json` (absolute, relative to the EXE,
  environment variables or a name in PATH), never from the UI.

## Consequences

- No FFmpeg build has to be linked or shipped with the source; the package ships one binary
  separately (ADR 0004).
- Scaling and colour conversion run in system memory; there is no end-to-end GPU pipeline.
- The original FLV timestamps are not passed to FFmpeg. The camera has its own 30 Hz clock and
  always shows the newest frame — a live preview, not a recording.
