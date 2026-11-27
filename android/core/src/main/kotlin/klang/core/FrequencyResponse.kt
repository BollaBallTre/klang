package klang.core

import kotlin.math.max
import kotlin.math.pow

/** Regner ut den samlede kurven for ti bånd. Brukes både til å tegne kurven og til lydeffekten på Android. */
object FrequencyResponse {
    const val REFERENCE_SAMPLE_RATE = 48000.0

    fun at(gains: List<Double>, frequency: Double, sampleRate: Double = REFERENCE_SAMPLE_RATE): Double {
        var db = 0.0
        for (i in 0 until Bands.COUNT)
            db += BiquadCoefficients.peaking(sampleRate, Bands.frequencies[i], gains[i], Bands.Q).magnitudeDb(sampleRate, frequency)
        return db
    }

    /** Punkter logaritmisk fordelt fra 20 Hz til 20 kHz. */
    fun curve(gains: List<Double>, points: Int = 200): List<Pair<Double, Double>> =
        (0..points).map { i ->
            val f = 20 * 1000.0.pow(i.toDouble() / points)
            f to at(gains, f)
        }

    fun peak(gains: List<Double>): Double = curve(gains).fold(Double.NEGATIVE_INFINITY) { acc, p -> max(acc, p.second) }

    /** True når den høyeste toppen pluss volumvernet går over 0 dB, så lyden kan forvrenges. */
    fun mayClip(gains: List<Double>, preampDb: Double): Boolean = peak(gains) + preampDb > 0.05
}
