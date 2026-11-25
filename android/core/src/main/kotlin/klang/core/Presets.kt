package klang.core

data class Preset(val name: String, val gains: List<Double>, val isBuiltIn: Boolean)

object Presets {
    const val FLAT_NAME = "Flat"

    /** De ti ferdige forhåndsinnstillingene. Flat er en tilbakestilling og er ikke med her. */
    val builtIn: List<Preset> = listOf(
        p("Bassforsterker", 6, 5, 4, 2, 0, 0, 0, 0, 0, 0),
        p("Kraftig bass", 9, 8, 6, 3, 1, 0, 0, 0, 0, 0),
        p("Diskantforsterker", 0, 0, 0, 0, 0, 1, 2, 4, 5, 6),
        p("Bass og diskant", 6, 5, 3, 0, -1, -1, 0, 3, 5, 6),
        p("Tale og podkast", -6, -4, -2, 0, 2, 3, 4, 3, 1, -1),
        p("Vokal", -2, -1, 0, 1, 3, 4, 3, 1, 0, -1),
        p("Rock", 5, 4, 2, -1, -2, -1, 1, 3, 4, 4),
        p("Pop", -1, 1, 3, 4, 3, 0, -1, -1, 1, 2),
        p("Elektronisk", 6, 5, 2, 0, -2, 0, 1, 2, 4, 5),
        p("Klassisk", 3, 2, 1, 0, 0, 0, -1, 1, 2, 3),
    )

    val flat = Preset(FLAT_NAME, Bands.flat(), true)

    fun findBuiltIn(name: String): Preset? =
        builtIn.firstOrNull { it.name.equals(name, ignoreCase = true) }
            ?: if (name.equals(FLAT_NAME, ignoreCase = true)) flat else null

    fun isReservedName(name: String): Boolean = findBuiltIn(name.trim()) != null

    private fun p(name: String, vararg g: Int) = Preset(name, g.map { it.toDouble() }, true)
}
