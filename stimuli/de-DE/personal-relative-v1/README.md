# Deutsches persönliches Vergleichsmaterial v1

Dieses Paket enthält vier Listen mit je 20 Zahlen, Einsilbern und
Mehrsilbern. Die Auswahl wurde für diesen persönlichen relativen Vergleich
neu zusammengestellt und ist nicht phonetisch oder klinisch validiert. Die
Listen dürfen daher nicht als Freiburger Sprachtest oder als gleichwertige
klinische Sprachaudiometrie bezeichnet werden.

`catalog-source.csv` ist die bearbeitbare Quelle. `catalog.json`,
`audio-index.json` und `audio/*.wav` werden reproduzierbar mit
`scripts/New-StimulusAudioPack.ps1` erzeugt. Jede Laufzeitdatei wird über
SHA-256 gebunden; eine fehlende oder veränderte Datei blockiert die Wiedergabe.

## Lizenz und Herkunft

- Die in `catalog-source.csv` zusammengestellten Texte und die daraus für
  dieses Repository erzeugten WAV-Dateien stehen unter CC0-1.0.
- Die WAV-Dateien sind synthetische Sprache, keine menschlichen Aufnahmen.
- Generator: Piper `2023.11.14-2`, MIT-Lizenz.
- Stimme: `de_DE-thorsten-high` aus `rhasspy/piper-voices`, gepinnt auf
  Revision `39ab474be869e9181350af6a65e4953eef67aaa0`.
- Die Modellkarte nennt MIT für das Modell und CC0 für den zugrunde liegenden
  Thorsten-Voice-Datensatz.
- Modell und Piper-Laufzeit werden nicht in diesem Repository verteilt.

Quellen:

- https://github.com/rhasspy/piper/releases/tag/2023.11.14-2
- https://github.com/rhasspy/piper/blob/master/LICENSE.md
- https://huggingface.co/rhasspy/piper-voices/blob/39ab474be869e9181350af6a65e4953eef67aaa0/de/de_DE/thorsten/high/MODEL_CARD
- https://github.com/thorstenMueller/Thorsten-Voice/blob/master/LICENSE

## Wiederholung

Eine gepaarte Sitzung muss zwei verschiedene Listen verwenden. Version 1
ermöglicht je Material zwei Sitzungen ohne Listenwiederholung. Weitere
Sitzungen müssen eine Wiederholung sichtbar kennzeichnen oder auf ein späteres
Materialpaket ausweichen; die App darf eine Wiederholung nicht verschleiern.
