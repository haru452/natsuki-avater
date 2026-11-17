package com.haru452.vrctranslator.app

import android.content.Context
import java.io.File

/**
 * The models live in the app's internal storage (fast to read). While developing they are pushed with adb to
 * the app's shared folder (Android/data/<app>/files/models) and copied in on the next start.
 * Downloading them in the app comes in plan 4.
 */
class ModelFiles(context: Context) {
    companion object {
        val NAMES = mapOf(
            "senseVoiceModel" to "sense-voice/model.int8.onnx",
            "senseVoiceTokens" to "sense-voice/tokens.txt",
            "1B" to "gemma-3-1b-it-Q4_K_M.gguf",
            "4B" to "gemma-3-4b-it-Q4_K_M.gguf",
        )
    }

    val dir = File(context.filesDir, "models")
    private val shared = context.getExternalFilesDir(null)?.let { File(it, "models") }

    fun path(key: String) = File(dir, NAMES.getValue(key)).absolutePath

    /** Missing files, after copying in whatever adb left in the shared folder. */
    fun prepare(progress: (String) -> Unit): List<String> {
        val missing = ArrayList<String>()
        for (rel in NAMES.values) {
            val target = File(dir, rel)
            val source = shared?.let { File(it, rel) }
            if (source != null && source.exists() && (!target.exists() || target.length() != source.length())) {
                progress("モデルをコピーしています: ${source.name}")
                target.parentFile?.mkdirs()
                val tmp = File(target.path + ".part")
                source.copyTo(tmp, overwrite = true)
                tmp.renameTo(target)
                source.delete()  // keep only one copy (they are large)
            }
            if (!target.exists()) missing.add(rel)
        }
        return missing
    }
}
