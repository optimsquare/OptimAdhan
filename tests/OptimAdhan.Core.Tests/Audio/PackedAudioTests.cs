using OptimAdhan.Core.Audio;

namespace OptimAdhan.Core.Tests.Audio;

public sealed class PackedAudioTests
{
    private static readonly byte[] FakeFrames = [0xFF, 0xF3, 0x28, 0xC0, 0x00, 0x0A, 0x50, 0x05, 0x11, 0x22, 0x33];

    [Fact]
    public void RoundTripsAndHidesTheAudio()
    {
        var audio = Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray();

        var packed = PackedAudio.Pack(audio);

        Assert.True(PackedAudio.IsPacked(packed));
        Assert.Equal(audio.Length + 8, packed.Length);
        Assert.NotEqual(audio, packed[8..]); // not stored in the clear
        Assert.Equal(audio, PackedAudio.Unpack(packed));
    }

    [Fact]
    public void RejectsOtherFiles()
    {
        Assert.False(PackedAudio.IsPacked(FakeFrames));
        Assert.Throws<InvalidDataException>(() => PackedAudio.Unpack(FakeFrames));
    }

    [Fact]
    public void RejectsUnknownFormatVersions()
    {
        var packed = PackedAudio.Pack(FakeFrames);
        packed[4] = 9;

        Assert.Throws<InvalidDataException>(() => PackedAudio.Unpack(packed));
    }

    [Fact]
    public void StripsLeadingAndTrailingId3Tags()
    {
        // ID3v2.3 header with a 20-byte body (syncsafe size 0x14), then frames, then a 128-byte ID3v1 tag.
        byte[] id3v2 = [(byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 0, 0x14, .. new byte[20]];
        byte[] id3v1 = [(byte)'T', (byte)'A', (byte)'G', .. new byte[125]];

        Assert.Equal(FakeFrames, PackedAudio.StripId3Tags([.. id3v2, .. FakeFrames, .. id3v1]));
        Assert.Equal(FakeFrames, PackedAudio.StripId3Tags(FakeFrames));
    }
}
