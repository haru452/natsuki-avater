// Saving spoken sentences (only while the wearer turns it on with R) and comparing recognizers on them.
using System.Diagnostics;
using System.Text;
using TranslatorPc.Core;

namespace TranslatorPc;

/// <summary>Passes speech to the real recognizer; while On, also saves each sentence as a WAV in Dir.</summary>
sealed class SampleRecorder : IRecognizer
{
    public static readonly string Dir = Path.Combine(Path.GetDirectoryName(Settings.DefaultPath)!, "samples");
    readonly IRecognizer inner;
    public volatile bool On;
    public int Saved { get; private set; }

    public SampleRecorder(IRecognizer inner) => this.inner = inner;

    public RecognizedSpeech Recognize(short[] pcm)
    {
        if (On)
        {
            Directory.CreateDirectory(Dir);
            Wav.Write(Path.Combine(Dir, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".wav"), pcm);
            Saved++;
        }
        return inner.Recognize(pcm);
    }
}

static class Wav
{
    /// <summary>16 kHz mono 16-bit PCM, the plain 44-byte header.</summary>
    public static void Write(string path, short[] pcm)
    {
        using var w = new BinaryWriter(File.Create(path));
        int bytes = pcm.Length * 2;
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Segmenter.Rate); w.Write(Segmenter.Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
        foreach (var s in pcm) w.Write(s);
    }

    public static short[] Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var pcm = new short[(bytes.Length - 44) / 2];
        Buffer.BlockCopy(bytes, 44, pcm, 0, pcm.Length * 2);
        return pcm;
    }
}

static class Compare
{
    /// <summary>Every WAV in dir through each recognizer; prints and writes dir\compare.tsv (text and seconds).</summary>
    public static int Run(string dir, IReadOnlyList<(string Name, IRecognizer Recognizer)> recognizers)
    {
        var files = Directory.GetFiles(dir, "*.wav").OrderBy(f => f).ToArray();
        if (files.Length == 0) { Console.WriteLine("WAV がありません: " + dir); return 1; }
        var lines = new List<string> { string.Join("\t", new[] { "file", "seconds" }.Concat(recognizers.SelectMany(r => new[] { r.Name, r.Name + " s" }))) };
        foreach (var file in files)
        {
            var pcm = Wav.Read(file);
            var cells = new List<string> { Path.GetFileName(file), (pcm.Length / (double)Segmenter.Rate).ToString("0.0") };
            Console.WriteLine($"{Path.GetFileName(file)}（{cells[1]} 秒）");
            foreach (var (name, recognizer) in recognizers)
            {
                var sw = Stopwatch.StartNew();
                var heard = recognizer.Recognize(pcm);
                cells.Add(heard.Text);
                cells.Add(sw.Elapsed.TotalSeconds.ToString("0.00"));
                Console.WriteLine($"  {name,-16} {sw.Elapsed.TotalSeconds:0.00} 秒  {heard.Text}");
            }
            lines.Add(string.Join("\t", cells));
        }
        File.WriteAllLines(Path.Combine(dir, "compare.tsv"), lines, Encoding.UTF8);
        return 0;
    }
}
