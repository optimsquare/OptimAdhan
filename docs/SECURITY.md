# Security

## How OptimAdhan handles security and privacy

- **No secrets in the app or installer.** Anything shipped to users can be extracted, so the app
  uses only keyless public services: OpenStreetMap Nominatim, for place names only.
- **No administrator rights.** The app installs per user and writes only to `%AppData%`,
  `%LocalAppData%` and `HKCU` (the start-at-sign-in entry).
- **Privacy by default.** No accounts and no telemetry. Location stays on the device; only
  place-name searches leave the PC. Prayer times are calculated locally.
- **Local diagnostics only.** The log in `%LocalAppData%\Optim Square\OptimAdhan\logs` is never
  uploaded, and it rolls over at 1 MB.
- **Robust settings.** Settings are written atomically. A corrupt file is kept aside and defaults
  are used.
- **Future issue reporting** will post to a hardened server endpoint (input escaping, length
  limits, rate limiting) and never call an email provider directly from the app.

## Reporting a vulnerability

Please don't open a public issue. Use **Security › Report a vulnerability** on this GitHub
repository, which sends a private report to the maintainers.
