# Adhan audio library

OptimAdhan bundles seven Adhan recordings. Users only ever see generic names, **Adhan 1** to
**Adhan 7**. Reciters, mosques and source titles are not shown anywhere in the app, and the packed
files carry none of the original tags.

## Library

| ID | Name in the app | Length | Format of the source | Notes |
|---|---|---|---|---|
| `adhan-1` | Adhan 1 | 3:39 | MP3 128 kbps, 44.1 kHz stereo | Best quality. **Default for new installs.** |
| `adhan-2` | Adhan 2 | 3:22 | MP3 16 kbps, 16 kHz mono | Low bitrate: acceptable on laptop speakers, thin on good ones |
| `adhan-3` | Adhan 3 | 3:24 | MP3 16 kbps, 16 kHz mono | Low bitrate; may be the same muezzin as Adhan 2 (listen to confirm) |
| `adhan-4` | Adhan 4 | 3:05 | MP3 16 kbps, 16 kHz mono | Low bitrate |
| `adhan-5` | Adhan 5 | 3:21 | MP3 8 kbps, 11 kHz mono | Formerly "Makkah" (alias `makkah`). Very low bitrate |
| `adhan-6` | Adhan 6 | 1:36 | MP3 8 kbps, 11 kHz mono | Formerly "Madinah" (alias `madina`). Very low bitrate |
| `adhan-7` | Adhan 7 | 3:07 | MP3 64 kbps VBR, 22 kHz stereo | A Fajr Adhan (tagged "أذان الفجر"), so it includes "as-salatu khayrun min an-nawm" |

All seven decode and play through Windows Media Foundation (checked on Windows 11). Lengths were
measured with Media Foundation.

## How it fits together

```
assets/audio/adhan-source/            originals, never modified (MP3s are git-ignored)
  library.json                        which source becomes which adhan-N
tools/pack-adhan-audio.cs             strips ID3 tags, packs, verifies the round trip
src/OptimAdhan.App/Assets/Audio/
  Adhan/adhan-1.oadh … adhan-7.oadh   what ships in the app and the MSI
  sounds.json                         id, display name, file, aliases, Fajr flag
```

Settings store sound IDs only. `AdhanSettings.DefaultSoundId` is the Default Adhan; each prayer's
`PrayerAlert.SoundId` is `null` to follow it, or an ID to override it. Old IDs (`makkah`, `madina`)
resolve through the `aliases` in `sounds.json`.

## The packed format (`.oadh`)

An 8-byte header (`OADH`, format version 1, 3 reserved bytes) followed by the audio, XOR-masked
with a fixed xorshift32 keystream (`OptimAdhan.Core.Audio.PackedAudio`). At playback the file is read,
unmasked in memory and handed to the Windows media player as a stream, so no decoded copy is ever written
to disk. Reading and decoding the largest file (3.4 MB) takes about 6–10 ms.

**What it does and does not do.** Explorer, media players and tag editors cannot open the files, and
they carry no titles, artists or cover art. That stops casual copying. It is **not** encryption or DRM:
the key is in the app, so a determined user can always recover the audio, for example by reading the
code or recording the speaker output. Real encryption would add key handling and complexity without
changing that, so it was not used.

## Adding or replacing a recording

1. Put the source file in `assets/audio/adhan-source/` and add it to `library.json`.
2. From the workspace root: `dotnet run tools/pack-adhan-audio.cs`
3. Add (or update) the entry in `sounds.json`: `{ "id": "adhan-8", "name": "Adhan 8", "file": "Adhan/adhan-8.oadh" }`.
   Never reuse or renumber an existing ID: saved settings refer to it.

A plain `.mp3`/`.wav` listed in `sounds.json` also plays, with no packing and no rebuild, for example to
try out a licensed recording.

## Before public distribution

The bundled recordings came from public Adhan sites; their licences have not been confirmed. Get written
permission or licensed replacements before a public release. The source files are git-ignored for
that reason.
