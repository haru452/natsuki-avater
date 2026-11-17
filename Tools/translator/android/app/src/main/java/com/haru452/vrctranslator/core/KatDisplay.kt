// Port of the Display class in Tools/translator/phone/display.py, plus Chinese routing:
// KAT has no Chinese characters, so anything going to Chinese is shown in the chatbox only.
package com.haru452.vrctranslator.core

/** What OwnVoicePipeline drives; KatDisplay sends to VRChat, tests use a fake. */
interface Display {
    var targetIsChinese: Boolean
    fun thinking()
    fun show(r: TranslationResult)
    fun error()
    fun clear()
    fun stop()
    fun tick()
}

/**
 * thinking() / show(result) / error() / clear() / stop(), then call tick() every ~50 ms.
 * A shown page is rewritten (without clearing) every resendSeconds so late joiners get all of it;
 * long text turns to the next page pageSeconds after a page is fully written. An answer on screen is
 * never cut short: "...", errors and clears wait, and a newer answer queues behind it.
 * Not thread-safe: call it from one thread (or under one lock).
 */
class KatDisplay(
    private val send: (ByteArray) -> Unit,
    mode: DisplayMode,
    private val sync: Int,
    private val showOriginal: Boolean,
    private val clock: () -> Double,
    private val pageSeconds: Double = 5.0,
    private val resendSeconds: Double = 3.0,
    private val stepSeconds: Double = 0.25,
    private val errorSeconds: Double = 3.0,
    /** Diagnostics: when an answer has to wait for the one on screen, or is dropped. */
    private val onEvent: (String) -> Unit = {},
) : Display {
    companion object {
        const val MAX_BACKLOG = 3
        private val POINTER = KatOsc.PARAM_PREFIX + "KAT_Pointer"
        /** Characters follow their pointer this long after it (both within one stepSeconds). */
        const val POINTER_SECONDS = 0.1
    }

    private val katMode = mode == DisplayMode.KAT || mode == DisplayMode.BOTH
    private val chatboxMode = mode == DisplayMode.CHATBOX || mode == DisplayMode.BOTH

    /** Set before thinking(): when the answer will be Chinese, "..." goes to the chatbox, not KAT. */
    override var targetIsChinese = false

    private val kat get() = katMode && !targetIsChinese
    private val chatbox get() = chatboxMode || targetIsChinese

    private var queue = ArrayList<List<ByteArray>>()
    private var next = 0.0
    private var pages: List<String> = emptyList()
    private var page = 0
    private var writtenAt: Double? = null
    private var errorUntil: Double? = null
    private var backlog = ArrayList<List<String>>()

    // --- commands -------------------------------------------------------

    override fun thinking() {
        if (kat && !busy()) {
            reset()
            write("...", clear = true)
        }
        if (chatbox) safeSend(KatOsc.oscMessage("/chatbox/typing", true))
    }

    override fun show(r: TranslationResult) {
        val chinese = r.target == Lang.ZH
        if (katMode && !chinese) {
            val newPages = Compose.paginate(Compose.kat(r, showOriginal))
            if (busy()) {
                // Let the answer on screen finish and be read; this one follows it.
                backlog.add(newPages)
                onEvent("表示を待ちます（前の訳を表示中。待ち ${backlog.size} 件）: ${r.translation}")
                while (backlog.size > MAX_BACKLOG) onEvent("表示を飛ばしました（待ちが多すぎます）: " + backlog.removeAt(0).joinToString(""))
            } else {
                start(newPages)
            }
        }
        if (chatboxMode || chinese) {
            safeSend(KatOsc.oscMessage("/chatbox/input", Compose.chatbox(r, showOriginal), true, false))
            safeSend(KatOsc.oscMessage("/chatbox/typing", false))
        }
    }

    override fun error() {
        if (kat && !busy()) {
            reset()
            write("(error)", clear = true)
            errorUntil = clock() + errorSeconds
        }
        if (chatbox) safeSend(KatOsc.oscMessage("/chatbox/typing", false))
    }

    /** Wipe the text (e.g. the "..." when the audio turned out to hold no speech). */
    override fun clear() {
        if (kat && !busy()) {
            reset()
            queue = arrayListOf(listOf(KatOsc.oscMessage(POINTER, KatOsc.CLEAR_POINTER)))
            next = clock()
        }
        if (chatbox) safeSend(KatOsc.oscMessage("/chatbox/typing", false))
    }

    /** Forget everything; the avatar's toggle clears the text itself. */
    override fun stop() {
        reset()
        backlog = ArrayList()
    }

    // --- timing ---------------------------------------------------------

    override fun tick() {
        val now = clock()
        if (queue.isNotEmpty()) {
            if (now >= next) {
                val group = queue.removeAt(0)
                val isPointer = group.size > 1 && KatOsc.parseMessage(group[0]).first == POINTER
                if (isPointer) {
                    // KAT writes whatever characters are current into the pointer's cells until the pointer moves,
                    // so move the pointer first and send its characters a moment later ("yeah" became "yeあh").
                    safeSend(group[0])
                    queue.add(0, group.drop(1))
                    next = now + POINTER_SECONDS
                    return
                }
                group.forEach(::safeSend)
                val isClear = group.size == 1 && KatOsc.parseMessage(group[0]) == (POINTER to listOf<Any>(KatOsc.CLEAR_POINTER))
                val isChars = KatOsc.parseMessage(group[0]).first != POINTER
                // Hold the clear so nobody misses it; a pointer + characters pair keeps its step in total.
                next = now + stepSeconds * (if (isClear) 2 else 1) - (if (isChars) POINTER_SECONDS else 0.0)
                if (queue.isEmpty()) writtenAt = now
            }
            return
        }
        errorUntil?.let { until ->
            if (now >= until) {
                errorUntil = null
                safeSend(KatOsc.oscMessage(POINTER, KatOsc.CLEAR_POINTER))
            }
            return
        }
        val written = writtenAt ?: return
        if (pages.isEmpty()) return
        when {
            page < pages.size - 1 -> if (now - written >= pageSeconds) {
                page++
                write(pages[page], clear = true)
            }
            backlog.isNotEmpty() -> if (now - written >= pageSeconds) start(backlog.removeAt(0))
            now - written >= resendSeconds -> write(pages[page], clear = false)
        }
    }

    // --- helpers --------------------------------------------------------

    /** True while an answer is being written, or has not yet been up for pageSeconds. */
    private fun busy(): Boolean {
        if (backlog.isNotEmpty()) return true
        if (pages.isEmpty()) return false
        val written = writtenAt
        if (queue.isNotEmpty() || written == null || page < pages.size - 1) return true
        return clock() - written < pageSeconds
    }

    private fun start(newPages: List<String>) {
        reset()
        pages = newPages
        write(newPages[0], clear = true)
    }

    private fun reset() {
        queue = ArrayList()
        next = 0.0
        pages = emptyList()
        page = 0
        writtenAt = null
        errorUntil = null
    }

    private fun write(text: String, clear: Boolean) {
        queue = ArrayList(KatOsc.buildMessages(text, sync, clear))
        next = clock()
        writtenAt = null
    }

    private fun safeSend(packet: ByteArray) {
        try {
            send(packet)
        } catch (e: Exception) {
            // A dropped Wi-Fi must never stop the translator.
        }
    }
}
