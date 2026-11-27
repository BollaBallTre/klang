package io.github.bollaballtre.klang.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

// Fargene er hentet fra Klang-ikonet: mørk marineblå med turkis, grønn og fiolett.
private val Dark = darkColorScheme(
    primary = Color(0xFF4FD8F0),
    onPrimary = Color(0xFF00333D),
    primaryContainer = Color(0xFF0E4A5A),
    onPrimaryContainer = Color(0xFFB8F1FF),
    secondary = Color(0xFF6EE7B7),
    tertiary = Color(0xFF8B92F7),
    background = Color(0xFF0B1026),
    onBackground = Color(0xFFE3E6F2),
    surface = Color(0xFF0B1026),
    onSurface = Color(0xFFE3E6F2),
    surfaceVariant = Color(0xFF242B45),
    onSurfaceVariant = Color(0xFFB9BED3),
    surfaceContainer = Color(0xFF151B35),
    surfaceContainerHigh = Color(0xFF1C2240),
    surfaceContainerHighest = Color(0xFF242B4B),
    outline = Color(0xFF4A5170),
    outlineVariant = Color(0xFF2E3552),
    error = Color(0xFFFFB4AB),
)

private val Light = lightColorScheme(
    primary = Color(0xFF00677D),
    onPrimary = Color.White,
    primaryContainer = Color(0xFFB4EBFF),
    onPrimaryContainer = Color(0xFF001F27),
    secondary = Color(0xFF006C4F),
    tertiary = Color(0xFF4F55C2),
    background = Color(0xFFF7F9FD),
    surface = Color(0xFFF7F9FD),
    surfaceContainer = Color(0xFFEBEEF5),
    surfaceContainerHigh = Color(0xFFE4E8F0),
    surfaceContainerHighest = Color(0xFFDEE2EB),
)

@Composable
fun KlangTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = if (isSystemInDarkTheme()) Dark else Light, content = content)
}
