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
        _player.MediaEnded += (_, _) => Finish();
        _player.MediaFailed += (_, e) =>
        {
            Log.Error("Audio playback failed", e.ErrorException);
            Finish();
        };
    }

    public event EventHandler? PlaybackStopped;

    public bool IsPlaying { get; private set; }

    /// <param name="volume">0–100.</param>
    public void Play(string path, int volume)
    {
        Stop();
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
        Finish();
    }

    private void Finish()
    {
        if (!IsPlaying)
        {
            return;
        }

        IsPlaying = false;
        PlaybackStopped?.Invoke(this, EventArgs.Empty);
    }
}
