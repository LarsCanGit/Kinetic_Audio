using GroupedAudioMixer.Core.Audio;
using GroupedAudioMixer.Core.Mapping;
using GroupedAudioMixer.Core.Volume;

namespace GroupedAudioMixer.Core.Session;

/// <summary>
/// Wires event-driven session detection to group assignment: a newly created audio session is
/// looked up in the executable-to-group map and linked immediately, falling back to the default
/// group when unmapped (auto-link, per the Product Design Doc's persistence section). Sessions
/// already active when this is constructed are linked too, so a restart of the app doesn't lose
/// grouping for processes that were already running.
///
/// A removed session is dropped from <see cref="VolumeStateManager"/>'s live tracking only - its
/// entry in <see cref="ExecutableGroupMap"/> is untouched, so relaunching the executable re-links
/// it to the same group.
/// </summary>
public sealed class SessionAutoLinker : IDisposable
{
    private readonly IAudioSessionProvider _sessionProvider;
    private readonly ExecutableGroupMap _executableGroupMap;
    private readonly VolumeStateManager _volumeStateManager;

    public SessionAutoLinker(
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
