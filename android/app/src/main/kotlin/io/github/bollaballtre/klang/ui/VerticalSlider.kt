package io.github.bollaballtre.klang.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.semantics.ProgressBarRangeInfo
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.disabled
import androidx.compose.ui.semantics.progressBarRangeInfo
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.setProgress
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import klang.core.Bands

/** Loddrett skyvebryter fra −12 til +12 dB. Trykk eller dra for å endre. */
@Composable
fun VerticalSlider(
    value: Double,
    onChange: (Double) -> Unit,
    description: String,
    enabled: Boolean,
    modifier: Modifier = Modifier,
    height: Dp = 200.dp,
) {
    val colors = MaterialTheme.colorScheme
    val active = if (enabled) colors.primary else colors.onSurface.copy(alpha = 0.38f)
    val track = colors.surfaceContainerHighest
    val zeroTick = colors.outline
    var heightPx by remember { mutableIntStateOf(1) }
    val current by rememberUpdatedState(value)
    val change by rememberUpdatedState(onChange)
    val density = androidx.compose.ui.platform.LocalDensity.current
    val padPx = with(density) { 14.dp.toPx() } // plass til knotten øverst og nederst

    fun valueAt(y: Float): Double {
        val usable = (heightPx - 2 * padPx).coerceAtLeast(1f)
        val frac = ((y - padPx) / usable).coerceIn(0f, 1f)
        return Bands.clamp(Bands.MAX_GAIN_DB - frac * (Bands.MAX_GAIN_DB - Bands.MIN_GAIN_DB))
    }

    Canvas(
        modifier
            .fillMaxWidth()
            .height(height)
            .onSizeChanged { heightPx = it.height }
            .semantics {
                contentDescription = description
                stateDescription = formatDb(current)
                progressBarRangeInfo = ProgressBarRangeInfo(
                    current.toFloat(), Bands.MIN_GAIN_DB.toFloat()..Bands.MAX_GAIN_DB.toFloat(), 47,
                )
                if (enabled) setProgress { v -> change(Bands.clamp(v.toDouble())); true } else disabled()
            }
            .pointerInput(enabled) {
                if (!enabled) return@pointerInput
                detectTapGestures { change(valueAt(it.y)) }
            }
            .pointerInput(enabled) {
                if (!enabled) return@pointerInput
                detectVerticalDragGestures { c, _ -> c.consume(); change(valueAt(c.position.y)) }
            }
    ) {
        val pad = 14.dp.toPx()
        val x = size.width / 2
        val top = pad
        val bottom = size.height - pad
        fun yOf(db: Double) = (top + (Bands.MAX_GAIN_DB - db) / (Bands.MAX_GAIN_DB - Bands.MIN_GAIN_DB) * (bottom - top)).toFloat()
        val w = 4.dp.toPx()
        drawLine(track, Offset(x, top), Offset(x, bottom), strokeWidth = w, cap = StrokeCap.Round)
        val y0 = yOf(0.0)
        drawLine(zeroTick, Offset(x - 9.dp.toPx(), y0), Offset(x + 9.dp.toPx(), y0), strokeWidth = 1.dp.toPx())
        val yv = yOf(value)
        drawLine(active, Offset(x, y0), Offset(x, yv), strokeWidth = w, cap = StrokeCap.Round)
        drawCircle(active, radius = 9.dp.toPx(), center = Offset(x, yv))
        drawCircle(colors.surface, radius = 3.5.dp.toPx(), center = Offset(x, yv))
    }
}
