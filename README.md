# Klang

En grafisk equalizer for Windows 11 som virker på ett program om gangen.

- 10 ferdige forhåndsinnstillinger (bl.a. bass og diskant), pluss Flat for tilbakestilling.
- Lagre egne innstillinger med navn og hente dem frem igjen.
- Husker innstillingen for hvert program.
- Av/på for å sammenligne med originallyden.
- Volumvern (skyvebryter fra −10 til −3 dB) så lyden ikke forvrenges når du forsterker. Klang varsler hvis det er for lite.

## Slik virker det

```
Valgt program → CABLE Input → CABLE Output → Klang (10 filtre) → Høyttalere
```

Klang setter det valgte programmet til å spille til VB-Cable, leser lyden fra kabelens
andre ende, legger på EQ og spiller den ut på høyttalerne dine. Andre programmer spiller som før.

## Før du starter

1. Installer VB-Cable fra https://vb-audio.com/Cable/ og start PC-en på nytt.
2. Sjekk at høyttalerne (ikke «CABLE Input») er standard avspillingsenhet i Windows.

## Bygge

Krever Windows og .NET 8 SDK.

```
dotnet test tests/Klang.Core.Tests
dotnet publish src/Klang.App/Klang.App.csproj -c Release -r win-x64 -p:Platform=x64 -o publish/Klang
```

Kjør `publish/Klang/Klang.exe`. Innstillinger lagres i `%AppData%\Klang\settings.json`.

## Android

Mappen `android/` har Klang for Android (Kotlin og Jetpack Compose, Android 9 eller nyere).
Samme EQ, de samme ti innstillingene, egne lagrede innstillinger, frekvenskurve og volumvern.

- **Last ned:** https://github.com/BollaBallTre/klang/releases/latest/download/Klang.apk
- Lyden går gjennom Androids egen lydeffekt (DynamicsProcessing). VB-Cable trengs ikke.
- «All lyd» virker med alle apper. Musikkapper som VLC sier fra når de spiller, og kan da få sin egen EQ.
- GitHub Actions bygger APK-en ved hver endring i `android/` (`.github/workflows/android.yml`).

## Prosjekt

| Mappe | Innhold |
|---|---|
| `src/Klang.Core` | EQ-filtre, forhåndsinnstillinger og lagring. Ingen Windows-avhengigheter. |
| `src/Klang.Audio` | Lyd inn og ut (NAudio/WASAPI), liste over programmer, bytte av utgang per program. |
| `src/Klang.App` | Vinduet (WinUI 3). |
| `tests/Klang.Core.Tests` | Tester for kjernen. |
| `android/core` | Samme kjerne skrevet i Kotlin, med tester. |
| `android/app` | Android-appen. |

## Kjente begrensninger

- Bytte av utgang per program bruker et udokumentert Windows-grensesnitt. Virker det ikke,
  viser Klang hvordan du gjør det selv i volumblanderen.
- Forsinkelsen er rundt 30–60 ms. Merkbart i spill og video med leppesynk, sjelden i musikk.
- Hvis Klang krasjer, kan programmet bli stående på VB-Cable (stille). Start Klang igjen,
  så rydder den opp, eller sett utgangen tilbake i volumblanderen.
