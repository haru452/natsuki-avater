// Port of Tools/translator/kat_osc.py: KAT (KillFrenzy Avatar Text) character codes and OSC messages.
// The character table matches KAT v1.3.2's KAT_CharTiles.png (16 x 16 tiles).
// The avatar's toggle owns KAT_Visible; the sender only clears (KAT_Pointer = 255) and writes.
// Each group sends the pointer before its characters, because KAT writes the CharSync floats into
// whichever slot the pointer names.
package com.haru452.vrctranslator.core

import java.io.ByteArrayOutputStream
import java.nio.ByteBuffer
import java.nio.charset.CharacterCodingException
import java.nio.charset.CodingErrorAction
import java.text.Normalizer

object KatOsc {
    const val TEXT_LIMIT = 128
    const val CLEAR_POINTER = 255
    const val PARAM_PREFIX = "/avatar/parameters/"
    // Must match TranslatorConfig.KatSyncChoices on the avatar side.
    val SYNC_CHOICES = listOf(4, 8, 16)

    private val ROWS = mapOf(
        129 to "ふあうえおやゆよわをほへたてい",
        144 to "すかんなにらせちとしはきくまのり",
        160 to "れけむつさそひこみもねるめろ。ぶ",
        176 to "ぷぼぽべぺだでずがぜぢどじばぱぎ",
        192 to "ぐげづざぞびぴごぁぃぅぇぉゃゅょ",
        208 to "ヌフアウエオヤユヨワヲホヘタテイ",
        224 to "スカンナニラセチトシハキクマノリ",
        240 to "レケムツサソヒコミモネルメロ゛゜",
    )

    // Characters the tile sheet lacks, replaced with the closest one it has.
    private val SUBSTITUTES = mapOf(
        "っ" to "つ", "ゎ" to "わ", "ゔ" to "う゛",
        "ッ" to "ツ", "ャ" to "ヤ", "ュ" to "ユ", "ョ" to "ヨ", "ヮ" to "ワ",
        "ァ" to "ア", "ィ" to "イ", "ゥ" to "ウ", "ェ" to "エ", "ォ" to "オ",
        "ー" to "-", "、" to ",", "・" to ".", "「" to "[", "」" to "]", "『" to "[", "』" to "]",
        "〜" to "~", "～" to "~", "…" to "...", "’" to "'", "‘" to "'", "“" to "\"", "”" to "\"",
        "—" to "-", "–" to "-", "゙" to "゛", "゚" to "゜", "\n" to " ", "\t" to " ", "\r" to " ",
    )

    private val TABLE: Map<String, Int> = buildMap {
        for (c in 32 until 127) put(c.toChar().toString(), c - 32)
        put("€", 95)
        put("ぬ", 127)
        for ((start, chars) in ROWS) chars.forEachIndexed { i, ch -> put(ch.toString(), start + i) }
    }
    private val UNKNOWN = TABLE.getValue("?")

    private fun codePoints(text: String): List<String> {
        val out = ArrayList<String>()
        var i = 0
        while (i < text.length) {
            val n = Character.charCount(text.codePointAt(i))
            out.add(text.substring(i, i + n))
            i += n
        }
        return out
    }

    private fun addCodes(ch: String, into: MutableList<Int>, depth: Int = 0) {
        TABLE[ch]?.let { into.add(it); return }
        if (depth > 3) { into.add(UNKNOWN); return }
        SUBSTITUTES[ch]?.let { sub -> codePoints(sub).forEach { addCodes(it, into, depth + 1) }; return }
        for (form in listOf(Normalizer.Form.NFKC, Normalizer.Form.NFD)) {
            val normalized = Normalizer.normalize(ch, form)
            if (normalized != ch) {
                codePoints(normalized).forEach { addCodes(it, into, depth + 1) }
                return
            }
        }
        into.add(UNKNOWN)
    }

    /** Text to KAT character codes, at most TEXT_LIMIT. Unsupported characters become '?'. */
    fun encodeText(text: String): List<Int> {
        val codes = ArrayList<Int>()
        for (ch in codePoints(text)) {
            addCodes(ch, codes)
            if (codes.size >= TEXT_LIMIT) break
        }
        return if (codes.size > TEXT_LIMIT) codes.subList(0, TEXT_LIMIT).toList() else codes
    }

    /** How many KAT cells one character (code point) takes: 2 for katakana written with a separate voicing mark. */
    fun charWidth(ch: String): Int = ArrayList<Int>().also { addCodes(ch, it) }.size

    /** Width of a whole string in KAT cells. */
    fun cells(text: String): Int = codePoints(text).sumOf { charWidth(it) }

    /** Splits a string into code points (for callers that walk text character by character). */
    fun chars(text: String): List<String> = codePoints(text)

    /** KAT maps codes 0..127 to 0..1 and 129..255 to -1..-1/127. */
    fun codeToFloat(code: Int): Float = (if (code > 127) code - 256 else code) / 127f

    private fun ByteArrayOutputStream.padded(raw: ByteArray) {
        write(raw)
        repeat(4 - raw.size % 4) { write(0) }
    }

    /** One OSC message. Values may be Boolean, Int, Float or String (sent as UTF-8). */
    fun oscMessage(address: String, vararg values: Any): ByteArray {
        val tags = StringBuilder(",")
        val body = ByteArrayOutputStream()
        for (value in values) {
            when (value) {
                is Boolean -> tags.append(if (value) 'T' else 'F')
                is Int -> { tags.append('i'); body.write(ByteBuffer.allocate(4).putInt(value).array()) }
                is Float -> { tags.append('f'); body.write(ByteBuffer.allocate(4).putFloat(value).array()) }
                is Double -> { tags.append('f'); body.write(ByteBuffer.allocate(4).putFloat(value.toFloat()).array()) }
                is String -> { tags.append('s'); body.padded(value.toByteArray(Charsets.UTF_8)) }
                else -> throw IllegalArgumentException("unsupported OSC value: $value")
            }
        }
        val out = ByteArrayOutputStream()
        out.padded(address.toByteArray(Charsets.US_ASCII))
        out.padded(tags.toString().toByteArray(Charsets.US_ASCII))
        out.write(body.toByteArray())
        return out.toByteArray()
    }

    private class Reader(val data: ByteArray) {
        var pos = 0
        private val decoder = Charsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT)

        fun string(): String {
            require(pos <= data.size) { "truncated OSC message" }
            var end = pos
            while (end < data.size && data[end] != 0.toByte()) end++
            require(end < data.size) { "unterminated OSC string" }
            val s = try {
                decoder.decode(ByteBuffer.wrap(data, pos, end - pos)).toString()
            } catch (e: CharacterCodingException) {
                throw IllegalArgumentException("bad UTF-8 in OSC string", e)
            }
            pos = (end / 4 + 1) * 4
            return s
        }

        fun four(): ByteBuffer {
            require(pos + 4 <= data.size) { "truncated OSC argument" }
            return ByteBuffer.wrap(data, pos, 4).also { pos += 4 }
        }
    }

    /** (address, values) from one OSC message. Throws IllegalArgumentException on anything malformed. */
    fun parseMessage(data: ByteArray): Pair<String, List<Any>> {
        val r = Reader(data)
        val address = r.string()
        require(address.startsWith("/")) { "OSC address must start with /" }
        val tags = r.string()
        require(tags.startsWith(",")) { "missing OSC type tags" }
        val values = ArrayList<Any>()
        for (tag in tags.substring(1)) {
            when (tag) {
                'T' -> values.add(true)
                'F' -> values.add(false)
                'i' -> values.add(r.four().int)
                'f' -> values.add(r.four().float)
                's' -> values.add(r.string())
                else -> throw IllegalArgumentException("unsupported OSC type tag $tag")
            }
        }
        return address to values
    }

    /**
     * Groups of OSC messages in send order; wait between groups so each one syncs.
     * clear = false rewrites the same slots without wiping first (periodic resends, no flicker).
     */
    fun buildMessages(text: String, sync: Int = 8, clear: Boolean = true): List<List<ByteArray>> {
        require(sync in SYNC_CHOICES) { "sync must be one of $SYNC_CHOICES, got $sync" }
        val codes = encodeText(text)
        val groups = ArrayList<List<ByteArray>>()
        if (clear) groups.add(listOf(oscMessage(PARAM_PREFIX + "KAT_Pointer", CLEAR_POINTER)))
        for (start in codes.indices step sync) {
            val group = ArrayList<ByteArray>()
            group.add(oscMessage(PARAM_PREFIX + "KAT_Pointer", start / sync + 1))
            for (i in 0 until sync) {
                val code = codes.getOrElse(start + i) { 0 }
                group.add(oscMessage("${PARAM_PREFIX}KAT_CharSync$i", codeToFloat(code)))
            }
            groups.add(group)
        }
        return groups
    }
}
