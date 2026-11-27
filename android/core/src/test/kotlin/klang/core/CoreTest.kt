package klang.core

import java.io.File
import java.nio.file.Files
import kotlin.math.abs
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

class BiquadTest {
    @Test fun peakingHasRequestedGainAtCentre() {
        for ((f0, gain) in listOf(31.0 to 6.0, 1000.0 to -6.0, 16000.0 to 12.0)) {
            val c = BiquadCoefficients.peaking(48000.0, f0, gain, Bands.Q)
            assertTrue(abs(c.magnitudeDb(48000.0, f0) - gain) < 0.05)
        }
    }

    @Test fun zeroGainIsIdentity() = assertTrue(BiquadCoefficients.peaking(48000.0, 1000.0, 0.0, Bands.Q).isIdentity)
}

class DeviceEqTest {
    @Test fun bandsAreAscendingAndCoverAudibleRange() {
        assertEquals(DeviceEq.BAND_COUNT, DeviceEq.cutoffs.size)
        assertTrue(DeviceEq.cutoffs.zipWithNext().all { (a, b) -> a < b })
        assertTrue(DeviceEq.cutoffs.first() < 31f)
        assertEquals(20000f, DeviceEq.cutoffs.last())
    }

    @Test fun flatGivesZeroEverywhere() = assertTrue(DeviceEq.bandGains(Bands.flat()).all { it == 0f })

    @Test fun bassBoostRaisesLowBandsMoreThanHighBands() {
        val g = DeviceEq.bandGains(Presets.findBuiltIn("Kraftig bass")!!.gains)
        assertTrue(g.take(8).max() > 8f, "bassbånd ${g.take(8)}")
        assertTrue(abs(g.last()) < 0.5f, "høyeste bånd ${g.last()}")
    }

    @Test fun matchesSliderAtBandCentre() {
        val gains = Bands.flat().toMutableList().also { it[5] = 6.0 }
        val g = DeviceEq.bandGains(gains)
        val k = DeviceEq.centers.indices.minBy { abs(DeviceEq.centers[it] - 1000.0) }
        assertTrue(abs(g[k] - 6f) < 0.6f, "1 kHz-båndet ${g[k]}")
    }
}

class BandsTest {
    @Test fun clampPreampStaysWithinRange() {
        assertEquals(-10.0, Bands.clampPreamp(-12.0))
        assertEquals(-3.0, Bands.clampPreamp(-1.0))
        assertEquals(-4.5, Bands.clampPreamp(-4.3))
        assertEquals(-6.0, Bands.clampPreamp(Double.NaN))
        assertEquals(-6.0, Bands.clampPreamp(0.0))
    }

    @Test fun clampRoundsToHalfDb() {
        assertEquals(12.0, Bands.clamp(20.0))
        assertEquals(2.5, Bands.clamp(2.4))
        assertEquals(0.0, Bands.clamp(Double.NaN))
    }

    @Test fun mayClipDetectsWhenPreampIsTooSmall() {
        assertTrue(FrequencyResponse.mayClip(Presets.findBuiltIn("Kraftig bass")!!.gains, -3.0))
        assertFalse(FrequencyResponse.mayClip(Bands.flat(), -3.0))
    }
}

class PresetTest {
    @Test fun tenBuiltInPresetsWithUniqueNames() {
        assertEquals(10, Presets.builtIn.size)
        assertEquals(10, Presets.builtIn.map { it.name }.distinct().size)
        assertTrue(Presets.builtIn.all { it.gains.size == Bands.COUNT })
    }

    @Test fun includesBassAndTreble() {
        assertNotNull(Presets.findBuiltIn("bassforsterker"))
        assertNotNull(Presets.findBuiltIn("Diskantforsterker"))
        assertTrue(Presets.isReservedName(" flat "))
    }
}

class SettingsTest {
    private fun tempFile(): File = Files.createTempDirectory("klang").resolve("settings.json").toFile()

    @Test fun saveAndLoadRoundTrip() {
        val store = SettingsStore(tempFile())
        var s = KlangSettings()
        s = SettingsStore.upsert(s, "Kveldsfilm", listOf(1.0, 2.0, 3.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 4.0)).first
        s = s.withState("org.videolan.vlc", AppEqState("Kveldsfilm", Bands.flat(), false, -8.0)).copy(selected = "org.videolan.vlc")
        store.save(s)
        val back = store.load()
        assertEquals(s, back)
    }

    @Test fun missingFileGivesDefaults() = assertEquals(KlangSettings(), SettingsStore(tempFile()).load())

    @Test fun brokenFileIsKeptAndDefaultsReturned() {
        val f = tempFile().also { it.parentFile.mkdirs(); it.writeText("{ ikke json") }
        assertEquals(KlangSettings(), SettingsStore(f).load())
        assertTrue(File(f.path + ".broken").exists())
    }

    @Test fun loadSanitizesValues() {
        val f = tempFile().also {
            it.parentFile.mkdirs()
            it.writeText("""{"customPresets":[{"name":"Flat","gains":[]},{"name":"Min","gains":[99]}],"apps":{"a":{"gains":[1],"preampDb":-20}},"selected":""}""")
        }
        val s = SettingsStore(f).load()
        assertEquals(listOf("Min"), s.customPresets.map { it.name })
        assertEquals(12.0, s.customPresets[0].gains[0])
        assertEquals(Bands.COUNT, s.apps["a"]!!.gains.size)
        assertEquals(-10.0, s.apps["a"]!!.preampDb)
        assertEquals(KlangSettings.ALL_AUDIO, s.selected)
    }

    @Test fun upsertOverwritesSameNameIgnoringCase() {
        var s = SettingsStore.upsert(KlangSettings(), "Film", Bands.flat()).first
        s = SettingsStore.upsert(s, "film", Bands.flat().map { 3.0 }).first
        assertEquals(1, s.customPresets.size)
        assertEquals("Film", s.customPresets[0].name)
        assertEquals(3.0, s.customPresets[0].gains[0])
    }

    @Test fun validateName() {
        assertEquals(SaveNameProblem.Empty, SettingsStore.validateName("  "))
        assertEquals(SaveNameProblem.ReservedName, SettingsStore.validateName("Rock"))
        assertEquals(SaveNameProblem.TooLong, SettingsStore.validateName("x".repeat(33)))
        assertEquals(SaveNameProblem.None, SettingsStore.validateName("Bil"))
    }

    @Test fun deleteClearsPresetNameButKeepsGains() {
        var s = SettingsStore.upsert(KlangSettings(), "Bil", Bands.flat().map { 2.0 }).first
        s = s.withState("x", AppEqState("Bil", Bands.flat().map { 2.0 }))
        s = SettingsStore.delete(s, "bil")
        assertTrue(s.customPresets.isEmpty())
        assertEquals("", s.apps["x"]!!.presetName)
        assertEquals(2.0, s.apps["x"]!!.gains[0])
    }
}
