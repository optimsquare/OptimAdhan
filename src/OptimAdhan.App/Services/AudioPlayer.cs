using System.IO;
using System.Windows.Threading;
using OptimAdhan.Core.Audio;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace OptimAdhan.App.Services;

/// <summary>
/// Plays one sound at a time with the Windows media player (Media Foundation).
/// Packed recordings (<see cref="PackedAudio"/>) are decoded in memory and never
/// written to disk; plain audio files play straight from their path.
/// Must be used from the UI thread; player events are marshalled back to it.
/// </summary>
public sealed class AudioPlayer
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private MediaPlayer? _player; // created on first use, then reused
    private MediaSource? _source;
    private volatile int _session;

    public enum PlaybackEnd
    {
        Completed,
        Stopped,
        Failed,
    }

    public event EventHandler? PlaybackStopped;

    public bool IsPlaying { get; private set; }

    /// <summary>Increases with every <see cref="Play"/>, so listeners can tell one playback from the next.</summary>
    public int Session => _session;

    /// <summary>How the most recent playback ended.</summary>
    public PlaybackEnd LastEnd { get; private set; }

    public TimeSpan Position => IsPlaying && _player is { } p ? p.PlaybackSession.Position : TimeSpan.Zero;

    /// <summary>Null until the file has opened and its length is known.</summary>
    public TimeSpan? Duration =>
        IsPlaying && _player?.PlaybackSession.NaturalDuration is { } d && d > TimeSpan.Zero ? d : null;

    /// <param name="volume">0–100.</param>
    public void Play(string path, int volume)
    {
        Stop();
        _session++;
        IsPlaying = true;
        try
        {
            _source = Open(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Error($"Audio file could not be opened: {System.IO.Path.GetFileName(path)}", ex);
            Finish(PlaybackEnd.Failed);
            return;
        }

        Log.Info($"Playing {System.IO.Path.GetFileName(path)} at volume {volume}");
        var player = _player ??= CreatePlayer();
        player.Volume = Math.Clamp(volume, 0, 100) / 100.0;
        player.Source = _source;
        player.Play();
    }

    public void Stop()
    {
        if (!IsPlaying)
        {
            return;
        }

        Release();
        Finish(PlaybackEnd.Stopped);
    }

    private static MediaSource Open(string path)
    {
        if (!string.Equals(System.IO.Path.GetExtension(path), PackedAudio.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return MediaSource.CreateFromUri(new Uri(path, UriKind.Absolute));
        }

        var audio = PackedAudio.Unpack(File.ReadAllBytes(path));
        var contentType = audio.AsSpan().StartsWith("RIFF"u8) ? "audio/wav" : "audio/mpeg";
        return MediaSource.CreateFromStream(new MemoryStream(audio, writable: false).AsRandomAccessStream(), contentType);
    }

    private MediaPlayer CreatePlayer()
    {
        var player = new MediaPlayer { AutoPlay = false };
        // An alert, not media: keep it out of the Windows media flyout and away from media keys.
        player.CommandManager.IsEnabled = false;
        player.MediaEnded += (_, _) => OnPlayerEvent(_session, PlaybackEnd.Completed, null);
        player.MediaFailed += (_, e) => OnPlayerEvent(_session, PlaybackEnd.Failed, e);
        return player;
    }

    /// <summary>Runs on a media thread; the session number tells a finished playback from a newer one.</summary>
    private void OnPlayerEvent(int session, PlaybackEnd end, MediaPlayerFailedEventArgs? failure)
    {
        var message = failure is null ? null : $"{failure.Error}: {failure.ErrorMessage} (0x{failure.ExtendedErrorCode?.HResult:X8})";
        _dispatcher.BeginInvoke(() =>
        {
            if (session != _session || !IsPlaying)
            {
                return;
            }

            if (message is not null)
            {
                Log.Error($"Audio playback failed: {message}");
            }

            Release();
            Finish(end);
        });
    }

    private void Release()
    {
        if (_player is { } player)
        {
            player.Pause();
            player.Source = null;
        }

        _source?.Dispose();
        _source = null;
    }

    private void Finish(PlaybackEnd end)
    {
        if (!IsPlaying)
        {
            return;
        }

        IsPlaying = false;
        LastEnd = end;
        Log.Info($"Playback {end.ToString().ToLowerInvariant()}");
        PlaybackStopped?.Invoke(this, EventArgs.Empty);
    }
}
