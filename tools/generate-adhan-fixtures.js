// Generates reference prayer times with adhan-js, the library the web app uses,
// so the C# port can be checked against it case by case.
//
// Usage (TZ=UTC makes adhan-js read the calendar date exactly as given):
//   TZ=UTC node tools/generate-adhan-fixtures.js <path-to-adhan-package> <output.json>
// Example:
//   TZ=UTC node tools/generate-adhan-fixtures.js PrayerAndRamadan/node_modules/adhan tests/OptimAdhan.Core.Tests/Fixtures/adhan-js-4.4.3.json.gz

const path = require("path");
const fs = require("fs");

const [adhanPath, outPath] = process.argv.slice(2);
if (!adhanPath || !outPath) {
  console.error("usage: node generate-adhan-fixtures.js <adhan-package-dir> <output.json>");
  process.exit(1);
}
if (new Date(2026, 0, 1).getTimezoneOffset() !== 0) {
  console.error("Run with TZ=UTC so calendar dates are interpreted exactly.");
  process.exit(1);
}

const adhan = require(path.resolve(adhanPath));
const version = require(path.resolve(adhanPath, "package.json")).version;

const cities = [
  ["Makkah", 21.4225, 39.8262],
  ["Madinah", 24.4672, 39.6111],
  ["Dhaka", 23.8103, 90.4125],
  ["Karachi", 24.8607, 67.0011],
  ["Delhi", 28.6139, 77.209],
  ["Jakarta", -6.2088, 106.8456],
  ["Kuala Lumpur", 3.139, 101.6869],
  ["Singapore", 1.3521, 103.8198],
  ["Istanbul", 41.0082, 28.9784],
  ["Cairo", 30.0444, 31.2357],
  ["Casablanca", 33.5731, -7.5898],
  ["Lagos", 6.5244, 3.3792],
  ["Nairobi", -1.2921, 36.8219],
  ["Cape Town", -33.9249, 18.4241],
  ["Dubai", 25.2048, 55.2708],
  ["Doha", 25.2854, 51.531],
  ["Kuwait City", 29.3759, 47.9774],
  ["Tehran", 35.6892, 51.389],
  ["London", 51.5074, -0.1278],
  ["Paris", 48.8566, 2.3522],
  ["Berlin", 52.52, 13.405],
  ["Oslo", 59.9139, 10.7522],
  ["Stockholm", 59.3293, 18.0686],
  ["Reykjavik", 64.1466, -21.9426],
  ["Tromso", 69.6492, 18.9553],
  ["New York", 40.7128, -74.006],
  ["Chicago", 41.8781, -87.6298],
  ["Los Angeles", 34.0522, -118.2437],
  ["Toronto", 43.6532, -79.3832],
  ["Edmonton", 53.5461, -113.4938],
  ["Anchorage", 61.2181, -149.9003],
  ["Mexico City", 19.4326, -99.1332],
  ["Sao Paulo", -23.5505, -46.6333],
  ["Buenos Aires", -34.6037, -58.3816],
  ["Sydney", -33.8688, 151.2093],
  ["Auckland", -36.8485, 174.7633],
  ["Honolulu", 21.3069, -157.8583],
  ["Tokyo", 35.6762, 139.6503],
  ["Quito", -0.1807, -78.4678],
  ["Ushuaia", -54.8019, -68.303],
];

// The 15th of every month of 2026, plus solstices, equinoxes, DST change days
// (US, EU, AU), leap day 2028, and the whole of Ramadan 1448 (Feb–Mar 2027).
const dates = [];
for (let m = 1; m <= 12; m++) dates.push([2026, m, 15]);
dates.push(
  [2026, 3, 8], [2026, 3, 29], [2026, 4, 5], [2026, 10, 4], [2026, 10, 25], [2026, 11, 1],
  [2026, 6, 21], [2026, 12, 21], [2026, 3, 20], [2026, 9, 23], [2026, 1, 1], [2026, 12, 31],
  [2028, 2, 29], [2030, 7, 4],
);
for (let d = 0; d < 30; d += 3) {
  const dt = new Date(Date.UTC(2027, 1, 8 + d));
  dates.push([dt.getUTCFullYear(), dt.getUTCMonth() + 1, dt.getUTCDate()]);
}

const methods = [
  "MuslimWorldLeague", "Egyptian", "Karachi", "UmmAlQura", "Dubai", "MoonsightingCommittee",
  "NorthAmerica", "Kuwait", "Qatar", "Singapore", "Tehran", "Turkey",
];
const madhabs = { Shafi: adhan.Madhab.Shafi, Hanafi: adhan.Madhab.Hanafi };
const highLatRules = {
  MiddleOfTheNight: adhan.HighLatitudeRule.MiddleOfTheNight,
  SeventhOfTheNight: adhan.HighLatitudeRule.SeventhOfTheNight,
  TwilightAngle: adhan.HighLatitudeRule.TwilightAngle,
};
const polar = {
  Unresolved: adhan.PolarCircleResolution.Unresolved,
  AqrabBalad: adhan.PolarCircleResolution.AqrabBalad,
  AqrabYaum: adhan.PolarCircleResolution.AqrabYaum,
};

const ms = (d) => (d instanceof Date && !isNaN(d.getTime()) ? d.getTime() : null);
const cases = [];

function add(city, lat, lon, [y, m, d], method, madhab, hlr, pcr, adjustments) {
  const params = adhan.CalculationMethod[method]();
  params.madhab = madhabs[madhab];
  params.highLatitudeRule = highLatRules[hlr];
  params.polarCircleResolution = polar[pcr];
  if (adjustments) Object.assign(params.adjustments, adjustments);
  const t = new adhan.PrayerTimes(new adhan.Coordinates(lat, lon), new Date(y, m - 1, d), params);
  cases.push({
    city, lat, lon, date: `${y}-${String(m).padStart(2, "0")}-${String(d).padStart(2, "0")}`,
    method, madhab, highLatitudeRule: hlr, polarCircleResolution: pcr, adjustments: adjustments || null,
    fajr: ms(t.fajr), sunrise: ms(t.sunrise), dhuhr: ms(t.dhuhr), asr: ms(t.asr),
    sunset: ms(t.sunset), maghrib: ms(t.maghrib), isha: ms(t.isha),
  });
}

for (const [city, lat, lon] of cities) {
  const highLat = Math.abs(lat) > 48;
  for (const date of dates) {
    for (const method of methods) {
      for (const madhab of Object.keys(madhabs)) {
        add(city, lat, lon, date, method, madhab, "MiddleOfTheNight", "Unresolved");
      }
      if (highLat) {
        add(city, lat, lon, date, method, "Shafi", "SeventhOfTheNight", "Unresolved");
        add(city, lat, lon, date, method, "Shafi", "TwilightAngle", "Unresolved");
        add(city, lat, lon, date, method, "Shafi", "SeventhOfTheNight", "AqrabBalad");
        add(city, lat, lon, date, method, "Shafi", "SeventhOfTheNight", "AqrabYaum");
      }
    }
  }
  add(city, lat, lon, [2026, 6, 15], "MuslimWorldLeague", "Shafi", "MiddleOfTheNight", "Unresolved",
    { fajr: 2, sunrise: -1, dhuhr: 3, asr: -4, maghrib: 5, isha: -6 });
}

fs.mkdirSync(path.dirname(outPath), { recursive: true });
const json = JSON.stringify({ generator: `adhan-js ${version}`, cases });
fs.writeFileSync(outPath, outPath.endsWith(".gz") ? require("zlib").gzipSync(json, { level: 9 }) : json);
console.log(`Wrote ${cases.length} cases from adhan-js ${version} to ${outPath}`);
