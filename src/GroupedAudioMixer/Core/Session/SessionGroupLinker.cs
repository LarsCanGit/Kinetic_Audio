using System.Linq;
using KineticAudio.Core.Audio;
using KineticAudio.Core.Mapping;
using KineticAudio.Core.Volume;

namespace KineticAudio.Core.Session;

/// <summary>
/// Wires session-to-group assignment for both entry points D9 requires to funnel through the
/// same <see cref="ExecutableGroupMap"/> single-writer path: auto-link on session creation, and
/// manual drag-to-group from the UI.
///
/// Auto-link: a newly created audio session is looked up in the executable-to-group map and
/// linked immediately, falling back to the default group when unmapped. Sessions already active
/// when this is constructed are linked too, so a restart of the app doesn't lose grouping for
/// processes that were already running.
///
/// Manual drag-to-group: <see cref="AssignExecutableToGroup"/> persists the new mapping via
/// <see cref="ExecutableGroupMap.SetMapping"/> and immediately reassigns any live session(s) for
/// that executable, rather than only affecting future session creations.
///
/// Capture (recording) sessions always link to the capture group (the Mic strip), whatever the
/// executable is mapped to: the map records where an app's playback goes. Without a capture group
/// they aren't linked at all, so a recording stream never lands in a playback group.
///
/// A removed session is dropped from <see cref="VolumeStateManager"/>'s live tracking only - its
/// entry in <see cref="ExecutableGroupMap"/> is untouched, so relaunching the executable re-links
/// it to the same group.
/// </summary>
public sealed class SessionGroupLinker : IDisposable
{
    private readonly IAudioSessionProvider _sessionProvider;
    private readonly ExecutableGroupMap _executableGroupMap;
    private readonly VolumeStateManager _volumeStateManager;
    private readonly string? _captureGroupId;

    /// <summary>
    /// Raised when an executable's stored group no longer exists and it was moved to the default
    /// group. Raised on the session-notification thread; UI handlers must marshal.
    /// </summary>
    public event EventHandler<MappingFallbackEventArgs>? MappingFellBack;

    public SessionGroupLinker(
        IAudioSessionProvider sessionProvider,
        ExecutableGroupMap executableGroupMap,
        VolumeStateManager volumeStateManager,
        EventHandler<MappingFallbackEventArgs>? onMappingFellBack = null,
        string? captureGroupId = null)
    {
        _sessionProvider = sessionProvider;
        _executableGroupMap = executableGroupMap;
        _volumeStateManager = volumeStateManager;
        _captureGroupId = captureGroupId is not null && volumeStateManager.HasGroup(captureGroupId)
            ? captureGroupId
            : null;

        // Subscribed before the initial link pass so fallbacks for already-running sessions
        // aren't lost at startup, which is exactly when a stale config shows up.
        if (onMappingFellBack is not null)
            MappingFellBack += onMappingFellBack;

        _sessionProvider.SessionCreated += OnSessionCreated;
        _sessionProvider.SessionRemoved += OnSessionRemoved;

        foreach (var session in _sessionProvider.GetActiveSessions())
            Link(session);
    }

    /// <summary>
    /// Manual drag-to-group: persists <paramref name="executablePath"/>'s mapping to
    /// <paramref name="groupId"/> through the same serialized path auto-link uses, then
    /// immediately reassigns every currently-live session for that executable so the UI reflects
    /// the move without waiting for the process to relaunch.
    /// </summary>
    public void AssignExecutableToGroup(string executablePath, string groupId)
    {
        if (!_volumeStateManager.HasGroup(groupId))
            throw new ArgumentException($"Unknown group '{groupId}'.", nameof(groupId));
        if (IsCaptureGroup(groupId))
            throw new ArgumentException($"Group '{groupId}' only holds recording sessions.", nameof(groupId));

        _executableGroupMap.SetMapping(executablePath, groupId);

        foreach (var session in _sessionProvider.GetActiveSessions())
        {
            if (session.Flow == AudioSessionFlow.Render
                && string.Equals(session.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase))
                _volumeStateManager.AssignSessionToGroup(session.SessionKey, groupId);
        }
    }

    private void OnSessionCreated(object? sender, AudioSessionEventArgs e) => Link(e.Session);

    private void OnSessionRemoved(object? sender, AudioSessionEventArgs e) =>
        _volumeStateManager.RemoveSession(e.Session.SessionKey);

    private void Link(AudioSessionInfo session)
    {
        if (session.Flow == AudioSessionFlow.Capture)
        {
            if (_captureGroupId is not null)
                _volumeStateManager.AssignSessionToGroup(session.SessionKey, _captureGroupId);
            return;
        }

        var mappedGroupId = _executableGroupMap.TryGetGroupId(session.ExecutablePath);
        var groupId = mappedGroupId ?? _volumeStateManager.DefaultGroupId;

        if (mappedGroupId is not null && (!_volumeStateManager.HasGroup(mappedGroupId) || IsCaptureGroup(mappedGroupId)))
        {
            // Stale mapping (the group id no longer exists, or is the capture group, which never
            // holds playback; e.g. a hand-edited or older config).
            // Rewrite it through the single writer so this only happens once, and tell the UI
            // rather than falling back silently.
            groupId = _volumeStateManager.DefaultGroupId;
            _executableGroupMap.SetMapping(session.ExecutablePath, groupId);
            MappingFellBack?.Invoke(this, new MappingFallbackEventArgs(session.ExecutablePath, mappedGroupId, groupId));
        }

        _volumeStateManager.AssignSessionToGroup(session.SessionKey, groupId);
    }

    private bool IsCaptureGroup(string groupId) =>
        _captureGroupId is not null && string.Equals(groupId, _captureGroupId, StringComparison.Ordinal);

    public void Dispose()
    {
        _sessionProvider.SessionCreated -= OnSessionCreated;
        _sessionProvider.SessionRemoved -= OnSessionRemoved;
    }
}

public sealed class MappingFallbackEventArgs(string executablePath, string missingGroupId, string fallbackGroupId) : EventArgs
{
    public string ExecutablePath { get; } = executablePath;
    public string MissingGroupId { get; } = missingGroupId;
    public string FallbackGroupId { get; } = fallbackGroupId;
}
