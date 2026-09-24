namespace KineticAudio.Core.Audio;

public sealed class AudioSessionEventArgs(AudioSessionInfo session) : EventArgs
{
    public AudioSessionInfo Session { get; } = session;
}
