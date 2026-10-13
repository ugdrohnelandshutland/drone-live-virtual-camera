# ADR 0003: Frame exchange between app and camera source

- Status: accepted
- Date: 2026-10-10

## Context

The app decodes frames in its own process, while the Windows Frame Server loads the camera source
in a different process and account (Local Service). Frames have to cross that boundary with low
latency, without queues that grow, and without ever showing half-written frames. A first version
with a single slot produced false "Kein Signal" frames when writer and reader collided; a reader
cache alone did not fix sustained overlaps.

## Decision

- **Memory-mapped file** under `%ProgramData%\DroneLive2VirtualCamera\Frames`, readable by
  Local Service, with **three fixed slots** (protocol version 2).
- The writer fills a free slot and publishes it with an atomic pointer; per-slot sequence
  counters (seqlock) detect a writer that overtakes a reader during the copy. Pixels are copied
  as one memory block.
- Each reader keeps the last consistent frame as a short reserve. Freshness uses the monotonic
  Windows uptime, not the system clock: after **2 seconds** without a new slot the source outputs
  "Kein Signal", even if the app hangs or crashes. An explicit "Kein Signal" from the app also
  invalidates the cached frame.
- Frame files of aborted sessions are removed at camera start, but only if no other process
  (a running app or the Frame Server) still has them open.

## Consequences

- No growing queue: raw frames are replaced, never buffered.
- App and COM source must always be updated together; a protocol change bumps the version.
