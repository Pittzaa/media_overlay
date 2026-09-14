using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Diagnostics;

namespace TopMediaBar;

/// <summary>Controls the active audio session of the app owning the current track.</summary>
public class VolumeService
{
    public bool TryGetState(string sourceApp, out float volume, out bool muted)
    {
        volume = 0; muted = false;
        try
        {
            using var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            var session = FindTargetSession(sessions, sourceApp);
            if (session == null) return false;
            volume = session.SimpleAudioVolume.Volume;
            muted = session.SimpleAudioVolume.Mute;
            return true;
        }
        catch { return false; }
    }

    public bool SetVolume(string sourceApp, float value)
    {
        if (!float.IsFinite(value)) return false;
        try
        {
            using var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            var session = FindTargetSession(sessions, sourceApp);
            if (session == null) return false;
            session.SimpleAudioVolume.Volume = Math.Clamp(value, 0f, 1f);
            if (value > 0) session.SimpleAudioVolume.Mute = false;
            return true;
        }
        catch { return false; }
    }

    public bool ToggleMute(string sourceApp)
    {
        try
        {
            using var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            var session = FindTargetSession(sessions, sourceApp);
            if (session == null) return false;
            session.SimpleAudioVolume.Mute = !session.SimpleAudioVolume.Mute;
            return true;
        }
        catch { return false; }
    }

    private static AudioSessionControl? FindTargetSession(SessionCollection sessions, string sourceApp)
    {
        var processName = sourceApp.Contains("edge", StringComparison.OrdinalIgnoreCase) ? "msedge" :
            sourceApp.Contains("firefox", StringComparison.OrdinalIgnoreCase) ? "firefox" :
            sourceApp.Contains("spotify", StringComparison.OrdinalIgnoreCase) ? "spotify" : "chrome";
        for (var i = 0; i < sessions.Count; i++)
        {
            try
            {
                var session = sessions[i];
                if (session.GetProcessID == 0 || session.State != AudioSessionState.AudioSessionStateActive) continue;
                using var process = Process.GetProcessById((int)session.GetProcessID);
                if (string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase)) return session;
            }
            catch { }
        }
        return null;
    }
}
