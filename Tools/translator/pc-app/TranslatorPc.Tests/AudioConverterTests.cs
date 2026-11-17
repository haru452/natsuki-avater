using TranslatorPc.Core;
using Xunit;

namespace TranslatorPc.Tests;

public class AudioConverterTests
{
    static byte[] FloatStereo(int rate, double seconds, double freq, bool opposite = false)
    {
        int n = (int)(rate * seconds);
        var b = new byte[n * 2 * 4];
        for (int i = 0; i < n; i++)
        {
            float v = (float)(0.5 * Math.Sin(2 * Math.PI * freq * i / rate));
            BitConverter.GetBytes(v).CopyTo(b, i * 8);
            BitConverter.GetBytes(opposite ? -v : v).CopyTo(b, i * 8 + 4);
        }
        return b;
    }

    static byte[] Int16Mono(int rate, double seconds, double freq)
    {
        int n = (int)(rate * seconds);
        var b = new byte[n * 2];
        for (int i = 0; i < n; i++)
            BitConverter.GetBytes((short)(12000 * Math.Sin(2 * Math.PI * freq * i / rate))).CopyTo(b, i * 2);
        return b;
    }

    static int ZeroCrossings(short[] s) => Enumerable.Range(1, s.Length - 1).Count(i => (s[i - 1] < 0) != (s[i] < 0));

    [Fact]
    public void FloatStereo48kBecomes16kMono()
    {
        var c = new AudioConverter(48000, 2, true, 32);
        var b = FloatStereo(48000, 0.1, 1000);
        var o = c.Convert(b, b.Length);
        Assert.InRange(o.Length, 1599, 1601);
        Assert.InRange(ZeroCrossings(o) / 2.0 / 0.1, 970, 1030);  // 1 kHz
        Assert.InRange(o.Max(), 0.95 * 0.5 * 32767, 1.05 * 0.5 * 32767);
    }

    [Fact]
    public void Int16Mono44kBecomes16k()
    {
        var c = new AudioConverter(44100, 1, false, 16);
        var b = Int16Mono(44100, 0.1, 440);
        Assert.InRange(c.Convert(b, b.Length).Length, 1598, 1602);
    }

    [Fact]
    public void OppositeChannelsCancel()
    {
        var c = new AudioConverter(48000, 2, true, 32);
        var b = FloatStereo(48000, 0.1, 1000, opposite: true);
        Assert.True(c.Convert(b, b.Length).All(s => Math.Abs(s) <= 2));
    }

    [Fact]
    public void ChunksJoinSmoothly()
    {
        var c = new AudioConverter(48000, 1, false, 16);
        var b = Int16Mono(48000, 0.1, 500);
        var whole = new AudioConverter(48000, 1, false, 16).Convert(b, b.Length);
        var parts = c.Convert(b[..3000], 3000).Concat(c.Convert(b[3000..], b.Length - 3000)).ToArray();
        Assert.InRange(parts.Length, whole.Length - 1, whole.Length + 1);
    }

    [Fact]
    public void FramerCarriesTheRemainder()
    {
        var f = new Framer(320);
        var frames = f.Push(new short[500]).Concat(f.Push(new short[300])).Concat(f.Push(new short[500])).ToList();
        Assert.Equal(4, frames.Count);
        Assert.All(frames, fr => Assert.Equal(320, fr.Length));
        Assert.Equal(20, f.Pending);
    }

    [Fact]
    public void ProcessWatcherFollowsVrchat()
    {
        int? pid = null;
        var w = new ProcessWatcher(() => pid);
        Assert.Null(w.Poll());
        pid = 100;
        Assert.Equal(100, w.Poll());
        Assert.True(w.Changed);
        Assert.Equal(100, w.Poll());
        Assert.False(w.Changed);
        pid = 200;
        Assert.Equal(200, w.Poll());
        Assert.True(w.Changed);
        pid = null;
        Assert.Null(w.Poll());
        Assert.True(w.Changed);
    }
}
