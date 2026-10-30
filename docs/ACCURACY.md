# Prayer-Time Accuracy: How It Is Verified

Accuracy is OptimAdhan's top priority. The engine is checked three independent ways.

## 1. Exact parity with adhan-js (offline, runs on every test run)

`tools/generate-adhan-fixtures.js` runs **adhan-js 4.4.3**, the library used by the web
app, to produce `tests/OptimAdhan.Core.Tests/Fixtures/adhan-js-4.4.3.json.gz`:

- **40 cities**, including Makkah, Dhaka, Jakarta, Cairo, London, Oslo, Reykjavik, Tromsø
  (polar), Anchorage, Sydney, Ushuaia, Quito and Honolulu
- **12 calculation methods × 2 madhabs**
- **About 40 dates:** the 15th of each month, solstices, equinoxes, US/EU/AU DST change days,
  a leap day and Ramadan 1448
- every high-latitude rule and polar-circle resolution for cities above 48°, and user minute adjustments

**Result: all 51,880 cases (about 363,000 times) match to the millisecond.**
(`AdhanJsParityTests`)

To regenerate, from the workspace root:
```
TZ=UTC node tools/generate-adhan-fixtures.js PrayerAndRamadan/node_modules/adhan tests/OptimAdhan.Core.Tests/Fixtures/adhan-js-4.4.3.json.gz
```

## 2. Cross-check against AlAdhan (online, opt-in)

```
OPTIMADHAN_NETWORK_TESTS=1 dotnet test
```
`AlAdhanCrossCheckTests` compares 17 cities × 4 dates × 6 times (408 values), each
city using its matching method. Method minute offsets are passed to AlAdhan with `tune=`
so both sides apply the same adjustments.

| Result (2026-10-28 run) | |
|---|---|
| Fajr, Sunrise, Dhuhr, Maghrib, Isha | all within **1 minute** (rounding) |
| Asr | within **3 minutes**, differing only near the equinoxes |
| Umm al-Qura Isha in Ramadan | AlAdhan used 120 min; adhan-js used 90. **OptimAdhan now uses 120 in Ramadan**, matching AlAdhan exactly |

**Which Asr is right?** An independent brute-force solar-position calculation
(sun altitude every 10 s) gave the true Asr for six cases:

| City, date | True Asr | OptimAdhan | AlAdhan |
|---|---|---|---|
| Los Angeles 2026-03-08 | 16:23:50 | 16:25 | 16:23 |
| Los Angeles 2026-11-01 | 14:37:05 | 14:36 | 14:38 |
| New York 2026-03-08 | 16:22:00 | 16:23 | 16:21 |
| London 2026-11-01 | 14:05:50 | 14:05 | 14:08 |
| Sydney 2026-03-08 | 16:39:20 | 16:39 | 16:41 |
| Makkah 2027-02-20 | 15:53:20 | 15:54 | 15:52 |

OptimAdhan is within about 1 minute in every case; AlAdhan is off by up to 2 minutes. So the
engine's Asr is kept unchanged.

`AlAdhanHijriCrossCheckTests` compared Hijri dates for 60 dates from 2026–2028, including
the days either side of Ramadan 1447, 1448 and 1449: **0 mismatches**.

## 3. Behavioural unit tests

`PrayerScheduleServiceTests` and `AlertPlannerTests` cover:
- time zones resolved from coordinates, and times shown in the location's zone
- local date rollover across midnight
- a US DST change: the wall clock jumps an hour, the UTC instant does not
- polar summer: resolved by default; with resolution off, times are missing, never invented
- the automatic high-latitude rule, user minute adjustments, Imsak offset
- the daily view and the Imsakia producing identical times
- next/current prayer across midnight, skipping Sunrise
- Adhan and reminder planning, per-prayer switches, the Fajr sound, Ramadan countdowns

## Known limits, and what users can adjust

- **Masjid timetables** often add their own minutes or use a different method.
  Per-prayer adjustments (±minutes) let users match them exactly.
- **Hijri dates** follow Umm al-Qura. Where Ramadan starts by local moonsighting, the
  ±2-day offset aligns the Imsakia.
- **Above about 48° latitude**, twilight-based Fajr and Isha need a high-latitude rule.
  The default is adhan's recommendation (seventh of the night above 48°), and users can change it.
- **Adhan recordings:** seven bundled recordings, several at low bitrates (8 to 16 kbps), with
  unconfirmed licences. Replace or license them before public release (see [AUDIO.md](AUDIO.md)).
