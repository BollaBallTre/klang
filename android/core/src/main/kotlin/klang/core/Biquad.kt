package klang.core

import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.log10
import kotlin.math.pow
import kotlin.math.sin

/** Koeffisienter for et andreordens filter (RBJ «Audio EQ Cookbook»), normalisert så a0 = 1. */
data class BiquadCoefficients(val b0: Double, val b1: Double, val b2: Double, val a1: Double, val a2: Double) {
    val isIdentity: Boolean get() = b0 == 1.0 && b1 == 0.0 && b2 == 0.0 && a1 == 0.0 && a2 == 0.0

    /** Forsterkning i dB ved [frequency]. */
    fun magnitudeDb(sampleRate: Double, frequency: Double): Double {
        if (isIdentity) return 0.0
        val w = 2 * PI * frequency / sampleRate
        val c1 = cos(w); val s1 = sin(w); val c2 = cos(2 * w); val s2 = sin(2 * w)
        val nr = b0 + b1 * c1 + b2 * c2; val ni = -(b1 * s1 + b2 * s2)
        val dr = 1 + a1 * c1 + a2 * c2; val di = -(a1 * s1 + a2 * s2)
        return 10 * log10((nr * nr + ni * ni) / (dr * dr + di * di))
    }

    companion object {
        val Identity = BiquadCoefficients(1.0, 0.0, 0.0, 0.0, 0.0)

        /** Peaking-filter: løfter eller senker rundt [frequency]. */
        fun peaking(sampleRate: Double, frequency: Double, gainDb: Double, q: Double): BiquadCoefficients {
            if (gainDb == 0.0 || frequency <= 0 || frequency >= sampleRate / 2) return Identity
            val a = 10.0.pow(gainDb / 40)
            val w0 = 2 * PI * frequency / sampleRate
            val alpha = sin(w0) / (2 * q)
            val cs = cos(w0)
            val a0 = 1 + alpha / a
            return BiquadCoefficients(
                (1 + alpha * a) / a0,
                (-2 * cs) / a0,
                (1 - alpha * a) / a0,
                (-2 * cs) / a0,
                (1 - alpha / a) / a0,
            )
        }
    }
}
