// Whatever the device gives (48 kHz stereo float, 44.1 kHz 16-bit...) to 16 kHz mono 16-bit for the phone.
namespace TranslatorPc.Core;

public class AudioConverter
{
    public const int TargetRate = 16000;

    readonly int channels;
    readonly bool isFloat;
    readonly int bytesPerSample;
    readonly double step;            // input samples per output sample
    readonly List<float> pending = new();
    double position;                 // next output sample's position in `pending`

    public AudioConverter(int sampleRate, int channels, bool isFloat, int bitsPerSample)
    {
        if (isFloat && bitsPerSample != 32) throw new ArgumentException("float audio must be 32-bit");
        if (!isFloat && bitsPerSample != 16 && bitsPerSample != 24 && bitsPerSample != 32)
            throw new ArgumentException($"unsupported PCM bit depth {bitsPerSample}");
        this.channels = channels;
        this.isFloat = isFloat;
        bytesPerSample = bitsPerSample / 8;
        step = (double)sampleRate / TargetRate;
    }

    float Sample(byte[] b, int offset) => isFloat
        ? BitConverter.ToSingle(b, offset)
        : bytesPerSample switch
        {
            2 => BitConverter.ToInt16(b, offset) / 32768f,
            3 => ((b[offset] << 8 | b[offset + 1] << 16 | b[offset + 2] << 24) >> 8) / 8388608f,
            _ => BitConverter.ToInt32(b, offset) / 2147483648f,
        };

    /// <summary>Converts `count` bytes; keeps what is left over for the next call so chunks join smoothly.</summary>
    public short[] Convert(byte[] buffer, int count)
    {
        int frame = bytesPerSample * channels;
        for (int off = 0; off + frame <= count; off += frame)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++) sum += Sample(buffer, off + c * bytesPerSample);
            pending.Add(sum / channels);
        }

        var output = new List<short>();
        double half = step / 2;
        // Average the input around each output position: a simple low-pass so high sounds do not fold down.
        while (position + half < pending.Count)
        {
            int from = Math.Max(0, (int)Math.Ceiling(position - half));
            int to = Math.Min(pending.Count - 1, (int)Math.Floor(position + half));
            float acc = 0;
            for (int i = from; i <= to; i++) acc += pending[i];
            float v = to >= from ? acc / (to - from + 1) : pending[(int)position];
            output.Add((short)Math.Clamp((int)Math.Round(v * 32767), short.MinValue, short.MaxValue));
            position += step;
        }

        int drop = Math.Max(0, (int)Math.Floor(position - half) - 1);
        if (drop > 0)
        {
            pending.RemoveRange(0, drop);
            position -= drop;
        }
        return output.ToArray();
    }
}

/// <summary>Cuts a stream of samples into equal frames, carrying the remainder over.</summary>
public class Framer
{
    readonly int frameSamples;
    readonly List<short> buffer = new();

    public Framer(int frameSamples = AudioPacket.Samples) => this.frameSamples = frameSamples;

    public int Pending => buffer.Count;

    public IEnumerable<short[]> Push(short[] samples)
    {
        buffer.AddRange(samples);
        var frames = new List<short[]>();
        while (buffer.Count >= frameSamples)
        {
            frames.Add(buffer.GetRange(0, frameSamples).ToArray());
            buffer.RemoveRange(0, frameSamples);
        }
        return frames;
    }
}

/// <summary>Notices VRChat starting, restarting (a new process id) and closing.</summary>
public class ProcessWatcher
{
    readonly Func<int?> findPid;
    int? last;
    bool first = true;

    public ProcessWatcher(Func<int?> findPid) => this.findPid = findPid;

    /// <summary>True when the last Poll() saw a different process than the one before.</summary>
    public bool Changed { get; private set; }

    public int? Poll()
    {
        int? now = findPid();
        Changed = first ? now != null : now != last;
        first = false;
        last = now;
        return now;
    }
}
