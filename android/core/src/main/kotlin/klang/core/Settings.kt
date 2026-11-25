package klang.core

import kotlinx.serialization.SerializationException
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import java.io.File
import java.io.IOException

/** Det som lagres i settings.json. */
@Serializable
data class KlangSettings(
    val version: Int = 1,
    val customPresets: List<CustomPreset> = emptyList(),
    /** Siste valg per app, nøkkel er pakkenavnet (f.eks. «org.videolan.vlc») eller [ALL_AUDIO]. */
    val apps: Map<String, AppEqState> = emptyMap(),
    /** Appen som får EQ nå. */
    val selected: String = ALL_AUDIO,
) {
    fun stateFor(key: String): AppEqState = apps[key] ?: AppEqState()
    fun withState(key: String, state: AppEqState): KlangSettings = copy(apps = apps + (key to state))

    companion object {
        /** Nøkkel for «All lyd», som virker på alle apper samtidig. */
        const val ALL_AUDIO = "*"
    }
}

@Serializable
data class CustomPreset(val name: String, val gains: List<Double> = Bands.flat())

@Serializable
data class AppEqState(
    /** Navnet på innstillingen som sist ble valgt (ferdig, egen eller «Flat»). Tom betyr egen justering. */
    val presetName: String = Presets.FLAT_NAME,
    /** Bryterverdiene, som kan være endret etter at innstillingen ble valgt. */
    val gains: List<Double> = Bands.flat(),
    val enabled: Boolean = true,
    /** Volumvern i dB, −10 til −3. */
    val preampDb: Double = Bands.DEFAULT_PREAMP_DB,
)

enum class SaveNameProblem { None, Empty, TooLong, ReservedName }

/** Leser og skriver innstillinger som JSON. Skriver først til en midlertidig fil så en krasj ikke ødelegger filen. */
class SettingsStore(val file: File) {

    fun load(): KlangSettings = try {
        if (!file.exists()) KlangSettings() else sanitize(json.decodeFromString(KlangSettings.serializer(), file.readText()))
    } catch (e: Exception) {
        if (e !is SerializationException && e !is IOException && e !is IllegalArgumentException) throw e
        // Ødelagt fil: ta vare på den og start på nytt.
        runCatching { file.copyTo(File(file.path + ".broken"), overwrite = true) }
        KlangSettings()
    }

    fun save(settings: KlangSettings) {
        file.parentFile?.mkdirs()
        val tmp = File(file.path + ".tmp")
        tmp.writeText(json.encodeToString(KlangSettings.serializer(), settings))
        if (!tmp.renameTo(file)) {
            file.delete()
            if (!tmp.renameTo(file)) throw IOException("Klarte ikke å lagre ${file.path}")
        }
    }

    companion object {
        const val MAX_NAME_LENGTH = 32

        private val json = Json { prettyPrint = true; ignoreUnknownKeys = true; encodeDefaults = true }

        fun validateName(name: String?): SaveNameProblem {
            val n = name?.trim() ?: ""
            return when {
                n.isEmpty() -> SaveNameProblem.Empty
                n.length > MAX_NAME_LENGTH -> SaveNameProblem.TooLong
                Presets.isReservedName(n) -> SaveNameProblem.ReservedName
                else -> SaveNameProblem.None
            }
        }

        fun hasCustom(s: KlangSettings, name: String): Boolean = s.customPresets.any { it.name.equals(name.trim(), ignoreCase = true) }

        /** Lagrer eller overskriver en egen innstilling med samme navn (store/små bokstaver likegyldig). */
        fun upsert(s: KlangSettings, name: String, gains: List<Double>): Pair<KlangSettings, CustomPreset> {
            require(validateName(name) == SaveNameProblem.None) { "Ugyldig navn" }
            val n = name.trim()
            val existing = s.customPresets.firstOrNull { it.name.equals(n, ignoreCase = true) }
            val saved = CustomPreset(existing?.name ?: n, Bands.normalize(gains))
            val list = if (existing == null) s.customPresets + saved
            else s.customPresets.map { if (it === existing) saved else it }
            return s.copy(customPresets = list) to saved
        }

        fun delete(s: KlangSettings, name: String): KlangSettings {
            if (!hasCustom(s, name)) return s
            // Apper som brukte den slettede innstillingen beholder bryterne, men står ikke lenger på et navn.
            return s.copy(
                customPresets = s.customPresets.filterNot { it.name.equals(name, ignoreCase = true) },
                apps = s.apps.mapValues { (_, v) -> if (v.presetName.equals(name, ignoreCase = true)) v.copy(presetName = "") else v },
            )
        }

        /** Finner bryterverdiene til en innstilling, enten ferdig, egen eller Flat. */
        fun gainsFor(s: KlangSettings, name: String): List<Double>? =
            Presets.findBuiltIn(name)?.gains
                ?: s.customPresets.firstOrNull { it.name.equals(name, ignoreCase = true) }?.gains

        fun sanitize(s: KlangSettings): KlangSettings = s.copy(
            customPresets = s.customPresets
                .filter { validateName(it.name) == SaveNameProblem.None }
                .groupBy { it.name.trim().lowercase() }
                .map { (_, g) -> CustomPreset(g.first().name.trim(), Bands.normalize(g.last().gains)) },
            apps = s.apps.filterKeys { it.isNotBlank() }.mapValues { (_, v) ->
                v.copy(gains = Bands.normalize(v.gains), preampDb = Bands.clampPreamp(v.preampDb))
            },
            selected = s.selected.ifBlank { KlangSettings.ALL_AUDIO },
        )
    }
}
