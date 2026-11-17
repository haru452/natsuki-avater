package com.haru452.vrctranslator.app

import com.haru452.vrctranslator.core.TextTranslator

/** JNI into native/translator_llm.c (our own llama.cpp build). */
object NativeLlmJni {
    init {
        System.loadLibrary("translator_llm")
    }

    @JvmStatic external fun load(path: String, threads: Int, ctx: Int): Long
    @JvmStatic external fun complete(handle: Long, prompt: ByteArray, maxTokens: Int): ByteArray?
    @JvmStatic external fun cancel(handle: Long)
    @JvmStatic external fun free(handle: Long)
}

/** Gemma 1B and 4B kept loaded side by side. One completion at a time. */
class NativeLlm(paths: Map<String, String>, threads: Int = 6) : TextTranslator, AutoCloseable {
    companion object {
        const val CONTEXT = 1024
        const val MAX_TOKENS = 96
    }

    private val handles: Map<String, Long> = paths.mapValues { (name, path) ->
        NativeLlmJni.load(path, threads, CONTEXT).also { require(it != 0L) { "could not load $name model" } }
    }
    @Volatile private var running = 0L

    @Synchronized
    override fun complete(model: String, prompt: String): String {
        val h = handles[model] ?: error("no $model model")
        running = h
        try {
            val out = NativeLlmJni.complete(h, prompt.toByteArray(Charsets.UTF_8), MAX_TOKENS)
                ?: throw IllegalStateException("llama decode failed or was cancelled")
            return String(out, Charsets.UTF_8)
        } finally {
            running = 0L
        }
    }

    override fun cancel() {
        val h = running
        if (h != 0L) NativeLlmJni.cancel(h)
    }

    override fun close() = handles.values.forEach(NativeLlmJni::free)
}
