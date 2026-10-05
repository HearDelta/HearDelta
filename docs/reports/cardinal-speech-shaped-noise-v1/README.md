# Validierung: sprachangepasstes Kardinalzahlrauschen v1

Der Offline-Generator hat beide materialgebundenen Profile ohne Audioausgabe erzeugt.
Alle 900 Mess-WAVs pro Stimme wurden gegen Audioindex, SHA-256, PCM16, Mono und
22,05 kHz geprüft. Die folgenden Werte beschreiben ausschließlich digitale Signale.
Sie sind weder dB SPL noch eine klinische oder perzeptive Validierung.

## de-DE-personal-cardinal-numbers-christoph-v1

- Profil: `stimuli/de-DE/cardinal-speech-shaped-noise-v1/de-DE-personal-cardinal-numbers-christoph-v1/profile.json`
- Profil-SHA-256: `40a88ad7891c705bd2f87f4712100c063da72dc74430d0b693933d167a8c7d44`
- Stimuli: 900
- Sprach-RMS: -24.530 bis -19.756 dBFS
- Aktiver Bereich: 0.917 bis 2.701 s

| Ausgaberate | max. Terzbandabweichung | max. SNR-Fehler | Grenzfälle | davon Vollpegel-blockiert |
| ---: | ---: | ---: | ---: | ---: |
| 44100 Hz | 0.306 dB | 0.000000 dB | 3600 | 900 |
| 48000 Hz | 0.294 dB | 0.000000 dB | 3600 | 900 |

Vollpegelüberschreitungen wurden bewusst nicht normalisiert. Sie markieren Kombinationen,
die ein späterer Renderer gemäß Vertrag blockieren muss.

## de-DE-personal-cardinal-numbers-katja-v1

- Profil: `stimuli/de-DE/cardinal-speech-shaped-noise-v1/de-DE-personal-cardinal-numbers-katja-v1/profile.json`
- Profil-SHA-256: `01fa0cda34b60dc253edf6f40ed43beb1ef180cada3116d81ccf2e6876943c27`
- Stimuli: 900
- Sprach-RMS: -25.598 bis -15.535 dBFS
- Aktiver Bereich: 1.107 bis 2.801 s

| Ausgaberate | max. Terzbandabweichung | max. SNR-Fehler | Grenzfälle | davon Vollpegel-blockiert |
| ---: | ---: | ---: | ---: | ---: |
| 44100 Hz | 0.400 dB | 0.000000 dB | 3600 | 900 |
| 48000 Hz | 0.315 dB | 0.000000 dB | 3600 | 900 |

Vollpegelüberschreitungen wurden bewusst nicht normalisiert. Sie markieren Kombinationen,
die ein späterer Renderer gemäß Vertrag blockieren muss.

## Noch nicht abgenommen

- keine physische Wiedergabe und keine Hörprüfung;
- keine Freigabe der Kardinalzahlmessung im Rauschen in der App;
- keine Aussage zu Maskierungswirkung, Item-/Listenäquivalenz oder Wiederholbarkeit.
