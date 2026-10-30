# Changelog

All notable changes to OptimAdhan. Versions follow `<Version>` in `src/OptimAdhan.App/OptimAdhan.App.csproj`,
which also names the installer.

## 0.3.1 — 2026-10-30

### Installer: no prerequisites
- The installer is now self-contained: the .NET 10 runtime (including WPF and WinForms) is bundled in
  the app folder, so OptimAdhan installs and starts with nothing else on the PC. Previously Windows
  asked users to download the .NET 10 Desktop Runtime first.
- The MSI is about 63 MB (0.3.0 was 10 MB). Unused framework translations and debug symbols are left out.
- Same upgrade code, so it replaces 0.3.0; install location, shortcuts, sign-in entry and settings are unchanged.

### Settings and About layout
- Settings and About now use the window's width: cards sit in two columns when there is room
  (sidebar collapsed, larger or maximized windows) and fall back to one column, in reading order, on
  smaller windows. Content is capped at the same 1240 px as Today and centred; cards keep their natural
  height and are never stretched.
- Settings cards share one header style (icon, title, one-line hint): Location, Prayer times,
  Adhan and sounds, Notifications, Ramadan, Desktop widget and Windows startup. Every setting is unchanged;
  advanced options stay in the Fine-tune and coordinates expanders.
- About: full-width brand header with feature badges, then Support Our Work beside Developer & Support
  and Privacy. Licences and acknowledgments fold into an expander. Zelle QR and PayPal link unchanged.
- New screenshots of every page and the widget in `docs/screenshots`.

## 0.3.0 — 2026-10-29

### About page and Support Our Work
- Redesigned About page: logo, tagline, installed version, "Developed by Optim Square LLC" and a short
  description of the app.
- New **Support Our Work** card with the same two donation options as the PrayerAndRamadan web app:
  - **Zelle**: expands to show the original Zelle QR code (an unmodified copy of the web app's
    `public/ZelleCode.png`) on a white quiet zone, and collapses again. Checked to decode to the original
    payload at display scales from 100% to 300%.
  - **PayPal**: opens `https://paypal.me/RRAZU` in the default browser.
  Contributions are voluntary. There are no donation popups or reminders, and OptimAdhan never collects or
  processes payment details.
- New **Developer & Help** links: the Optim Square LLC website and Report an issue (GitHub Issues). The
  settings-folder and log-folder buttons stay on this card.

### Adaptive navigation and full-screen dashboard
- Three sidebar modes (expanded, icon rail, hidden), switched with the pane button, Ctrl+B or the
  title-bar layout menu, and remembered between sessions.
- F11 full-screen Today dashboard on the current monitor; Esc or F11 restores the previous window state.
- Window size and maximized state are remembered; Ctrl+1..5 open the pages.
- Responsive Today page, with a rolling day timetable that becomes the Imsakia during Ramadan.

### Adhan popup and widget
- Compact "Now Playing" Adhan card driven by real playback position, with Hide and Stop Adhan.
- Widget countdown: hours and minutes, then live seconds in the final ten minutes.
- Real Amiri font (SIL OFL) for Arabic prayer names; glass polish across cards.

## 0.2.0

- First public build: prayer-time engine, background Adhan in the system tray, main window, desktop
  widget, Ramadan Imsakia and the per-user MSI installer, in the Premium Midnight design.
