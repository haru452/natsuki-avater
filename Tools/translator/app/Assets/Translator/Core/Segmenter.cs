// Port of the Segmenter in Tools/translator/phone/voice.py.
using System;
using System.Collections.Generic;

namespace Translator.Core
{
    public class SegmenterSettings
    {
        public float StartS = 0.15f;
        public float EndSilenceS = 0.8f;
        public float PrerollS = 0.3f;
        public float MaxS = 60f;
        public float MinS = 0.5f;
        public float Margin = 3.0f;
        public float FloorMin = 200f;
    }

    /// <summary>
    /// Feed 30 ms frames; get back one utterance (PCM) when the speaker stops.
    /// A frame is "voice" when it is Margin times louder than the room's noise floor (and above FloorMin * Margin).
    /// Recording starts after StartS of voice, keeps PrerollS from before the start, ends after EndSilenceS of
    /// quiet (keeping a short tail), and is cut at MaxS. Utterances with less than MinS of voice are dropped.
    /// </summary>
    public class Segmenter
    {
        public const int Rate = 16000;
        public const int FrameMs = 30;
        public const int FrameSamples = Rate * FrameMs / 1000;
        const float TailS = 0.2f;

        readonly int startFrames, endFrames, maxFrames, minFrames, tailFrames, prerollMax;
        readonly float margin, floorMin;
        readonly Queue<short[]> preroll = new Queue<short[]>();
        List<short[]> buffer = new List<short[]>();
        int voicedRun, voicedTotal, silence;

        public float NoiseFloor { get; private set; }
        public bool Recording { get; private set; }
        public float Threshold => Math.Max(NoiseFloor, floorMin) * margin;

        static int Frames(float seconds) => Math.Max(1, (int)Math.Round(seconds * 1000 / FrameMs, MidpointRounding.ToEven));

        public Segmenter(SegmenterSettings s)
        {
            startFrames = Frames(s.StartS);
            endFrames = Frames(s.EndSilenceS);
            maxFrames = Frames(s.MaxS);
            minFrames = Frames(s.MinS);
            tailFrames = Frames(TailS);
            prerollMax = Frames(s.PrerollS) + startFrames;
            margin = s.Margin;
            floorMin = s.FloorMin;
            NoiseFloor = floorMin;
            Reset();
        }

        public static float Rms(short[] frame)
        {
            if (frame == null || frame.Length == 0) return 0f;
            double sum = 0;
            foreach (short v in frame) sum += (double)v * v;
            return (float)Math.Sqrt(sum / frame.Length);
        }

        /// <summary>Forget anything half-recorded (used when the toggle turns off).</summary>
        public void Reset()
        {
            Recording = false;
            buffer = new List<short[]>();
            voicedRun = voicedTotal = silence = 0;
            preroll.Clear();
        }

        public short[] Feed(short[] frame)
        {
            float level = Rms(frame);
            bool voiced = level > Threshold;
            // Follow the room: quickly while quiet, very slowly otherwise, so a new steady noise (a fan)
            // eventually stops counting as voice.
            float alpha = voiced ? 0.002f : 0.05f;
            if (!Recording)
            {
                NoiseFloor += (level - NoiseFloor) * alpha;
                preroll.Enqueue(frame);
                while (preroll.Count > prerollMax) preroll.Dequeue();
                voicedRun = voiced ? voicedRun + 1 : 0;
                if (voicedRun >= startFrames)
                {
                    Recording = true;
                    buffer = new List<short[]>(preroll);
                    voicedTotal = voicedRun;
                    silence = 0;
                }
                return null;
            }

            buffer.Add(frame);
            if (voiced) { voicedTotal++; silence = 0; }
            else silence++;

            if (buffer.Count >= maxFrames) return Finish(false);
            if (silence >= endFrames) return Finish(true);
            return null;
        }

        short[] Finish(bool trim)
        {
            var frames = buffer;
            int count = trim ? frames.Count - (silence - tailFrames) : frames.Count;
            bool enough = voicedTotal >= minFrames;
            Reset();
            if (!enough) return null;
            int total = 0;
            for (int i = 0; i < count; i++) total += frames[i].Length;
            var pcm = new short[total];
            int pos = 0;
            for (int i = 0; i < count; i++)
            {
                Array.Copy(frames[i], 0, pcm, pos, frames[i].Length);
                pos += frames[i].Length;
            }
            return pcm;
        }
    }
}
