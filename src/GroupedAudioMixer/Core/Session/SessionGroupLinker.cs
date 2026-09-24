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
/// A removed session is dropped from <see cref="VolumeStateManager"/>'s live tracking only - its
/// entry in <see cref="ExecutableGroupMap"/> is untouched, so relaunching the executable re-links
/// it to the same group.
/// </summary>
public sealed class SessionGroupLinker : IDisposable
{
    private readonly IAudioSessionProvider _sessionProvider;
    private readonly ExecutableGroupMap _executableGroupMap;
    private readonly VolumeStateManager _volumeStateManager;

    public SessionGroupLinker(
        IAudioSessionProvider sessionProvider,
        ExecutableGroupMap executableGroupMap,
        VolumeStateManager volumeStateManager)
    {
        _sessionProvider = sessionProvider;
        _executableGroupMap = executableGroupMap;
        _volumeStateManager = volumeStateManager;

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
        _executableGroupMap.SetMapping(executablePath, groupId);

        foreach (var session in _sessionProvider.GetActiveSessions())
        {
            if (string.Equals(session.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase))
                _volumeStateManager.AssignSessionToGroup(session.SessionKey, groupId);
        }
    }

    private void OnSessionCreated(object? sender, AudioSessionEventArgs e) => Link(e.Session);

    private void OnSessionRemoved(object? sender, AudioSessionEventArgs e) =>
        _volumeStateManager.RemoveSession(e.Session.SessionKey);

    private void Link(AudioSessionInfo session)
    {
        var groupId = _executableGroupMap.TryGetGroupId(session.ExecutablePath) ?? _volumeStateManager.DefaultGroupId;
        _volumeStateManager.AssignSessionToGroup(session.SessionKey, groupId);
    }

    public void Dispose()
    {
        _sessionProvider.SessionCreated -= OnSessionCreated;
        _sessionProvider.SessionRemoved -= OnSessionRemoved;
    }
}
