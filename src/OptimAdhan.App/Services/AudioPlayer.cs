using System.Windows.Media;

namespace OptimAdhan.App.Services;

/// <summary>
/// Plays one sound at a time with WPF's MediaPlayer (Windows Media Foundation).
/// Must be used from the UI thread.
/// </summary>
public sealed class AudioPlayer
{
    private readonly MediaPlayer _player = new();

    public AudioPlayer()
    {
        _player.MediaEnded += (_, _) => Finish(PlaybackEnd.Completed);
        _player.MediaFailed += (_, e) =>
        {
            Log.Error("Audio playback failed", e.ErrorException);
            _player.Close();
            Finish(PlaybackEnd.Failed);
        };
    }

    public enum PlaybackEnd
    {
        Completed,
        Stopped,
        Failed,
    }

    public event EventHandler? PlaybackStopped;

    public bool IsPlaying { get; private set; }

    /// <summary>Increases with every <see cref="Play"/>, so listeners can tell one playback from the next.</summary>
    public int Session { get; private set; }

    /// <summary>How the most recent playback ended.</summary>
    public PlaybackEnd LastEnd { get; private set; }

    public TimeSpan Position => IsPlaying ? _player.Position : TimeSpan.Zero;

    /// <summary>Null until the file has opened and its length is known.</summary>
    public TimeSpan? Duration => IsPlaying && _player.NaturalDuration.HasTimeSpan ? _player.NaturalDuration.TimeSpan : null;

    /// <param name="volume">0–100.</param>
    public void Play(string path, int volume)
    {
        Stop();
        Session++;
        _player.Open(new Uri(path, UriKind.Absolute));
        _player.Volume = Math.Clamp(volume, 0, 100) / 100.0;
        _player.Play();
        IsPlaying = true;
    }

    public void Stop()
    {
        if (!IsPlaying)
        {
            return;
        }

        _player.Stop();
        _player.Close();
        Finish(PlaybackEnd.Stopped);
    }

    private void Finish(PlaybackEnd end)
    {
        if (!IsPlaying)
        {
            return;
        }

        IsPlaying = false;
        LastEnd = end;
        PlaybackStopped?.Invoke(this, EventArgs.Empty);
    }
}
