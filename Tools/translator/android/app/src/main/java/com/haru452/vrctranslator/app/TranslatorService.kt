package com.haru452.vrctranslator.app

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.IBinder
import android.os.PowerManager
import android.os.SystemClock
import android.util.Log
import com.haru452.vrctranslator.core.Display
import com.haru452.vrctranslator.core.KatDisplay
import com.haru452.vrctranslator.core.KuromojiKana
import com.haru452.vrctranslator.core.OwnVoicePipeline
import com.haru452.vrctranslator.core.RelayLink
import com.haru452.vrctranslator.core.TranslationLog
import com.haru452.vrctranslator.core.TranslationResult
import com.haru452.vrctranslator.core.TranslatorSettings
import java.io.File
import java.time.LocalDateTime
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.update

/** What the screen shows. Written by the service's threads, read by MainActivity. */
data class Status(
    val running: Boolean = false,
    val models: String = "",
    val relayFound: Boolean = false,
    val listening: Boolean = false,
    val localAI: Boolean = true,
    val last: String = "",
    val note: String = "",
    /** Your latest sentence and its translation (the "自分の声" area). */
    val ownOriginal: String = "",
    val ownTranslation: String = "",
)

object TranslatorState {
    val status = MutableStateFlow(Status())

    /** One shared instance: the screen changes it (e.g. the target language) and the running service sees it. */
    @Volatile private var shared: TranslatorSettings? = null

    fun settings(context: Context): TranslatorSettings =
        shared ?: synchronized(this) { shared ?: SettingsStore(context).load().also { shared = it } }
}

/** KatDisplay is not thread-safe; the pipeline and the ticker share it through this lock. */
private class LockedDisplay(private val inner: Display, private val onShow: (TranslationResult) -> Unit) : Display {
    override var targetIsChinese: Boolean
        get() = synchronized(this) { inner.targetIsChinese }
        set(v) = synchronized(this) { inner.targetIsChinese = v }
    @Synchronized override fun thinking() = inner.thinking()
    @Synchronized override fun show(r: TranslationResult) {
        inner.show(r)
        onShow(r)
    }
    @Synchronized override fun error() = inner.error()
    @Synchronized override fun clear() = inner.clear()
    @Synchronized override fun stop() = inner.stop()
    @Synchronized override fun tick() = inner.tick()
}

/**
 * Listens and translates while the avatar's 翻訳 toggle is on, even with the screen off.
 * Everything heavy (copying and loading models) happens on its own thread.
 */
class TranslatorService : Service() {
    companion object {
        private const val TAG = "VrcTranslator"
        private const val CHANNEL = "translator"

        fun start(context: Context) = context.startForegroundService(Intent(context, TranslatorService::class.java))
        fun stop(context: Context) = context.stopService(Intent(context, TranslatorService::class.java))
    }

    @Volatile private var running = false
    private val threads = ArrayList<Thread>()
    private var wakeLock: PowerManager.WakeLock? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (running) return START_NOT_STICKY
        running = true
        startForeground(1, notification("翻訳: 準備しています"), ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE)
        wakeLock = (getSystemService(POWER_SERVICE) as PowerManager)
            .newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "vrctranslator:listen").apply { acquire() }
        TranslatorState.status.value = Status(running = true, models = "準備中")
        spawn("setup") { setup() }
        return START_NOT_STICKY
    }

    private fun spawn(name: String, body: () -> Unit) {
        threads += Thread({
            try {
                body()
            } catch (e: Throwable) {
                Log.e(TAG, "$name stopped", e)
                note("止まりました（$name）: ${e.javaClass.simpleName}")
            }
        }, name).apply { isDaemon = true; start() }
    }

    private fun note(text: String) {
        event(text)
        TranslatorState.status.update { it.copy(note = text) }
    }

    private val eventTime = java.time.format.DateTimeFormatter.ofPattern("HH:mm:ss.SSS")

    /** Diagnostics that survive logcat's small buffer: files/events.log (read with adb run-as). */
    @Synchronized
    private fun event(text: String) {
        Log.i(TAG, text)
        try {
            val f = File(filesDir, "events.log")
            if (f.length() > 1_000_000) f.renameTo(File(filesDir, "events.old.log"))
            f.appendText(LocalDateTime.now().format(eventTime) + "  " + text + "\n")
        } catch (e: Exception) {
            // diagnostics only
        }
    }

    private fun clock() = SystemClock.elapsedRealtimeNanos() / 1e9

    private fun setup() {
        val settings: TranslatorSettings = TranslatorState.settings(this)
        val files = ModelFiles(this)
        val missing = files.prepare { TranslatorState.status.update { s -> s.copy(models = it) } }
        if (missing.isNotEmpty()) {
            TranslatorState.status.update { it.copy(models = "モデルがありません: ${missing.joinToString()}") }
            return
        }
        TranslatorState.status.update { it.copy(models = "読み込んでいます") }
        val stt = SenseVoice(files.path("senseVoiceModel"), files.path("senseVoiceTokens"))
        val llm = NativeLlm(mapOf("1B" to files.path("1B"), "4B" to files.path("4B")))
        val kana = KuromojiKana().also { it.toKana("準備") }  // load the dictionary now, not on the first sentence
        TranslatorState.status.update { it.copy(models = "できた") }

        lateinit var pipeline: OwnVoicePipeline
        val relay = RelayLink({ settings.pcIp }, settings.relayPort, settings.listenPort, { a, args ->
            pipeline.onOsc(a, args)
            TranslatorState.status.update { it.copy(listening = pipeline.active, localAI = pipeline.useLocalAI) }
        })
        val display = LockedDisplay(KatDisplay(relay::send, settings.display, settings.katSync, settings.showOriginal, ::clock,
            onEvent = ::event)) { r ->
            TranslatorState.status.update { it.copy(ownOriginal = r.original, ownTranslation = r.translation) }
        }
        pipeline = OwnVoicePipeline(settings, stt, llm, display, kana,
            TranslationLog(File(filesDir, "vrc_translation_log.tsv")), ::clock, LocalDateTime::now) { msg ->
            event(msg)
            TranslatorState.status.update { it.copy(last = msg) }
        }
        pipeline.onStatus = ::note
        updateNotification("翻訳: 待っています（アバターの「翻訳」をオンに）")

        spawn("worker") { while (running) pipeline.processNext(300) }
        spawn("mic") { micLoop(pipeline) }
        try {
            while (running) {
                display.tick()
                relay.tick(clock())
                TranslatorState.status.update { it.copy(relayFound = relay.found) }
                Thread.sleep(50)
            }
        } finally {
            relay.close()
            llm.cancel()
        }
    }

    /** The microphone is open only while the toggle is on. */
    private fun micLoop(pipeline: OwnVoicePipeline) {
        while (running) {
            if (!pipeline.active) {
                Thread.sleep(50)
                continue
            }
            val mic = try {
                Mic()
            } catch (e: Exception) {
                note("マイクを開けません。2 秒後にもう一度")
                Thread.sleep(2000)
                continue
            }
            updateNotification("翻訳: 聞いています")
            mic.use {
                while (running && pipeline.active) pipeline.feedFrame(it.read() ?: break)
            }
            updateNotification("翻訳: 待っています")
        }
    }

    private fun notification(text: String): Notification {
        val nm = getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(NotificationChannel(CHANNEL, "翻訳", NotificationManager.IMPORTANCE_LOW))
        return Notification.Builder(this, CHANNEL)
            .setContentTitle("VRC Translator")
            .setContentText(text)
            .setSmallIcon(android.R.drawable.ic_btn_speak_now)
            .setOngoing(true)
            .build()
    }

    private fun updateNotification(text: String) =
        getSystemService(NotificationManager::class.java).notify(1, notification(text))

    override fun onDestroy() {
        running = false
        threads.forEach { it.join(1500) }
        threads.clear()
        wakeLock?.let { if (it.isHeld) it.release() }
        TranslatorState.status.value = Status()
        super.onDestroy()
    }
}
