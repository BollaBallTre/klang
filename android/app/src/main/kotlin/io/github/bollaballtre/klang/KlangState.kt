package io.github.bollaballtre.klang

import android.content.Context
import android.util.Log
import androidx.car.app.connection.CarConnection
import klang.core.AppEqState
import klang.core.KlangSettings
import klang.core.SettingsStore
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.FlowPreview
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.debounce
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

/**
 * Alt appen vet: innstillingene (lagres i settings.json), hvilke apper som spiller lyd nå
 * (lydøkter meldt fra musikkappene), og om Klang er i gang. Holder lydeffektene oppdatert.
 */
private data class Inputs(val settings: KlangSettings, val sessions: Map<Int, String>, val running: Boolean, val androidAuto: Boolean)

@OptIn(FlowPreview::class)
class KlangState(context: Context) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val store = SettingsStore(File(context.filesDir, "settings.json"))

    private val _settings = MutableStateFlow(store.load())
    val settings: StateFlow<KlangSettings> = _settings.asStateFlow()

    /** Lydøkter som er åpne nå: øktnummer → pakkenavn. */
    private val _sessions = MutableStateFlow<Map<Int, String>>(emptyMap())
    val sessions: StateFlow<Map<Int, String>> = _sessions.asStateFlow()

    /** True når telefonen er koblet til bilen med Android Auto. */
    private val _androidAuto = MutableStateFlow(false)
    val androidAuto: StateFlow<Boolean> = _androidAuto.asStateFlow()

    private val running = MutableStateFlow(true)
    private val controlChanged = MutableStateFlow(0)

    private val effects = EffectManager { scope.launch { controlChanged.update { it + 1 } } }
    val status: StateFlow<EffectStatus> = effects.status

    init {
        scope.launch {
            combine(_settings, _sessions, running, _androidAuto, controlChanged) { s, ss, r, car, _ -> Inputs(s, ss, r, car) }
                .collect { effects.apply(it.settings, it.sessions, it.running, it.androidAuto) }
        }
        // Android Auto sender lyden til bilen utenom vanlig avspilling. Da må EQ-en legges på hver app.
        try {
            CarConnection(context).type.observeForever { type ->
                _androidAuto.value = type == CarConnection.CONNECTION_TYPE_PROJECTION
                Log.i("Klang", "Bilforbindelse: $type")
            }
        } catch (e: Exception) {
            Log.w("Klang", "Fant ikke ut om Android Auto er tilkoblet", e)
        }
        scope.launch {
            _settings.drop(1).debounce(400).collect { s ->
                withContext(Dispatchers.IO) { runCatching { store.save(s) } }
            }
        }
    }

    fun openSession(sessionId: Int, packageName: String) {
        if (sessionId <= 0 || packageName.isBlank()) return
        _sessions.update { it + (sessionId to packageName) }
        // Husk appen så den står i listen også senere.
        _settings.update { s -> if (s.apps.containsKey(packageName)) s else s.withState(packageName, AppEqState()) }
    }

    fun closeSession(sessionId: Int) = _sessions.update { it - sessionId }

    fun select(key: String) = _settings.update { it.copy(selected = key) }

    fun update(change: (KlangSettings) -> KlangSettings) = _settings.update(change)

    /** Endrer innstillingen for appen som er valgt nå. */
    fun updateCurrent(change: (AppEqState) -> AppEqState) =
        _settings.update { s -> s.withState(s.selected, change(s.stateFor(s.selected))) }

    fun setRunning(on: Boolean) {
        running.value = on
        if (!on) scope.launch(Dispatchers.IO) { runCatching { store.save(_settings.value) } }
    }
}
