@file:OptIn(ExperimentalMaterial3Api::class)

package io.github.bollaballtre.klang.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Star
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.MenuAnchorType
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Slider
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import io.github.bollaballtre.klang.EffectStatus
import io.github.bollaballtre.klang.KlangState
import klang.core.AppEqState
import klang.core.Bands
import klang.core.FrequencyResponse
import klang.core.KlangSettings
import klang.core.Presets
import klang.core.SaveNameProblem
import klang.core.SettingsStore
import kotlin.math.roundToInt

@Composable
fun KlangScreen(state: KlangState) {
    val settings by state.settings.collectAsStateWithLifecycle()
    val sessions by state.sessions.collectAsStateWithLifecycle()
    val status by state.status.collectAsStateWithLifecycle()
    val androidAuto by state.androidAuto.collectAsStateWithLifecycle()
    val context = LocalContext.current
    val cache = remember { AppInfoCache(context) }
    val choices = cache.choices(settings, sessions)
    val selected = choices.firstOrNull { it.key == settings.selected } ?: choices.first()
    val cur = settings.stateFor(selected.key)

    var showSave by remember { mutableStateOf(false) }
    var showDelete by remember { mutableStateOf(false) }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Klang", fontWeight = FontWeight.SemiBold) },
                actions = {
                    Text(if (cur.enabled) "EQ på" else "EQ av", style = MaterialTheme.typography.labelLarge)
                    Spacer(Modifier.width(8.dp))
                    Switch(
                        checked = cur.enabled,
                        onCheckedChange = { on -> state.updateCurrent { it.copy(enabled = on) } },
                        modifier = Modifier.padding(end = 12.dp).semantics { contentDescription = "EQ av eller på" },
                    )
                },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.background),
            )
        },
    ) { pad ->
        Column(
            Modifier
                .fillMaxSize()
                .padding(pad)
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 16.dp, vertical = 8.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            AppPicker(choices, selected) { state.select(it.key) }
            StatusLine(selected, cur, status, androidAuto, sessions.isNotEmpty())
            PresetSection(
                settings = settings,
                cur = cur,
                onPick = { name -> SettingsStore.gainsFor(settings, name)?.let { g -> state.updateCurrent { it.copy(presetName = name, gains = g) } } },
                onSave = { showSave = true },
                onDelete = { showDelete = true },
                onFlat = { state.updateCurrent { it.copy(presetName = Presets.FLAT_NAME, gains = Bands.flat()) } },
            )
            Section {
                CurveView(cur.gains, cur.enabled)
                Spacer(Modifier.size(8.dp))
                BandSliders(cur.gains, true) { band, v ->
                    state.updateCurrent { s -> s.copy(gains = s.gains.toMutableList().also { it[band] = v }) }
                }
            }
            PreampSection(cur) { v -> state.updateCurrent { it.copy(preampDb = Bands.clampPreamp(v)) } }
            HelpSection()
            Spacer(Modifier.size(24.dp))
        }
    }

    if (showSave) SaveDialog(settings, cur, onDismiss = { showSave = false }) { name ->
        state.update { s ->
            val (next, saved) = SettingsStore.upsert(s, name, cur.gains)
            next.withState(next.selected, next.stateFor(next.selected).copy(presetName = saved.name))
        }
        showSave = false
    }
    if (showDelete) AlertDialog(
        onDismissRequest = { showDelete = false },
        title = { Text("Slette «${cur.presetName}»?") },
        text = { Text("Bryterne blir stående som de er nå. Du kan lagre dem på nytt senere.") },
        confirmButton = {
            TextButton(onClick = { state.update { SettingsStore.delete(it, cur.presetName) }; showDelete = false }) { Text("Slett") }
        },
        dismissButton = { TextButton(onClick = { showDelete = false }) { Text("Avbryt") } },
    )
}

@Composable
private fun Section(content: @Composable () -> Unit) {
    Card(
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainer),
        shape = RoundedCornerShape(20.dp),
    ) { Column(Modifier.padding(16.dp)) { content() } }
}

@Composable
private fun AppIcon(choice: AppChoice) {
    val bitmap = choice.icon
    if (bitmap != null) {
        Image(bitmap, contentDescription = null, modifier = Modifier.size(28.dp).clip(RoundedCornerShape(7.dp)))
    } else {
        Box(
            Modifier.size(28.dp).clip(CircleShape).background(MaterialTheme.colorScheme.primaryContainer),
            contentAlignment = Alignment.Center,
        ) {
            Icon(Icons.Filled.Star, null, Modifier.size(16.dp), tint = MaterialTheme.colorScheme.onPrimaryContainer)
        }
    }
}

@Composable
private fun AppPicker(choices: List<AppChoice>, selected: AppChoice, onSelect: (AppChoice) -> Unit) {
    var open by remember { mutableStateOf(false) }
    Column {
        Text("EQ på", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Spacer(Modifier.size(6.dp))
        ExposedDropdownMenuBox(expanded = open, onExpandedChange = { open = it }) {
            OutlinedTextField(
                value = if (selected.isAllAudio) "All lyd (alle apper)" else selected.label,
                onValueChange = {},
                readOnly = true,
                singleLine = true,
                leadingIcon = { AppIcon(selected) },
                trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(open) },
                modifier = Modifier.fillMaxWidth().menuAnchor(MenuAnchorType.PrimaryNotEditable),
                shape = RoundedCornerShape(14.dp),
            )
            ExposedDropdownMenu(expanded = open, onDismissRequest = { open = false }) {
                choices.forEach { c ->
                    DropdownMenuItem(
                        leadingIcon = { AppIcon(c) },
                        text = {
                            Column {
                                Text(if (c.isAllAudio) "All lyd (alle apper)" else c.label, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                val sub = when {
                                    c.isAllAudio -> "Virker med alle apper, også strømmetjenester"
                                    c.playing -> "Spiller nå"
                                    else -> null
                                }
                                if (sub != null) Text(sub, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                        },
                        onClick = { onSelect(c); open = false },
                    )
                }
            }
        }
    }
}

@Composable
private fun StatusLine(app: AppChoice, cur: AppEqState, status: EffectStatus, androidAuto: Boolean, anyAppPlaying: Boolean) {
    val name = app.label
    val (text, ok) = when {
        !cur.enabled -> "EQ er av for ${if (app.isAllAudio) "all lyd" else name}. Lyden spilles uendret." to null
        status == EffectStatus.Off -> "Klang er slått av. Åpne appen igjen for å slå den på." to false
        status == EffectStatus.NoControl -> "En annen lydapp (f.eks. Dolby eller en annen EQ) styrer lyden nå. Slå den av for å bruke Klang." to false
        status == EffectStatus.Failed && app.isAllAudio -> "Telefonen tillot ikke EQ på all lyd. Velg en musikkapp i listen i stedet." to false
        status == EffectStatus.Failed -> "Klarte ikke å koble til lyden fra $name. Prøv «All lyd» i stedet." to false
        app.isAllAudio && androidAuto && anyAppPlaying ->
            "Android Auto: EQ virker på musikkapper som VLC. Apper som Spotify og YouTube Music slipper ikke EQ-apper til i bilen." to true
        app.isAllAudio && androidAuto ->
            "Android Auto: «All lyd» virker ikke i bilen. Spill fra en app som VLC, så legger Klang EQ på den." to false
        app.isAllAudio -> "EQ virker på all lyd fra telefonen." to true
        status == EffectStatus.Waiting -> "Venter på lyd fra $name. Start avspillingen, eller stopp og start den igjen." to null
        else -> "EQ virker på $name nå." to true
    }
    val color = when (ok) {
        true -> MaterialTheme.colorScheme.secondary
        false -> MaterialTheme.colorScheme.error
        null -> MaterialTheme.colorScheme.onSurfaceVariant
    }
    Row(verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(10.dp).clip(CircleShape).background(color))
        Spacer(Modifier.width(10.dp))
        Text(text, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurface)
    }
}

@Composable
private fun PresetSection(
    settings: KlangSettings,
    cur: AppEqState,
    onPick: (String) -> Unit,
    onSave: () -> Unit,
    onDelete: () -> Unit,
    onFlat: () -> Unit,
) {
    var open by remember { mutableStateOf(false) }
    val base = SettingsStore.gainsFor(settings, cur.presetName)
    val dirty = base != null && base != cur.gains
    val isCustom = SettingsStore.hasCustom(settings, cur.presetName)
    val shown = when {
        base == null -> "Egen justering"
        cur.presetName.equals(Presets.FLAT_NAME, true) -> "Flat (ingen endring)"
        else -> cur.presetName
    }
    Section {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("Innstilling", style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f))
            if (dirty) Surface(shape = RoundedCornerShape(8.dp), color = MaterialTheme.colorScheme.primaryContainer) {
                Text("Endret", Modifier.padding(horizontal = 8.dp, vertical = 2.dp), style = MaterialTheme.typography.labelMedium)
            }
        }
        Spacer(Modifier.size(10.dp))
        ExposedDropdownMenuBox(expanded = open, onExpandedChange = { open = it }) {
            OutlinedTextField(
                value = shown,
                onValueChange = {},
                readOnly = true,
                singleLine = true,
                trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(open) },
                modifier = Modifier.fillMaxWidth().menuAnchor(MenuAnchorType.PrimaryNotEditable),
                shape = RoundedCornerShape(14.dp),
            )
            ExposedDropdownMenu(expanded = open, onDismissRequest = { open = false }) {
                MenuHeader("Ferdige")
                Presets.builtIn.forEach { p ->
                    DropdownMenuItem(text = { Text(p.name) }, onClick = { onPick(p.name); open = false })
                }
                HorizontalDivider()
                MenuHeader(if (settings.customPresets.isEmpty()) "Mine (ingen ennå)" else "Mine")
                settings.customPresets.forEach { p ->
                    DropdownMenuItem(text = { Text(p.name) }, onClick = { onPick(p.name); open = false })
                }
                HorizontalDivider()
                DropdownMenuItem(text = { Text("Flat (ingen endring)") }, onClick = { onPick(Presets.FLAT_NAME); open = false })
            }
        }
        Spacer(Modifier.size(12.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick = onSave, modifier = Modifier.weight(1f)) { Text("Lagre …") }
            OutlinedButton(onClick = onDelete, enabled = isCustom, modifier = Modifier.weight(1f)) {
                Icon(Icons.Filled.Delete, null, Modifier.size(18.dp)); Spacer(Modifier.width(4.dp)); Text("Slett")
            }
            OutlinedButton(onClick = onFlat, modifier = Modifier.weight(1f)) {
                Icon(Icons.Filled.Refresh, null, Modifier.size(18.dp)); Spacer(Modifier.width(4.dp)); Text("Flat")
            }
        }
    }
}

@Composable
private fun MenuHeader(text: String) {
    Text(
        text,
        Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
        style = MaterialTheme.typography.labelMedium,
        color = MaterialTheme.colorScheme.primary,
    )
}

@Composable
private fun BandSliders(gains: List<Double>, enabled: Boolean, onChange: (Int, Double) -> Unit) {
    Row(Modifier.fillMaxWidth()) {
        for (i in 0 until Bands.COUNT) {
            Column(Modifier.weight(1f), horizontalAlignment = Alignment.CenterHorizontally) {
                Text(
                    formatDb(gains[i], unit = false),
                    style = MaterialTheme.typography.labelSmall,
                    fontSize = 11.sp,
                    maxLines = 1,
                    textAlign = TextAlign.Center,
                )
                VerticalSlider(
                    value = gains[i],
                    onChange = { v -> if (v != gains[i]) onChange(i, v) },
                    description = "${Bands.labels[i]} hertz",
                    enabled = enabled,
                )
                Text(
                    Bands.labels[i],
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                )
            }
        }
    }
}

@Composable
private fun PreampSection(cur: AppEqState, onChange: (Double) -> Unit) {
    val mayClip = remember(cur.gains, cur.preampDb) { FrequencyResponse.mayClip(cur.gains, cur.preampDb) }
    Section {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("Volumvern", style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f))
            Text(formatDb(cur.preampDb), style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.primary)
        }
        Text(
            "Senker lyden litt, så den ikke skurrer når du forsterker bass eller diskant.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Slider(
            value = cur.preampDb.toFloat(),
            onValueChange = { v -> val r = (v * 2).roundToInt() / 2.0; if (r != cur.preampDb) onChange(r) },
            valueRange = Bands.PREAMP_MIN_DB.toFloat()..Bands.PREAMP_MAX_DB.toFloat(),
            steps = 13,
            modifier = Modifier.semantics { contentDescription = "Volumvern" },
        )
        Row {
            Text("−10 dB", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.weight(1f))
            Text("−3 dB", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        if (cur.enabled && mayClip) {
            Spacer(Modifier.size(8.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Filled.Warning, null, tint = Color(0xFFF5B942), modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(8.dp))
                Text(
                    "Lyden kan skurre. Dra volumvernet mot −10 dB, eller senk noen av bryterne.",
                    style = MaterialTheme.typography.bodySmall,
                )
            }
        }
    }
}

@Composable
private fun HelpSection() {
    var open by rememberSaveable { mutableStateOf(false) }
    Section {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Filled.Info, null, tint = MaterialTheme.colorScheme.primary)
            Spacer(Modifier.width(10.dp))
            Text("Hvilke apper virker det med?", style = MaterialTheme.typography.titleSmall, modifier = Modifier.weight(1f))
            TextButton(onClick = { open = !open }) { Text(if (open) "Skjul" else "Vis") }
        }
        if (open) {
            Spacer(Modifier.size(4.dp))
            Text(
                "• «All lyd» virker med alle apper, også Spotify, YouTube og Netflix, på høyttaleren, " +
                    "hodetelefoner og Bluetooth.\n\n" +
                    "• I Android Auto sender telefonen lyden en annen vei, utenom «All lyd». Der virker EQ-en bare " +
                    "på musikkapper som sier fra til EQ-apper, som VLC.\n\n" +
                    "• Musikkapper som VLC sier fra til Klang når de spiller. De dukker opp i listen, " +
                    "og da kan du gi akkurat den appen sin egen EQ. Start Klang først, og start så avspillingen.\n\n" +
                    "• Klang husker innstillingen for hver app.\n\n" +
                    "• Klang kjører i bakgrunnen og viser et varsel. Trykk «Slå av Klang» i varselet for å stoppe.",
                style = MaterialTheme.typography.bodySmall,
                modifier = Modifier.widthIn(max = 600.dp),
            )
        }
    }
}

@Composable
private fun SaveDialog(settings: KlangSettings, cur: AppEqState, onDismiss: () -> Unit, onSave: (String) -> Unit) {
    var name by remember { mutableStateOf(if (SettingsStore.hasCustom(settings, cur.presetName)) cur.presetName else "") }
    val problem = SettingsStore.validateName(name)
    val exists = SettingsStore.hasCustom(settings, name)
    val hint = when (problem) {
        SaveNameProblem.Empty -> ""
        SaveNameProblem.ReservedName -> "Det navnet brukes av en ferdig innstilling. Velg et annet."
        SaveNameProblem.TooLong -> "Navnet kan ha maks ${SettingsStore.MAX_NAME_LENGTH} tegn."
        SaveNameProblem.None -> if (exists) "Du har allerede en innstilling med dette navnet. Den blir overskrevet." else ""
    }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Lagre innstilling") },
        text = {
            Column {
                OutlinedTextField(
                    value = name,
                    onValueChange = { name = it },
                    label = { Text("Navn") },
                    placeholder = { Text("F.eks. Kveldsfilm") },
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                    keyboardActions = KeyboardActions(onDone = { if (problem == SaveNameProblem.None) onSave(name) }),
                )
                if (hint.isNotEmpty()) {
                    Spacer(Modifier.size(8.dp))
                    Text(hint, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.tertiary)
                }
            }
        },
        confirmButton = { TextButton(onClick = { onSave(name) }, enabled = problem == SaveNameProblem.None) { Text("Lagre") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Avbryt") } },
    )
}
