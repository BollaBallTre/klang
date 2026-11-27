package io.github.bollaballtre.klang

import android.media.audiofx.DynamicsProcessing
import android.util.Log
import klang.core.AppEqState
import klang.core.DeviceEq
import klang.core.KlangSettings
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

enum class EffectStatus {
    /** Klang er stoppet. */
    Off,
    /** Valgt app spiller ikke lyd (eller har ikke sagt fra ennå). */
    Waiting,
    /** EQ-en er koblet på lyden. */
    Active,
    /** En annen lydapp styrer lyden nå. */
    NoControl,
    /** Telefonen tillot ikke lydeffekten. */
    Failed,
}

/**
 * Legger Androids lydeffekt DynamicsProcessing på lyden: enten på lydøktene til appen som er valgt,
 * eller på økt 0 (all lyd). Bare den valgte appen får EQ, som i Windows-versjonen.
 */
class EffectManager(private val onControlChanged: () -> Unit) {
    private val effects = mutableMapOf<Int, DynamicsProcessing>()
    private val applied = mutableMapOf<Int, AppEqState>()
    private val failed = mutableSetOf<Int>()
    private val noControl = mutableSetOf<Int>()

    private val _status = MutableStateFlow(EffectStatus.Off)
    val status: StateFlow<EffectStatus> = _status.asStateFlow()

    fun apply(settings: KlangSettings, sessions: Map<Int, String>, running: Boolean, androidAuto: Boolean) {
        val target = settings.selected
        val state = settings.stateFor(target)
        val wanted: Set<Int> = when {
            !running -> emptySet()
            // Med Android Auto går lyden utenom økt 0, så «All lyd» legges også på hver app som har sagt fra.
            target == KlangSettings.ALL_AUDIO && androidAuto -> sessions.keys + GLOBAL_SESSION
            target == KlangSettings.ALL_AUDIO -> setOf(GLOBAL_SESSION)
            else -> sessions.filterValues { it == target }.keys
        }

        (effects.keys - wanted).forEach(::release)
        failed.retainAll(wanted)
        noControl.retainAll(wanted)

        for (id in wanted) {
            if (id in failed) continue
            val dp = effects[id] ?: create(id) ?: continue
            configure(id, dp, state)
        }

        _status.value = when {
            !running -> EffectStatus.Off
            wanted.isEmpty() -> EffectStatus.Waiting
            wanted.all { it in failed } -> EffectStatus.Failed
            target == KlangSettings.ALL_AUDIO && androidAuto && wanted.none { it != GLOBAL_SESSION && it !in failed } -> EffectStatus.Waiting
            wanted.any { it in noControl } -> EffectStatus.NoControl
            else -> EffectStatus.Active
        }
    }

    private fun create(id: Int): DynamicsProcessing? = try {
        val eq = DynamicsProcessing.Eq(true, true, DeviceEq.BAND_COUNT)
        for (k in 0 until DeviceEq.BAND_COUNT) eq.setBand(k, DynamicsProcessing.EqBand(true, DeviceEq.cutoffs[k], 0f))
        val config = DynamicsProcessing.Config.Builder(
            DynamicsProcessing.VARIANT_FAVOR_FREQUENCY_RESOLUTION,
            CHANNELS,
            true, DeviceEq.BAND_COUNT, // EQ før kompressor
            false, 0, // ingen kompressor
            false, 0, // ingen EQ etter
            false, // ingen begrenser
        ).setPreEqAllChannelsTo(eq).build()
        DynamicsProcessing(PRIORITY, id, config).also { dp ->
            dp.setControlStatusListener { _, _ -> onControlChanged() }
            effects[id] = dp
            Log.i(TAG, "Koblet på lydøkt $id")
        }
    } catch (e: Exception) {
        Log.w(TAG, "Fikk ikke lagt EQ på lydøkt $id", e)
        failed += id
        null
    }

    private fun configure(id: Int, dp: DynamicsProcessing, state: AppEqState) {
        val hasControl = runCatching { dp.hasControl() }.getOrDefault(false)
        if (!hasControl) {
            noControl += id
            applied.remove(id)
            return
        }
        noControl -= id
        if (applied[id] == state) return
        try {
            val gains = DeviceEq.bandGains(state.gains)
            for (k in gains.indices)
                dp.setPreEqBandAllChannelsTo(k, DynamicsProcessing.EqBand(true, DeviceEq.cutoffs[k], gains[k]))
            dp.setInputGainAllChannelsTo(state.preampDb.toFloat())
            dp.enabled = state.enabled
            applied[id] = state
        } catch (e: Exception) {
            Log.w(TAG, "Klarte ikke å endre EQ på lydøkt $id", e)
            noControl += id
            applied.remove(id)
        }
    }

    private fun release(id: Int) {
        effects.remove(id)?.let { runCatching { it.enabled = false; it.release() } }
        applied.remove(id)
        Log.i(TAG, "Koblet fra lydøkt $id")
    }

    companion object {
        private const val TAG = "Klang"
        private const val GLOBAL_SESSION = 0
        private const val CHANNELS = 2
        private const val PRIORITY = 1000
    }
}
