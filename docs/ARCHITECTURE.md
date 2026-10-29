# OptimAdhan — Architecture & Technical Decisions

**Product:** OptimAdhan · **Developer:** Optim Square LLC · *Your Daily Prayer Companion*

The reference web app lives in `PrayerAndRamadan/` and is **read-only**: nothing in this
solution modifies it. Assets are copied out of it, never referenced in place.

## Solution layout

```
OptimAdhan.slnx
src/OptimAdhan.Core/        Pure .NET 10 library: calculation, Hijri, schedules, alerts, settings. No UI, no Windows APIs.
src/OptimAdhan.App/         WPF app: tray, background scheduler, audio, main window, widget.
tests/OptimAdhan.Core.Tests xUnit tests, including the adhan-js parity fixture.
tools/                      Fixture generator and build helpers.
installer/                  WiX MSI project.
docs/                       This file, ACCURACY.md, SECURITY.md.
```

## Decisions

### D1. .NET 10 (LTS) + WPF with the built-in Fluent theme
Native Windows look on Windows 10 and 11, low memory, easy transparent widget windows,
and support until November 2028. Business logic lives in `OptimAdhan.Core`, so it is
fully testable without the UI.

### D2. Prayer times: a local engine, verified against both reference sources
The engine is a line-by-line C# port of **adhan-js 4.4.3**, the library the web app already
uses for its daily view (`src/OptimAdhan.Core/Calculation`).

- **Parity:** the C# engine reproduces adhan-js **to the millisecond** on 51,880 cases
  (about 363,000 times). Details are in ACCURACY.md.
- **Independent check:** it agrees with the **AlAdhan API** (the source for the web app's
  Ramadan calendar) to within 1 minute for every prayer except Asr. An independent
  solar-position computation showed our Asr is the more accurate of the two.
- **Why not call the API at runtime?** Alarms must fire with no internet, at exactly the
  same time the screen shows. With one deterministic engine, the daily view, the monthly
  timetable, the Imsakia and the Adhan scheduler always agree. That fixes the web app's
  split, where daily times came from adhan-js and the Ramadan calendar from AlAdhan.
  AlAdhan is still used, as an independent validator in the test suite.
- **No fabricated times, ever.** If a time cannot be calculated (for example, in polar
  regions with resolution turned off), it is `null` and the UI says so. The web app's
  hard-coded fallback times (05:00, 12:30, …) are not carried over.
- **Improvement over both sources:** Umm al-Qura's Isha is 120 minutes after Maghrib
  during Ramadan, 90 otherwise. AlAdhan does this, adhan-js does not, and OptimAdhan does.

### D3. Time zones and daylight saving
- Every location stores its **own IANA time zone**. It is resolved offline from the
  coordinates by `GeoTimeZone` (bundled boundary data), so a city chosen in another
  country shows that city's local times and day boundaries. The web app instead showed
  times in the browser's time zone.
- All instants are UTC internally. Conversion uses `TimeZoneInfo`, which follows the
  daylight saving rules Windows Update keeps current. Alarms are scheduled on UTC
  instants, so DST changes cannot shift or repeat them.
- When the location's zone differs from the PC's, the UI labels the times with the zone.

### D4. Location
1. **Detect:** Windows location services (`Windows.Devices.Geolocation`), which respect
   the user's Windows privacy settings.
2. **Search:** OpenStreetMap Nominatim, the same free service as the web app. The app
   follows Nominatim's usage policy: identifying User-Agent, at most 1 request per second,
   cached results, and searches only when the user presses Enter.
3. **Manual:** coordinates can be entered directly (works offline).

Only the place *name* needs the network. The coordinates and time zone are all the
calculation needs. The calculation method is suggested by country, using the web app's
mapping, until the user picks one.

### D5. Hijri calendar
.NET's built-in `UmAlQuraCalendar` (offline), plus a user offset of ±2 days for local
moonsighting. It matched AlAdhan's conversion API on all 60 sampled dates, including the
days either side of Ramadan 1447–1449.

### D6. Background operation
- **One per-user process** that lives in the system tray and optionally starts at sign-in
  (`HKCU\…\Run`). It is *not* a Windows Service: services run in Session 0 and cannot play
  audio to the user.
- **Single instance:** launching again just opens the main window.
- **Scheduler:** the pure `AlertPlanner` (Core) produces the next 48 hours of alerts, and
  a timer wakes at the next alert, or at most every 30 s. The timer also re-plans on
  resume from sleep, clock changes, time zone changes, midnight and settings changes. An
  alert more than 2 minutes late, for example because the PC was asleep, plays no audio;
  a missed Adhan shows a quiet "Maghrib began at 18:12" notice instead.
- **Low resource use:** the main window is closed, not hidden, so its memory is freed.
  The widget redraws once a second only while it is visible.

### D7. Audio
WPF `MediaPlayer` (Windows Media Foundation: MP3, WAV, AAC/M4A, WMA), with no extra
dependency. Sounds are listed in `Assets/Audio/sounds.json`, so licensed recordings can be
dropped in later without code changes. Fajr has its own sound setting. The development
build uses the web app's recordings (see ACCURACY.md for their limits).

### D8. Notifications
Our own small, always-on-top "Adhan playing" panel with **Stop** and **Mute**, plus tray
notifications for reminders. A custom panel needs no app registration, stays out of
Notification Center and is not silenced by Focus Assist. That matters for the Adhan.

### D9. Installer
A WiX v5 **MSI** (`installer/Package.wxs`, built by `tools/build-installer.ps1`), installed
per user to `%LocalAppData%\Programs\OptimAdhan` with no admin rights. It adds a Start menu
shortcut and the sign-in entry, closes a running copy before upgrading or uninstalling,
starts the app when done, and uninstalls cleanly. The app is framework-dependent on the
.NET 10 Desktop Runtime: 9 MB, against 204 MB uncompressed for a self-contained build. Code signing,
auto-update and Microsoft Store are planned for later versions.

### D10. Settings and privacy
JSON in `%AppData%\Optim Square\OptimAdhan\settings.json`, written atomically. A corrupt
file is kept for diagnosis and defaults are used. No telemetry, no accounts, and **no API
keys in the app**. Network use: Nominatim (place names) only.

## Milestones
1. **Core engine** ✅ calculation, Hijri, time zones, schedules, alert planning, settings, tests.
2. **Background app** ✅ tray, single instance, scheduler, Adhan audio, alert panel, start-up, mute.
3. **Main window** ✅ today, monthly timetable, Ramadan Imsakia, settings, first-run setup.
4. **Desktop widget** ✅ compact/standard, Ramadan mode, position lock, opacity, on-top.
5. **Installer** ✅ per-user MSI (9 MB), validated, install/uninstall tested.

## D11. Premium Midnight design system (v0.2)
- **Theme:** `src/OptimAdhan.App/Themes/Brand.xaml` holds the palette (midnight, deep blue, gold, champagne,
  emerald, ivory), type styles (Segoe UI Variable, Amiri for Arabic), glass card brushes, the eight-point
  star motif, and full templates for buttons, switches, segmented controls, chips, navigation, and tables.
  It overrides the Windows accent colour, so system purple never appears.
- **Brand assets** come from `assets/`: the official icon, the dimensional symbol, and the tray crescents
  (gold on a dark taskbar, navy on a light one; they switch automatically).
- **Materials:**
  - **Widget and Adhan alert:** Windows 11 acrylic, drawn by DWM on the GPU at no CPU cost to the app.
    They are kept visually active so the blur also shows on a window that never takes focus, with a
    midnight tint on top to keep text readable on any wallpaper.
  - **Fallback:** an opaque midnight panel on Windows 10, or when "Transparency effects" is off.
  - **Main window:** solid midnight with painted light, which looks the same on Windows 10 and 11.
    Mica was tried and dropped because the native caption buttons showed through it.
- **Motion:** 220 ms page fade, 140 ms switch thumb, 260 ms alert rise, nothing that loops. All of it is
  skipped when Windows animation effects are off.
- **Redraws:** the widget wakes once a minute, and every second only during a countdown's final hour.
  The progress ring redraws only when its value changes.
- **Accessibility:** gold focus rings on every control, automation names on icon buttons, contrast-checked
  text colours, and the main window reflows for small or high-scaling screens (minimum 820×580 DIP).

## Measured resource use (v0.1, Release build)

| State | CPU (one core) | Private memory |
|---|---|---|
| Tray only, widget hidden | 0.00 % | 21 MB |
| Widget visible | 0.21 % | 33 MB |

v0.2 (redesign), same machine and method:

| State | CPU (one core) | Private memory |
|---|---|---|
| Startup to main window visible | — | 0.83 s |
| Tray only, widget hidden | 0.00 % | 21 MB |
| Widget visible (acrylic glass) | 0.08 % | 34 MB |
| Main window open on Today (live seconds countdown) | 1.8 % | 53 MB |

The widget shows seconds only during the final hour before a prayer, so it redraws about
once a minute. It has no visual effects (a DropShadowEffect made the transparent window
redraw in software every second, costing 1.7 % CPU).

## Before public release
- **Runtime prerequisite:** wrap the MSI in a WiX Burn `setup.exe` that installs the .NET 10
  Desktop Runtime when missing. Today, .NET's own launcher offers the download on first run.
- **Code signing**, so SmartScreen does not warn. Then auto-update and Microsoft Store.
- **Licensed full-length Adhan recordings**, including a Fajr Adhan.
- **Final brand icon and logo.**
- Optional size cut: `Microsoft.Windows.SDK.NET.dll` (25 MB uncompressed, about 6 MB in the MSI)
  is there only for location detection. Hand-written WinRT interop for `Geolocator`
  would bring the MSI to about 4 MB.

Later: Qibla, Islamic events, languages, printable Imsakia, Windows 11 Widgets Board.
