// Ported from Tools/translator/test_kat_osc.py (same cases, same values). send() is not ported: KatDisplay paces the groups.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Translator.Core.Tests
{
    public class KatOscTests
    {
        static List<int> E(string s) => KatOsc.EncodeText(s);
        static List<int> Cat(List<int> a, params int[] b) => a.Concat(b).ToList();

        static byte[] B(params object[] parts)
        {
            var o = new List<byte>();
            foreach (var p in parts)
            {
                if (p is string s) o.AddRange(s.Select(c => (byte)c));
                else o.AddRange((byte[])p);
            }
            return o.ToArray();
        }

        static byte[] BigEndian(float f)
        {
            var b = BitConverter.GetBytes(f);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return b;
        }

        [Test] public void AsciiCodes()
        {
            Assert.AreEqual(new[] { 0 }, E(" "));
            Assert.AreEqual(new[] { 33 }, E("A"));
            Assert.AreEqual(new[] { 94 }, E("~"));
        }

        [Test] public void KanaCodes()
        {
            Assert.AreEqual(new[] { 127 }, E("ぬ"));
            Assert.AreEqual(new[] { 129 }, E("ふ"));
            Assert.AreEqual(new[] { 133, 154, 136, 131 }, E("おはよう"));
            Assert.AreEqual(new[] { 208 }, E("ヌ"));
            Assert.AreEqual(new[] { 253 }, E("ロ"));
        }

        [Test] public void UnknownCharBecomesQuestion()
        {
            Assert.AreEqual(E("?"), E("漢"));
            Assert.AreEqual(E("?"), E("😀"));
        }

        [Test] public void MissingKanaAreSubstituted()
        {
            Assert.AreEqual(E("つ"), E("っ"));
            Assert.AreEqual(E("ツ"), E("ッ"));
            Assert.AreEqual(E("-"), E("ー"));
            Assert.AreEqual(E(","), E("、"));
            Assert.AreEqual(E("ヤ"), E("ャ"));
        }

        [Test] public void VoicedKatakanaUsesSeparateMark()
        {
            Assert.AreEqual(Cat(E("カ"), 254), E("ガ"));
            Assert.AreEqual(Cat(E("ハ"), 255), E("パ"));
        }

        [Test] public void FullwidthBecomesHalfwidth() => Assert.AreEqual(E("A1!"), E("Ａ１！"));

        [Test] public void NeverEmitsUnsendableCode()
        {
            var sb = new StringBuilder();
            for (int c = 0x20; c < 0x3100; c++) sb.Append((char)c);
            var codes = E(sb.ToString());
            CollectionAssert.DoesNotContain(codes, 128);
            Assert.IsTrue(codes.All(c => c >= 0 && c <= 255));
        }

        [Test] public void TruncatesAtLimit()
        {
            Assert.AreEqual(128, E(new string('x', 300)).Count);
            Assert.AreEqual(128, E(string.Concat(Enumerable.Repeat("ガ", 100))).Count);
        }

        [Test] public void NewlineAndTabBecomeSpace() => Assert.AreEqual(E("a b c"), E("a\nb\tc"));

        [Test] public void FloatMapping()
        {
            Assert.AreEqual(0.0, KatOsc.CodeToFloat(0), 1e-6);
            Assert.AreEqual(1.0, KatOsc.CodeToFloat(127), 1e-6);
            Assert.AreEqual(-1.0, KatOsc.CodeToFloat(129), 1e-6);
            Assert.AreEqual(-1 / 127.0, KatOsc.CodeToFloat(255), 1e-6);
        }

        [Test] public void OscMessageLayout()
        {
            Assert.AreEqual(B("/a\0\0,i\0\0\0\0\0\x01"), KatOsc.OscMessage("/a", 1));
            Assert.AreEqual(B("/a\0\0,T\0\0"), KatOsc.OscMessage("/a", true));
            Assert.AreEqual(B("/a\0\0,F\0\0"), KatOsc.OscMessage("/a", false));
            Assert.AreEqual(B("/a\0\0,f\0\0", BigEndian(0.5f)), KatOsc.OscMessage("/a", 0.5f));
        }

        [Test] public void AddressPaddingIsMultipleOfFour() =>
            Assert.AreEqual(B("/abc\0\0\0\0,T\0\0"), KatOsc.OscMessage("/abc", true));

        [Test] public void RoundTripEachType()
        {
            foreach (var value in new object[] { true, false, 7, -3, 0.5f, "おはよう Hi" })
            {
                var (address, args) = KatOsc.ParseMessage(KatOsc.OscMessage("/a/b", value));
                Assert.AreEqual("/a/b", address);
                Assert.AreEqual(new[] { value }, args);
            }
        }

        [Test] public void SeveralArguments()
        {
            var (address, args) = KatOsc.ParseMessage(KatOsc.OscMessage("/chatbox/input", "hello", true, false));
            Assert.AreEqual("/chatbox/input", address);
            Assert.AreEqual(new object[] { "hello", true, false }, args);
        }

        [Test] public void BrokenPacketsRaiseFormatException()
        {
            var cases = new[]
            {
                B(""), B("/a"), B("/a\0\0"), B("/a\0\0,i\0\0\0\x01"), B("/a\0\0,x\0\0"),
                new byte[] { 0xff, 0xfe, 0, 0, (byte)',', (byte)'i', 0, 0 },
            };
            foreach (var data in cases)
                Assert.Throws<FormatException>(() => KatOsc.ParseMessage(data), BitConverter.ToString(data));
        }

        [Test] public void RewriteWithoutClear()
        {
            var groups = KatOsc.BuildMessages("Good morning", 8, false);
            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual(KatOsc.OscMessage("/avatar/parameters/KAT_Pointer", 1), groups[0][0]);
        }

        [Test] public void SyncChoicesMatchThePrefab()
        {
            Assert.AreEqual(new[] { 4, 8, 16 }, KatOsc.SyncChoices);
            Assert.Throws<ArgumentException>(() => KatOsc.BuildMessages("a", 2));
        }

        [Test] public void Groups()
        {
            var groups = KatOsc.BuildMessages("Good morning", 8);
            Assert.AreEqual(3, groups.Count);
            Assert.AreEqual(1, groups[0].Count);
            Assert.AreEqual(KatOsc.OscMessage("/avatar/parameters/KAT_Pointer", 255), groups[0][0]);
            Assert.AreEqual(9, groups[1].Count);
            Assert.AreEqual(KatOsc.OscMessage("/avatar/parameters/KAT_Pointer", 1), groups[1][0]);
            Assert.AreEqual(KatOsc.OscMessage("/avatar/parameters/KAT_CharSync0", 39 / 127f), groups[1][1]);
            Assert.AreEqual(KatOsc.OscMessage("/avatar/parameters/KAT_Pointer", 2), groups[2][0]);
        }

        [Test] public void LastGroupPaddedWithSpaces()
        {
            var groups = KatOsc.BuildMessages("Hi", 8);
            Assert.AreEqual(9, groups[1].Count);
            Assert.AreEqual(KatOsc.OscMessage("/avatar/parameters/KAT_CharSync7", 0f), groups[1][8]);
        }

        [Test] public void LongTextUsesAllPointers()
        {
            Assert.AreEqual(1 + 16, KatOsc.BuildMessages(new string('x', 300), 8).Count);
            Assert.AreEqual(1 + 32, KatOsc.BuildMessages(new string('x', 300), 4).Count);
        }

        [Test] public void EmptyTextOnlyClears() => Assert.AreEqual(1, KatOsc.BuildMessages("", 8).Count);

        [Test] public void RejectsBadSync() => Assert.Throws<ArgumentException>(() => KatOsc.BuildMessages("a", 3));
    }
}
