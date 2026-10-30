# Changelog

All notable changes to OptimAdhan. Versions follow `<Version>` in `src/OptimAdhan.App/OptimAdhan.App.csproj`,
which also names the installer.

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
