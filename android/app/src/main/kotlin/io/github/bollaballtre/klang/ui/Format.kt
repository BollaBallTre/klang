package io.github.bollaballtre.klang.ui

import kotlin.math.abs

/** «+6 dB», «−3,5 dB», «0 dB». */
fun formatDb(v: Double, unit: Boolean = true): String {
    val a = abs(v)
    val num = if (a % 1.0 == 0.0) "${a.toInt()}" else "${a.toInt()},${((a % 1.0) * 10).toInt()}"
    val sign = if (v > 0) "+" else if (v < 0) "−" else ""
    return sign + num + if (unit) " dB" else ""
}
