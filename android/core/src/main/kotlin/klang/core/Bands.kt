package klang.core

import kotlin.math.round

/** De ti faste frekvensbåndene (oktavavstand) og grensene for forsterkning. Samme verdier som Windows-versjonen. */
object Bands {
    const val COUNT = 10
    const val MIN_GAIN_DB = -12.0
    const val MAX_GAIN_DB = 12.0
    const val GAIN_STEP_DB = 0.5

    /** Q for oktavbredde. Gir jevne overganger mellom nabobånd. */
    const val Q = 1.41

    val frequencies: List<Double> = listOf(31.0, 62.0, 125.0, 250.0, 500.0, 1000.0, 2000.0, 4000.0, 8000.0, 16000.0)
    val labels: List<String> = listOf("31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k")

    /** Volumvern: hvor mye hele signalet senkes, valgt med en skyvebryter. */
    const val PREAMP_MIN_DB = -10.0
    const val PREAMP_MAX_DB = -3.0
    const val DEFAULT_PREAMP_DB = -6.0

    fun flat(): List<Double> = List(COUNT) { 0.0 }

    /** Klemmer volumvernet til −10…−3 dB og runder til nærmeste halve dB. */
    fun clampPreamp(db: Double): Double {
        if (db.isNaN() || db == 0.0) return DEFAULT_PREAMP_DB
        val c = db.coerceIn(PREAMP_MIN_DB, PREAMP_MAX_DB)
        return round(c / GAIN_STEP_DB) * GAIN_STEP_DB
    }

    /** Klemmer til lovlig område og runder til nærmeste halve dB. */
    fun clamp(gainDb: Double): Double {
        if (gainDb.isNaN()) return 0.0
        val c = gainDb.coerceIn(MIN_GAIN_DB, MAX_GAIN_DB)
        return round(c / GAIN_STEP_DB) * GAIN_STEP_DB
    }

    fun normalize(gains: List<Double>?): List<Double> =
        List(COUNT) { i -> if (gains != null && i < gains.size) clamp(gains[i]) else 0.0 }
}
