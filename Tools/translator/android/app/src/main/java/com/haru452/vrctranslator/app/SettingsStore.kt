package com.haru452.vrctranslator.app

import android.content.Context
import com.haru452.vrctranslator.core.DisplayMode
import com.haru452.vrctranslator.core.Lang
import com.haru452.vrctranslator.core.TranslatorSettings

/** TranslatorSettings in SharedPreferences. */
class SettingsStore(context: Context) {
    private val prefs = context.getSharedPreferences("settings", Context.MODE_PRIVATE)

    fun load() = TranslatorSettings(
        pcIp = prefs.getString("pcIp", "") ?: "",
        showOriginal = prefs.getBoolean("showOriginal", true),
        display = runCatching { DisplayMode.valueOf(prefs.getString("display", "KAT")!!) }.getOrDefault(DisplayMode.KAT),
        katSync = prefs.getInt("katSync", 8),
        targetLang = if (prefs.getString("targetLang", "EN") == "ZH") Lang.ZH else Lang.EN,
        ownModel = prefs.getString("ownModel", "1B") ?: "1B",
    )

    fun save(s: TranslatorSettings) {
        prefs.edit()
            .putString("pcIp", s.pcIp.trim())
            .putBoolean("showOriginal", s.showOriginal)
            .putString("display", s.display.name)
            .putInt("katSync", s.katSync)
            .putString("targetLang", s.targetLang.name)
            .putString("ownModel", s.ownModel)
            .apply()
    }
}
