package com.haru452.vrctranslator.app

import android.Manifest
import android.content.pm.PackageManager
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.haru452.vrctranslator.core.DisplayMode
import com.haru452.vrctranslator.core.Lang

class MainActivity : ComponentActivity() {
    private val askPermissions = registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { granted ->
        if (granted[Manifest.permission.RECORD_AUDIO] == true) TranslatorService.start(this)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            MaterialTheme(colorScheme = darkColorScheme()) {
                Surface(Modifier.fillMaxSize()) { Screen() }
            }
        }
    }

    private fun startTranslator() {
        val needed = listOf(Manifest.permission.RECORD_AUDIO, Manifest.permission.POST_NOTIFICATIONS)
            .filter { checkSelfPermission(it) != PackageManager.PERMISSION_GRANTED }
        if (needed.isEmpty()) TranslatorService.start(this) else askPermissions.launch(needed.toTypedArray())
    }

    @Composable
    private fun Screen() {
        val status by TranslatorState.status.collectAsState()
        val settings = TranslatorState.settings(this)
        val store = remember { SettingsStore(this) }
        var target by remember { mutableStateOf(settings.targetLang) }
        var pcIp by remember { mutableStateOf(settings.pcIp) }
        var showOriginal by remember { mutableStateOf(settings.showOriginal) }
        var display by remember { mutableStateOf(settings.display) }
        var katSync by remember { mutableStateOf(settings.katSync.toString()) }

        Column(Modifier.padding(20.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text("VRC Translator", style = MaterialTheme.typography.headlineSmall)
            StatusLine("中継", if (status.relayFound) "つながった" else "見つからない")
            StatusLine("自分の声", if (status.listening) "聞いています" else "止まっています")
            StatusLine("AI", if (status.localAI) "スマホの中（ローカル）" else "ローカル（Gemini はまだありません）")
            StatusLine("モデル", status.models.ifEmpty { "-" })
            if (status.last.isNotEmpty()) Text(status.last, style = MaterialTheme.typography.bodyLarge)
            if (status.note.isNotEmpty()) Text(status.note, color = MaterialTheme.colorScheme.error)

            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                Text("相手:")
                for ((lang, label) in listOf(Lang.EN to "英語", Lang.ZH to "中国語")) {
                    FilterChip(selected = target == lang, label = { Text(label) }, onClick = {
                        target = lang
                        settings.targetLang = lang
                        store.save(settings)
                    })
                }
            }
            Button(onClick = { if (status.running) TranslatorService.stop(this@MainActivity) else startTranslator() },
                modifier = Modifier.fillMaxWidth()) {
                Text(if (status.running) "停止" else "開始")
            }

            HorizontalDivider()
            Text("設定（開始し直すと反映されます）", style = MaterialTheme.typography.titleMedium)
            OutlinedTextField(pcIp, { pcIp = it; settings.pcIp = it.trim(); store.save(settings) },
                label = { Text("PC の IP アドレス（中継の画面に出ます）") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri), singleLine = true, modifier = Modifier.fillMaxWidth())
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("原文も出す", Modifier.weight(1f))
                Switch(showOriginal, { showOriginal = it; settings.showOriginal = it; store.save(settings) })
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                Text("出し先:")
                for ((mode, label) in listOf(DisplayMode.KAT to "ペットの上", DisplayMode.CHATBOX to "チャット", DisplayMode.BOTH to "両方")) {
                    FilterChip(selected = display == mode, label = { Text(label) }, onClick = {
                        display = mode
                        settings.display = mode
                        store.save(settings)
                    })
                }
            }
            OutlinedTextField(katSync, { v ->
                katSync = v
                v.toIntOrNull()?.takeIf { it in listOf(4, 8, 16) }?.let { settings.katSync = it; store.save(settings) }
            }, label = { Text("KAT の同期設定（4 / 8 / 16）") }, singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number))
            Text("中国語の訳はチャットに出ます（ペットの上には漢字を出せません）。", style = MaterialTheme.typography.bodySmall)
            Spacer(Modifier.size(24.dp))
        }
    }

    @Composable
    private fun StatusLine(label: String, value: String) {
        Row {
            Text("$label: ", style = MaterialTheme.typography.bodyMedium)
            Text(value, style = MaterialTheme.typography.bodyMedium)
        }
    }
}
