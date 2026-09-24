using KineticAudio.Core.Audio;

namespace KineticAudio.Core.Volume;

/// <summary>
/// Poll-driven wiring for external volume drift reconciliation (binding decision: external
/// volume drift gets reconciled, not ignored). Session creation/removal is event-driven per D8,
/// but a session's volume can change from outside the app (Windows volume mixer, the app itself
/// exposing its own slider) with no notification, so this polls each live session's actual
/// volume on an interval and hands it to <see cref="VolumeStateManager.ReconcileExternalVolume"/>,
/// which treats a mismatch as a manual override rather than silently overwriting it on the next
/// fader move.
///
/// <see cref="PollOnce"/> is the pure, synchronously-testable unit - it has no wall-clock
/// dependency and tests drive it directly. <see cref="Start"/>/<see cref="Stop"/> wire it to a
/// real timer for production use, at the target interval (250ms-1s) from CLAUDE.md.
/// </summary>
public sealed class VolumeDriftReconciler : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(500);

    private readonly IAudioSessionProvider _sessionProvider;
    private readonly VolumeStateManager _volumeStateManager;
    private readonly TimeSpan _interval;
    private Timer? _timer;

    public VolumeDriftReconciler(
        IAudioSessionProvider sessionProvider,
        VolumeStateManager volumeStateManager,
        TimeSpan? interval = null)
    {
        _sessionProvider = sessionProvider;
        _volumeStateManager = volumeStateManager;
        _interval = interval ?? DefaultInterval;
    }

    /// <summary>Starts periodic polling on a background timer. No-op if already started.</summary>
    public void Start() => _timer ??= new Timer(_ => PollOnce(), null, _interval, _interval);

    /// <summary>Stops polling. Safe to call even if never started.</summary>
    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>
    /// Runs a single reconciliation pass over every currently live session, reading its actual
    /// Core Audio volume and reconciling it against the computed value.
    /// </summary>
    public void PollOnce()
    {
        foreach (var session in _sessionProvider.GetActiveSessions())
        {
            // The session can expire between GetActiveSessions and the read; skip it.
            if (_sessionProvider.TryGetSessionVolume(session.SessionKey, out var actualVolume))
                _volumeStateManager.ReconcileExternalVolume(session.SessionKey, actualVolume);
        }
    }

    public void Dispose() => Stop();
}
