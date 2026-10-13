# ADR 0005: Look of the hub test page

- Status: accepted
- Date: 2026-10-12
- Decided by: product owner (the app should look related to the hub, which got a new design and
  icon, see ADR 0010 of drone-live-stream-hub)

## Decision

The window follows the **Tag** look of the hub web UI. No day/night switch. Only design values are
taken over; controls and names are our own:

- **Colours:** ground `#e8eef3`, surfaces `#ffffff`, text `#0b1a28`/`#3b5063`, lines
  `#b9c8d4`/`#6f8597`, accent `#0f6f68`, filled controls `#1d6f68` (hover `#17847b`).
  Status colours stay reserved: live `#0b7a3e`, no signal `#f2b01e`, error `#c4202b`.
- **Type:** Segoe UI; the wordmark "Drohnen-" + "Livestream" in teal with the spaced label
  "FÜR VIDEOKONFERENZEN" (not "für Teams": any conferencing app works).
- **Layout:** fixed client area 780 × 548 px at 100 % scaling. Header with wordmark and the
  read-only server name; drone list as tiles with slot number, room for four entries and
  scrolling beyond; HD / Full HD selector; one button that switches between "Übertragung starten"
  and "Übertragung stoppen"; preview with a bar for number, drone, format and status.
- **Icon:** the hub's navy shield with a teal **video camera and live dot** instead of the
  quadcopter, drawn in code by `BrandGraphics` (`DroneLive.Core`). The same code renders
  `Application.ico` (`scripts/GenerateIcons.ps1`), the tray and taskbar icons with a status dot
  (green connected, yellow no signal, red error) and the "Kein Signal" frame.
- **Flicker-free refresh:** double buffering and `WS_EX_COMPOSITED`; the list replaces only
  changed entries and property setters act only on real changes.

The design draft with all states is in `docs/design/DroneLive2VirtualCamera-Designentwurf.pdf`.

## Consequences

- Behaviour and tests are unchanged apart from the new controls; screenshots of the window are
  written to `artifacts/test-results/` by the test program.
