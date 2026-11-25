package klang.core

import kotlin.math.pow

/**
 * Oversetter de ti båndene til Androids lydeffekt (DynamicsProcessing), som deler lyden i
 * mange smale bånd med fast forsterkning. Vi bruker 31 tredjedels-oktaver og gir hvert av dem
 * forsterkningen kurven har midt i båndet, så lyden blir som på Windows.
 */
object DeviceEq {
    const val BAND_COUNT = 31

    /** Midtfrekvens for hvert smale bånd: 20 Hz, 25 Hz, 32 Hz … ca. 20 kHz. */
    val centers: List<Double> = List(BAND_COUNT) { k -> 20.0 * 2.0.pow(k / 3.0) }

    /** Øvre grense for hvert bånd (DynamicsProcessing bruker øvre grensefrekvens). */
    val cutoffs: List<Float> = List(BAND_COUNT) { k ->
        if (k == BAND_COUNT - 1) 20000f else (centers[k] * 2.0.pow(1.0 / 6)).toFloat()
    }

    fun bandGains(gains: List<Double>): List<Float> = centers.map { FrequencyResponse.at(gains, it).toFloat() }
}
