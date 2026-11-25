package io.github.bollaballtre.klang

import android.app.Application

class KlangApp : Application() {
    lateinit var state: KlangState
        private set

    override fun onCreate() {
        super.onCreate()
        instance = this
        state = KlangState(this)
    }

    companion object {
        private lateinit var instance: KlangApp
        val state: KlangState get() = instance.state
    }
}
