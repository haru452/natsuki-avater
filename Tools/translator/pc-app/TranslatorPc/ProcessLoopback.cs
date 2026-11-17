// Captures the sound of one process (and its children) only — Windows 10 2004+ "process loopback".
// NAudio 2.2.1 has no public API for this, so this talks to Windows directly (ActivateAudioInterfaceAsync).
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace TranslatorPc;

sealed class ProcessLoopbackCapture : IWaveIn
{
    public WaveFormat WaveFormat { get; set; } = new(48000, 16, 2);
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    readonly IAudioClient client;
    IAudioCaptureClient? capture;
    readonly AutoResetEvent ready = new(false);
    Thread? thread;
    volatile bool stopping;

    ProcessLoopbackCapture(IAudioClient client) => this.client = client;

    public static ProcessLoopbackCapture Create(int processId)
    {
        var p = new AudioClientActivationParams { ActivationType = 1 /* PROCESS_LOOPBACK */, TargetProcessId = (uint)processId, ProcessLoopbackMode = 0 /* include tree */ };
        int size = Marshal.SizeOf<AudioClientActivationParams>();
        IntPtr blob = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(p, blob, false);
            var variant = new PropVariantBlob { vt = 65 /* VT_BLOB */, cbSize = (uint)size, pBlobData = blob };
            var handler = new Completion();
            var iid = typeof(IAudioClient).GUID;
            Native.ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, ref variant, handler, out var op);
            if (!handler.Done.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Windows did not answer");
            op.GetActivateResult(out int hr, out var unknown);
            Marshal.ThrowExceptionForHR(hr);
            var c = new ProcessLoopbackCapture((IAudioClient)unknown);
            c.Initialize();
            return c;
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }
    }

    void Initialize()
    {
        var wf = new WaveFormatEx { wFormatTag = 1, nChannels = 2, nSamplesPerSec = 48000, nAvgBytesPerSec = 48000 * 4, nBlockAlign = 4, wBitsPerSample = 16 };
        var session = Guid.Empty;
        const uint flags = 0x00020000 /* LOOPBACK */ | 0x00040000 /* EVENTCALLBACK */ | 0x80000000 /* AUTOCONVERTPCM */ | 0x08000000 /* SRC_DEFAULT_QUALITY */;
        client.Initialize(0 /* shared */, flags, 200_000 /* 20 ms */, 0, ref wf, ref session);
        client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle());
        var iid = typeof(IAudioCaptureClient).GUID;
        client.GetService(ref iid, out var service);
        capture = (IAudioCaptureClient)service;
    }

    public void StartRecording()
    {
        stopping = false;
        client.Start();
        thread = new Thread(Loop) { IsBackground = true, Name = "process-loopback" };
        thread.Start();
    }

    void Loop()
    {
        Exception? error = null;
        var buffer = new byte[48000 * 4];
        try
        {
            while (!stopping)
            {
                ready.WaitOne(100);
                capture!.GetNextPacketSize(out int frames);
                while (frames > 0 && !stopping)
                {
                    capture.GetBuffer(out IntPtr data, out int n, out int bufferFlags, out _, out _);
                    int bytes = n * 4;
                    if (bytes > buffer.Length) buffer = new byte[bytes];
                    if ((bufferFlags & 2 /* SILENT */) != 0) Array.Clear(buffer, 0, bytes);
                    else Marshal.Copy(data, buffer, 0, bytes);
                    capture.ReleaseBuffer(n);
                    DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, bytes));
                    capture.GetNextPacketSize(out frames);
                }
            }
        }
        catch (Exception e)
        {
            error = e;  // e.g. VRChat closed
        }
        RecordingStopped?.Invoke(this, new StoppedEventArgs(error));
    }

    public void StopRecording()
    {
        stopping = true;
        thread?.Join(1000);
        try { client.Stop(); } catch (COMException) { }
    }

    public void Dispose()
    {
        StopRecording();
        if (capture != null) Marshal.ReleaseComObject(capture);
        Marshal.ReleaseComObject(client);
        ready.Dispose();
    }

    // ---- Windows interop ----

    [StructLayout(LayoutKind.Sequential)]
    struct AudioClientActivationParams { public int ActivationType; public uint TargetProcessId; public int ProcessLoopbackMode; }

    [StructLayout(LayoutKind.Explicit)]
    struct PropVariantBlob
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public uint cbSize;
        [FieldOffset(16)] public IntPtr pBlobData;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    struct WaveFormatEx
    {
        public ushort wFormatTag, nChannels;
        public uint nSamplesPerSec, nAvgBytesPerSec;
        public ushort nBlockAlign, wBitsPerSample, cbSize;
    }

    static class Native
    {
        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern void ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
            ref Guid riid, ref PropVariantBlob activationParams, IActivateAudioInterfaceCompletionHandler completionHandler,
            out IActivateAudioInterfaceAsyncOperation activationOperation);
    }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivateAudioInterfaceCompletionHandler
    {
        void ActivateCompleted(IActivateAudioInterfaceAsyncOperation op);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivateAudioInterfaceAsyncOperation
    {
        void GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    // Marks the callback as callable from any thread (Windows requires it).
    [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAgileObject { }

    sealed class Completion : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public readonly ManualResetEventSlim Done = new(false);
        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation op) => Done.Set();
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        void Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, ref WaveFormatEx format, ref Guid session);
        void GetBufferSize(out uint frames);
        void GetStreamLatency(out long latency);
        void GetCurrentPadding(out int frames);
        void IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        void GetMixFormat(out IntPtr format);
        void GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(IntPtr handle);
        void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        void GetBuffer(out IntPtr data, out int frames, out int flags, out long devicePosition, out long qpcPosition);
        void ReleaseBuffer(int frames);
        void GetNextPacketSize(out int frames);
    }
}
