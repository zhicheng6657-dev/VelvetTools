using System.Runtime.InteropServices;
using VelvetTools.Common;

namespace VelvetTools.Modules.Audio;

/// <summary>系统主音量控制（CoreAudio IAudioEndpointVolume，自研 COM 互操作）。</summary>
public sealed class AudioService
{
    // ---------- COM ----------
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    private enum EDataFlow { Render = 0, Capture = 1, All = 2 }
    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
        int GetDevice(string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(IntPtr client);
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        int OpenPropertyStore(uint access, out IntPtr properties);
        int GetId(out IntPtr id);
        int GetState(out uint state);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out uint count);
        int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        int GetMasterVolumeLevel(out float levelDb);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        int GetChannelVolumeLevel(uint channel, out float levelDb);
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        int GetVolumeStepInfo(out uint step, out uint stepCount);
        int VolumeStepUp(ref Guid eventContext);
        int VolumeStepDown(ref Guid eventContext);
        int QueryHardwareSupport(out uint mask);
        int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }

    private Guid _ctx = Guid.NewGuid();

    private IAudioEndpointVolume? GetEndpoint()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device) != 0)
                return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, 1 /*CLSCTX_INPROC_SERVER*/, IntPtr.Zero, out object obj) != 0)
                return null;
            return (IAudioEndpointVolume)obj;
        }
        catch (Exception ex)
        {
            Logger.Error("获取音频端点失败", ex);
            return null;
        }
    }

    public bool IsAvailable => GetEndpoint() is not null;

    /// <summary>0-100，失败返回 -1。</summary>
    public int GetVolume()
    {
        var ep = GetEndpoint();
        if (ep is null) return -1;
        return ep.GetMasterVolumeLevelScalar(out float level) == 0 ? (int)Math.Round(level * 100) : -1;
    }

    public void SetVolume(int percent)
    {
        var ep = GetEndpoint();
        ep?.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref _ctx);
    }

    public bool GetMute()
    {
        var ep = GetEndpoint();
        return ep is not null && ep.GetMute(out bool mute) == 0 && mute;
    }

    public void SetMute(bool mute)
    {
        var ep = GetEndpoint();
        ep?.SetMute(mute, ref _ctx);
    }
}
