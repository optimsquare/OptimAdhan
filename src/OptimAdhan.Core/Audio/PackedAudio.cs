using System.Buffers.Binary;

namespace OptimAdhan.Core.Audio;

/// <summary>
/// The app's own container for bundled recordings (<c>.oadh</c>): an 8-byte
/// header followed by the audio, masked with a fixed keystream. It keeps the
/// files from being opened or browsed casually in Explorer or a media player;
/// it is not encryption, and anyone determined can still recover the audio.
/// Decoding is a single pass over a few megabytes at most, so playback start is
/// unaffected.
/// </summary>
public static class PackedAudio
{
    public const string Extension = ".oadh";

    private const int HeaderLength = 8;
    private const byte FormatVersion = 1;
    private const uint Seed = 0x4F414448; // "OADH"
    private static readonly byte[] Magic = "OADH"u8.ToArray();

    public static bool IsPacked(ReadOnlySpan<byte> data) =>
        data.Length >= HeaderLength && data[..Magic.Length].SequenceEqual(Magic);

    /// <summary>Wraps audio bytes (for example an MP3 file) in the packed container.</summary>
    public static byte[] Pack(ReadOnlySpan<byte> audio)
    {
        var packed = new byte[HeaderLength + audio.Length];
        Magic.CopyTo(packed, 0);
        packed[Magic.Length] = FormatVersion;
        audio.CopyTo(packed.AsSpan(HeaderLength));
        Mask(packed.AsSpan(HeaderLength));
        return packed;
    }

    /// <summary>Returns the original audio bytes.</summary>
    /// <exception cref="InvalidDataException">The data is not a packed recording this version can read.</exception>
    public static byte[] Unpack(ReadOnlySpan<byte> packed)
    {
        if (!IsPacked(packed))
        {
            throw new InvalidDataException("Not an OptimAdhan audio file.");
        }

        if (packed[Magic.Length] != FormatVersion)
        {
            throw new InvalidDataException($"Unsupported OptimAdhan audio format version {packed[Magic.Length]}.");
        }

        var audio = packed[HeaderLength..].ToArray();
        Mask(audio);
        return audio;
    }

    /// <summary>Removes the tags of an MP3 or the metadata chunks of a WAV; see the two methods below.</summary>
    public static byte[] StripMetadata(ReadOnlySpan<byte> audio) =>
        IsWave(audio) ? StripWavMetadata(audio) : StripId3Tags(audio);

    /// <summary>
    /// Keeps only the chunks a WAV needs to play (<c>fmt </c>, <c>fact</c>, <c>data</c>), dropping
    /// LIST/INFO (title, artist, encoder), ID3 and other metadata chunks, and fixes the RIFF size.
    /// The audio samples are copied unchanged. Data that is not a WAV is returned unchanged.
    /// </summary>
    public static byte[] StripWavMetadata(ReadOnlySpan<byte> wav)
    {
        if (!IsWave(wav))
        {
            return wav.ToArray();
        }

        using var output = new MemoryStream(wav.Length);
        output.Write(wav[..12]); // "RIFF", size (fixed below), "WAVE"
        var position = 12;
        while (wav.Length - position >= 8)
        {
            var id = wav.Slice(position, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(wav.Slice(position + 4, 4));
            var next = position + 8L + size + (size & 1); // chunks are padded to an even length
            var keep = id.SequenceEqual("fmt "u8) || id.SequenceEqual("fact"u8) || id.SequenceEqual("data"u8);
            if (keep)
            {
                // A truncated last chunk is kept as far as it goes.
                output.Write(wav[position..(int)Math.Min(next, wav.Length)]);
            }

            if (next >= wav.Length)
            {
                break;
            }

            position = (int)next;
        }

        var result = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), (uint)(result.Length - 8));
        return result;
    }

    private static bool IsWave(ReadOnlySpan<byte> data) =>
        data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WAVE"u8);

    /// <summary>
    /// Removes ID3v2 (leading) and ID3v1 (trailing) tags from an MP3, so titles,
    /// artists and cover art of the source recording do not travel with the app.
    /// Data without tags is returned unchanged.
    /// </summary>
    public static byte[] StripId3Tags(ReadOnlySpan<byte> mp3)
    {
        var start = 0;
        // ID3v2 tags may be stacked; each has a 10-byte header with a 28-bit "syncsafe" size.
        while (mp3.Length - start >= 10 && mp3[start] == 'I' && mp3[start + 1] == 'D' && mp3[start + 2] == '3')
        {
            var size = (mp3[start + 6] & 0x7F) << 21 | (mp3[start + 7] & 0x7F) << 14 | (mp3[start + 8] & 0x7F) << 7 | (mp3[start + 9] & 0x7F);
            var footer = (mp3[start + 5] & 0x10) != 0 ? 10 : 0;
            var next = start + 10 + size + footer;
            if (next > mp3.Length)
            {
                break;
            }

            start = next;
        }

        var end = mp3.Length;
        if (end - start >= 128 && mp3[end - 128] == 'T' && mp3[end - 127] == 'A' && mp3[end - 126] == 'G')
        {
            end -= 128;
        }

        return mp3[start..end].ToArray();
    }

    /// <summary>XORs with an xorshift32 keystream; applying it twice restores the input.</summary>
    private static void Mask(Span<byte> data)
    {
        var state = Seed;
        for (var i = 0; i < data.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            data[i] ^= (byte)state;
        }
    }
}
