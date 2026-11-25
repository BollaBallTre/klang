package io.github.bollaballtre.klang

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.media.audiofx.AudioEffect
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
import io.github.bollaballtre.klang.ui.KlangScreen
import io.github.bollaballtre.klang.ui.KlangTheme

class MainActivity : ComponentActivity() {
    private val askNotifications = registerForActivityResult(ActivityResultContracts.RequestPermission()) { }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        handleIntent(intent)
        if (Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) askNotifications.launch(Manifest.permission.POST_NOTIFICATIONS)
        setContent { KlangTheme { KlangScreen(KlangApp.state) } }
    }

    override fun onStart() {
        super.onStart()
        KlangApp.state.setRunning(true)
        KlangService.start(this)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleIntent(intent)
    }

    /** «Equalizer»-knappen i en musikkapp åpner Klang med den appen valgt. */
    private fun handleIntent(intent: Intent?) {
        if (intent?.action != AudioEffect.ACTION_DISPLAY_AUDIO_EFFECT_CONTROL_PANEL) return
        val pkg = intent.getStringExtra(AudioEffect.EXTRA_PACKAGE_NAME) ?: return
        val id = intent.getIntExtra(AudioEffect.EXTRA_AUDIO_SESSION, AudioEffect.ERROR_BAD_VALUE)
        if (pkg == packageName) return
        KlangApp.state.openSession(id, pkg)
        KlangApp.state.select(pkg)
    }
}
