package io.github.bollaballtre.klang.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import klang.core.FrequencyResponse
import kotlin.math.log10

/** Frekvenskurven: hvordan lyden endres fra 20 Hz (dyp bass) til 20 kHz (lys diskant). */
@Composable
fun CurveView(gains: List<Double>, enabled: Boolean, modifier: Modifier = Modifier) {
    val colors = MaterialTheme.colorScheme
    val line = if (enabled) colors.primary else colors.onSurface.copy(alpha = 0.4f)
    val grid = colors.outlineVariant
    val zero = colors.outline
    val labelColor = colors.onSurfaceVariant
    val measurer = rememberTextMeasurer()
    val points = remember(gains) { FrequencyResponse.curve(gains, 120) }
    val labelStyle = TextStyle(fontSize = 10.sp, color = labelColor)

    Canvas(
        modifier.fillMaxWidth().height(150.dp).semantics { contentDescription = "Frekvenskurve" }
    ) {
        val padL = 26.dp.toPx(); val padB = 16.dp.toPx(); val top = 6.dp.toPx(); val right = 4.dp.toPx()
        val range = 15.0
        fun fx(f: Double) = (padL + log10(f / 20) / log10(1000.0) * (size.width - padL - right)).toFloat()
        fun fy(db: Double) = (top + (range - db.coerceIn(-range, range)) / (2 * range) * (size.height - padB - top)).toFloat()

        for (db in listOf(-12, -6, 0, 6, 12)) {
            drawLine(if (db == 0) zero else grid, Offset(padL, fy(db.toDouble())), Offset(size.width, fy(db.toDouble())), 1.dp.toPx())
            val t = measurer.measure(if (db > 0) "+$db" else if (db < 0) "−${-db}" else "0", labelStyle)
            drawText(t, topLeft = Offset(padL - t.size.width - 4.dp.toPx(), fy(db.toDouble()) - t.size.height / 2f))
        }
        for ((f, text) in listOf(100.0 to "100 Hz", 1000.0 to "1 kHz", 10000.0 to "10 kHz")) {
            drawLine(grid, Offset(fx(f), top), Offset(fx(f), size.height - padB), 1.dp.toPx())
            val t = measurer.measure(text, labelStyle)
            drawText(t, topLeft = Offset(fx(f) - t.size.width / 2f, size.height - t.size.height))
        }

        val path = Path()
        points.forEachIndexed { i, (f, db) -> if (i == 0) path.moveTo(fx(f), fy(db)) else path.lineTo(fx(f), fy(db)) }
        val fill = Path().apply {
            addPath(path)
            lineTo(fx(points.last().first), fy(0.0))
            lineTo(fx(points.first().first), fy(0.0))
            close()
        }
        drawPath(fill, Brush.verticalGradient(listOf(line.copy(alpha = 0.28f), line.copy(alpha = 0.04f))))
        drawPath(path, line, style = Stroke(width = 2.5.dp.toPx()))
    }
}
