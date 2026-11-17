// Ported from Tools/translator/phone/test_voice.py (Rms and Segmenter cases, same values).
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Translator.Core.Tests
{
    public class SegmenterTests
    {
        const int Quiet = 60;     // room noise
        const int Speech = 6000;  // talking

        internal static List<short[]> Frames(double seconds, int amplitude, double freq = 220.0)
        {
            var o = new List<short[]>();
            int n = (int)Math.Round(seconds * 1000 / 30);
            for (int f = 0; f < n; f++)
            {
                var s = new short[Segmenter.FrameSamples];
                for (int i = 0; i < s.Length; i++)
                    s[i] = (short)(int)(amplitude * Math.Sin(2 * Math.PI * freq * (f * s.Length + i) / Segmenter.Rate));
                o.Add(s);
            }
            return o;
        }

        static List<short[]> FeedAll(Segmenter seg, IEnumerable<short[]> chunks) =>
            chunks.Select(seg.Feed).Where(p => p != null).ToList();

        static double SecondsOf(short[] pcm) => pcm.Length / (double)Segmenter.Rate;

        static List<short[]> Join(params List<short[]>[] parts) => parts.SelectMany(p => p).ToList();

        static Segmenter New(Action<SegmenterSettings> change = null)
        {
            var s = new SegmenterSettings();
            change?.Invoke(s);
            return new Segmenter(s);
        }

        [Test] public void RmsOfSine() => Assert.AreEqual(1000 / Math.Sqrt(2), Segmenter.Rms(Frames(0.03, 1000)[0]), 15);

        [Test] public void RmsOfSilenceAndEmpty()
        {
            Assert.AreEqual(0.0, Segmenter.Rms(Frames(0.03, 0)[0]));
            Assert.AreEqual(0.0, Segmenter.Rms(new short[0]));
        }

        [Test] public void OneUtteranceWithPreroll()
        {
            var seg = New();
            var got = FeedAll(seg, Join(Frames(1, Quiet), Frames(1, Speech), Frames(1, Quiet)));
            Assert.AreEqual(1, got.Count);
            Assert.Greater(SecondsOf(got[0]), 1.25);
            Assert.Less(SecondsOf(got[0]), 1.6);
            Assert.IsFalse(seg.Recording);
        }

        [Test] public void WaitsFor08SecondsOfSilence()
        {
            var seg = New();
            Assert.IsEmpty(FeedAll(seg, Join(Frames(1, Quiet), Frames(1, Speech), Frames(0.6, Quiet))));
            Assert.IsTrue(seg.Recording, "a short pause is part of the same sentence");
            Assert.AreEqual(1, FeedAll(seg, Frames(0.3, Quiet)).Count);
        }

        [Test] public void PauseInsideSentenceKeepsOneUtterance()
        {
            var seg = New();
            var chunks = Join(Frames(1, Quiet), Frames(1, Speech), Frames(0.5, Quiet), Frames(1, Speech), Frames(1, Quiet));
            Assert.AreEqual(1, FeedAll(seg, chunks).Count);
        }

        [Test] public void ShortNoiseIsDropped() =>
            Assert.IsEmpty(FeedAll(New(), Join(Frames(1, Quiet), Frames(0.3, Speech), Frames(1, Quiet))));

        [Test] public void TwoUtterances()
        {
            var one = Join(Frames(1, Quiet), Frames(1, Speech), Frames(1, Quiet));
            Assert.AreEqual(2, FeedAll(New(), Join(one, one)).Count);
        }

        [Test] public void LongSpeechIsCutAtMax()
        {
            var got = FeedAll(New(s => s.MaxS = 5), Join(Frames(0.5, Quiet), Frames(6, Speech)));
            Assert.AreEqual(1, got.Count);
            Assert.AreEqual(5, SecondsOf(got[0]), 0.05);
        }

        [Test] public void NoiseFloorFollowsALouderRoom()
        {
            var seg = New();
            FeedAll(seg, Frames(1, Quiet));
            float quietFloor = seg.NoiseFloor;
            FeedAll(seg, Frames(0.1, 400));  // a fan switches on: too short to count as speech
            FeedAll(seg, Frames(4, 400));
            Assert.Greater(seg.NoiseFloor, quietFloor * 3);
            Assert.IsEmpty(FeedAll(seg, Frames(2, 400)), "steady noise is not speech");
        }

        [Test] public void ResetDiscardsARecording()
        {
            var seg = New();
            FeedAll(seg, Join(Frames(1, Quiet), Frames(1, Speech)));
            Assert.IsTrue(seg.Recording);
            seg.Reset();
            Assert.IsFalse(seg.Recording);
            Assert.IsEmpty(FeedAll(seg, Frames(1, Quiet)));
        }
    }
}
