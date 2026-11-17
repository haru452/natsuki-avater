// The two sounds the PC sends to the phone: your microphone, and VRChat's own sound.
// Each is opened only while the phone wants it (AudioGate), so the mic is not held open for nothing.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using TranslatorPc.Core;

namespace TranslatorPc;

/// <summary>Where captured audio goes.</summary>
interface IAudioSink
{
    void Push(WaveFormat wf, byte[] buffer, int count);
}

/// <summary>Captured audio as 16 kHz 30 ms frames for the translator (the Segmenter's frame size).</summary>
sealed class FrameSink : IAudioSink
{
    readonly Action<short[]> onFrame;
    readonly Framer framer = new(Segmenter.FrameSamples);
    AudioConverter? converter;
    WaveFormat? format;

    public FrameSink(Action<short[]> onFrame) => this.onFrame = onFrame;

    public void Push(WaveFormat wf, byte[] buffer, int count)
    {
        if (format == null || !format.Equals(wf))
        {
            format = wf;
            converter = AudioSender.ConverterFor(wf);
        }
        foreach (var frame in framer.Push(converter!.Convert(buffer, count))) onFrame(frame);
    }
}

/// <summary>Turns captured buffers into 20 ms packets and sends them where the gate says (phone version).</summary>
class AudioSender : IAudioSink
{
    readonly StreamKind kind;
    readonly Func<StreamKind, IPEndPoint?> destination;
    readonly UdpClient socket;
    AudioConverter? converter;
    WaveFormat? format;
    readonly Framer framer = new();
    uint seq;
    int sentThisSecond;

    public AudioSender(StreamKind kind, Func<StreamKind, IPEndPoint?> destination, UdpClient socket)
    {
        this.kind = kind;
        this.destination = destination;
        this.socket = socket;
    }

    public static AudioConverter ConverterFor(WaveFormat wf) => new(wf.SampleRate, wf.Channels,
        wf.Encoding == WaveFormatEncoding.IeeeFloat
        || (wf is WaveFormatExtensible ext && ext.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT),
        wf.BitsPerSample);

    /// <summary>Packets sent since the last call (the screen asks once a second).</summary>
    public int TakeCount() => Interlocked.Exchange(ref sentThisSecond, 0);

    public void Push(WaveFormat wf, byte[] buffer, int count)
    {
        if (format == null || !format.Equals(wf))
        {
            format = wf;
            converter = ConverterFor(wf);
        }
        foreach (var frame in framer.Push(converter!.Convert(buffer, count)))
        {
            var to = destination(kind);
            if (to == null) continue;
            var packet = AudioPacket.Encode(kind, seq++, frame);
            try
            {
                socket.Send(packet, packet.Length, to);
                Interlocked.Increment(ref sentThisSecond);
            }
            catch (SocketException) { /* Wi-Fi hiccup: drop it */ }
        }
    }
}

/// <summary>One capture that is opened when wanted and closed when not. Call Update about once a second.</summary>
abstract class CaptureSource : IDisposable
{
    protected readonly IAudioSink sender;
    IWaveIn? capture;
    public string State { get; protected set; } = "止まっています";

    protected CaptureSource(IAudioSink sender) => this.sender = sender;

    protected void Start(IWaveIn c, string state)
    {
        Stop();
        capture = c;
        c.DataAvailable += (_, e) => sender.Push(c.WaveFormat, e.Buffer, e.BytesRecorded);
        c.RecordingStopped += (_, e) => { if (e.Exception != null) State = "止まりました: " + e.Exception.Message; };
        c.StartRecording();
        State = state;
    }

    protected bool Running => capture != null;

    protected void Stop()
    {
        if (capture == null) return;
        try { capture.StopRecording(); } catch { /* already stopped */ }
        capture.Dispose();
        capture = null;
        State = "止まっています";
    }

    public abstract void Update(bool wanted);

    public void Dispose() => Stop();
}

/// <summary>Your Windows default microphone.</summary>
class MicSource : CaptureSource
{
    public MicSource(IAudioSink sender) : base(sender) { }

    public override void Update(bool wanted)
    {
        if (wanted == Running) return;
        if (!wanted) { Stop(); return; }
        try
        {
            using var devices = new MMDeviceEnumerator();
            var mic = ChooseMic(devices);
            Start(new WasapiCapture(mic), "送っています（" + mic.FriendlyName + "）");
        }
        catch (Exception e)
        {
            State = "マイクを開けません: " + e.Message;
        }
    }

/// <summary>
/// The mic VRChat records from: the device named in VRChat's settings, else Windows' default recording device
/// (the Console role, which is what VRChat's "Default" uses — not the Communications default, which on laptops
/// is often the built-in mic array).
/// </summary>
static MMDevice ChooseMic(MMDeviceEnumerator devices)
{
    var all = devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
    int? i = MicChoice.Pick(all.Select(d => d.FriendlyName).ToList(), VrchatMicName());
    return i is int n ? all[n] : devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
}

/// <summary>VRChat keeps its chosen mic (desktop or VR) in the registry; empty means "Default".</summary>
static string? VrchatMicName()
{
    try
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\VRChat\VRChat");
        if (key == null) return null;
        foreach (var name in key.GetValueNames().Where(n => n.StartsWith("VRC_INPUT_MIC_DEVICE_NAME_")))
        {
            string value = key.GetValue(name) switch
            {
                byte[] b => System.Text.Encoding.UTF8.GetString(b).TrimEnd((char)0),
                string str => str,
                _ => "",
            };
            if (value.Trim().Length > 0) return value;
        }
    }
    catch (Exception) { /* no access: use the default */ }
    return null;
}
}

/// <summary>Only VRChat's sound (not YouTube, Discord, …): Windows' per-process loopback.</summary>
class VrchatSource : CaptureSource
{
    readonly ProcessWatcher watcher = new(() =>
    {
        var p = Process.GetProcessesByName("VRChat");
        try { return p.Length > 0 ? p[0].Id : null; }
        finally { foreach (var x in p) x.Dispose(); }
    });
    int? pid;

    public VrchatSource(IAudioSink sender) : base(sender) { }

    public override void Update(bool wanted)
    {
        pid = watcher.Poll();
        if (watcher.Changed || !wanted || pid == null) Stop();
        if (!wanted) return;
        if (pid == null) { State = "VRChat が起動していません"; return; }
        if (Running) return;
        try
        {
            Start(ProcessLoopbackCapture.Create(pid.Value), "送っています（VRChat の音だけ）");
        }
        catch (Exception e)
        {
            State = "VRChat の音を取れません: " + e.Message;
        }
    }
}
