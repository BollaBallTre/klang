package io.github.bollaballtre.klang.ui

import android.content.Context
import android.content.pm.PackageManager
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.core.graphics.drawable.toBitmap
import klang.core.KlangSettings

data class AppChoice(val key: String, val label: String, val icon: ImageBitmap?, val playing: Boolean) {
    val isAllAudio: Boolean get() = key == KlangSettings.ALL_AUDIO
}

/** Henter navn og ikon for apper. Huskes så vi ikke spør telefonen hver gang skjermen tegnes. */
class AppInfoCache(private val context: Context) {
    private val cache = mutableMapOf<String, Pair<String, ImageBitmap?>>()

    fun info(pkg: String): Pair<String, ImageBitmap?> = cache.getOrPut(pkg) {
        val pm = context.packageManager
        try {
            val ai = pm.getApplicationInfo(pkg, 0)
            val icon = runCatching { pm.getApplicationIcon(ai).toBitmap(96, 96).asImageBitmap() }.getOrNull()
            pm.getApplicationLabel(ai).toString() to icon
        } catch (e: PackageManager.NameNotFoundException) {
            pkg to null
        }
    }

    fun choices(settings: KlangSettings, sessions: Map<Int, String>): List<AppChoice> {
        val playing = sessions.values.toSet()
        val keys = (settings.apps.keys + playing + settings.selected) - KlangSettings.ALL_AUDIO - context.packageName
        val apps = keys.map { k -> info(k).let { (label, icon) -> AppChoice(k, label, icon, k in playing) } }
            .sortedWith(compareByDescending<AppChoice> { it.playing }.thenBy { it.label.lowercase() })
        return listOf(AppChoice(KlangSettings.ALL_AUDIO, "All lyd", null, false)) + apps
    }
}
