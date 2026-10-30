# OptimAdhan

**Your Daily Prayer Companion** — a lightweight Windows 10/11 app by **Optim Square LLC**.

Accurate daily prayer times for any location, the Adhan played automatically in the
background, a desktop widget, a monthly timetable and a Ramadan Imsakia.

> `PrayerAndRamadan/` is the original web app, kept **read-only** as a reference.
> Nothing in this solution modifies it.

## What's in v0.3

| Area | Status |
|---|---|
| Prayer-time engine (12 methods, madhab, high-latitude and polar rules, minute adjustments) | ✅ verified (see docs/ACCURACY.md) |
| Location: Windows detection, OpenStreetMap search, manual coordinates; own time zone and DST | ✅ |
| Background Adhan: tray app, start at sign-in, separate Fajr sound, per-prayer switches, reminders, mute next/1 hour, missed-Adhan notice after sleep | ✅ |
| Main window: Today, monthly Timetable, Ramadan Imsakia, Settings, About | ✅ |
| Desktop widget: standard and compact layouts, Ramadan mode, drag/lock, opacity, keep on top | ✅ |
| Ramadan: Sehri/Iftar times, Hijri offset, adjustable Imsak, spoken countdown reminders | ✅ |
| Per-user MSI installer (no admin), clean uninstall | ✅ |
| **Premium Midnight design:** sidebar app, hero countdown, frosted-glass widget and alerts, dark tray menu, brand assets | ✅ v0.2 |
| Adaptive sidebar, F11 full-screen dashboard, rolling timetable, Now Playing Adhan card | ✅ v0.3 |
| About page with voluntary **Support Our Work** (Zelle QR, PayPal link) and Report an issue | ✅ v0.3 |

Screenshots: [docs/screenshots](docs/screenshots). Release notes: [CHANGELOG.md](CHANGELOG.md).

## Build, test, run

Requires the **.NET 10 SDK**. The installer also needs WiX v5
(`dotnet tool install -g wix --version 5.0.2` and `wix extension add -g WixToolset.Util.wixext/5.0.2`).

```powershell
dotnet test                                    # 56 offline tests, including 51,880 adhan-js parity cases
$env:OPTIMADHAN_NETWORK_TESTS = "1"; dotnet test   # + cross-checks against the AlAdhan API (needs internet)

$env:OPTIMADHAN_DEV = "1"                      # developer mode: separate settings, never touches the Run key
dotnet run --project src/OptimAdhan.App

powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1
# -> artifacts\installer\OptimAdhan-<version>-x64.msi
```

The MSI installs per user to `%LocalAppData%\Programs\OptimAdhan`. Users need the
**.NET 10 Desktop Runtime**; if it is missing, Windows offers the download when the app
first starts.

## Where things live (on a user's PC)

| What | Where |
|---|---|
| Settings | `%AppData%\Optim Square\OptimAdhan\settings.json` |
| Diagnostic log (local only) | `%LocalAppData%\Optim Square\OptimAdhan\logs` |
| Start at sign-in | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\OptimAdhan` |
| Sounds (replaceable) | `<install folder>\Assets\Audio\sounds.json` and audio files |

## Replacing the Adhan recordings
Put the new files in `src/OptimAdhan.App/Assets/Audio/Adhan/` and list them in
`Assets/Audio/sounds.json` (id, name, file). No code changes are needed. Settings refer to
sounds by `id`, so keep `makkah`/`madina` or users will fall back to the first sound listed.

## Brand assets
The app uses the official artwork from `assets/`, copied into `src/OptimAdhan.App/Assets/Brand/`:
`optimadhan.ico` (app, windows and installer), `symbol.png` (sidebar, widget, alerts) and the tray
crescents `tray-gold.ico` / `tray-dark.ico` (chosen to match the taskbar's light or dark setting).
To update the branding, replace those files and rebuild. `tools/make-temp-icon.ps1` only produced the
early placeholder icon.

## Documentation
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): technical decisions and milestones
- [docs/ACCURACY.md](docs/ACCURACY.md): how prayer-time accuracy is verified, with results
- [docs/SECURITY.md](docs/SECURITY.md): security and privacy practices, and how to report a vulnerability

## Licence
© Optim Square LLC. All rights reserved. The source is public for transparency and review. No
licence to reuse or redistribute it is granted unless stated otherwise.

Third-party components keep their own licences: the prayer-time algorithms are ported from
[adhan-js](https://github.com/batoulapps/adhan-js) (MIT, Batoul Apps);
[GeoTimeZone](https://github.com/mattjohnsonpint/GeoTimeZone) (MIT); the
[Amiri](https://github.com/aliftype/amiri) font (SIL OFL 1.1); place names © OpenStreetMap
contributors via Nominatim.
