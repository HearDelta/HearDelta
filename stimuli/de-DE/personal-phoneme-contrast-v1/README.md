# Deutsches persönliches Phonemkontrastmaterial v1

Dieses Paket ist das aktive Stimulusmaterial der Anwendung. Es enthält 25
geschlossene Fünfergruppen ähnlich klingender deutscher Wörter. Die fünf
Listen `contrast-a` bis `contrast-e` enthalten je ein Ziel aus jeder Gruppe.
Eine lateinische Rotation sorgt dafür, dass jede Alternative gruppenweit genau
einmal Ziel ist und jede Liste alle fünf Zielpositionen je fünfmal verwendet.
Bei fünf Antwortalternativen beträgt die reine Zufallstrefferquote 20 Prozent.

Die 25 Gruppen verteilen sich auf acht Anfangs-, neun Vokal- und acht
Endkontraste. `contrast-groups.csv` ist die versionierte Quelle für Wörter,
breite phonemische IPA-Notation, Zielphoneme und grobe akustische
Cue-Kategorien. Die Kategorien `broad`, `low-mid`, `mid` und `high` sind keine
Frequenzbänder und behaupten keine Frequenzselektivität natürlicher Wörter.
Einige Alternativen sind regelgerecht gebildete flektierte oder imperative Formen wie
`büß`, `wühl`, `löt`, `höhn` und `reib`; sie werden bewusst als isolierte
Antwortoptionen verwendet.

Das Material ist eigenständig zusammengestellt, nicht klinisch oder
phonetisch äquivalent validiert und weder der WaKo-Einsilber-Reimtest noch eine
Nachbildung lizenzierter Testaufnahmen. Es dient ausschließlich dem persönlichen
relativen Vergleich und ersetzt keine Sprachaudiometrie.

## Audio und KI-Kennzeichnung

Alle 125 produktiven WAV-Dateien enthalten die KI-generierte Azure-Stimme
`de-DE-RalfNeural` und keine menschliche Aufnahme. Sie wurden mit
kataloggebundener IPA-Aussprache, SSML-Tempo `-30 %` und dem im Audioindex
dokumentierten dienstverwalteten Modellstand erzeugt. Die Azure-Ausgabe ist
nicht bitidentisch neu generierbar. Deshalb werden sowohl die unveränderten
API-Rohdateien unter `source-audio/` als auch die normalisierten Messdateien
unter `audio/` versioniert und jeweils per SHA-256 im `audio-index.json`
gebunden.

Die API liefert PCM16, Mono, 24 kHz. Die Messdateien werden in zwei FFmpeg-
Durchläufen auf PCM16, Mono, 22,05 kHz und einen Sample-Peak von -6,0 dBFS
gebracht: zuerst Zielformatkonvertierung, danach Pegelanpassung anhand des
tatsächlichen Peaks. Stille wird nicht automatisch entfernt. Die Rohdateien
werden nur als Provenienznachweis versioniert und nicht mit der Anwendung
ausgeliefert.

Die Wortlisten und Metadaten stehen unter CC0-1.0. Erzeugung und Nutzung der
Audiodateien erfolgen zusätzlich unter den jeweils geltenden Microsoft-Azure-
Dienstbedingungen. Modellgewichte und Trainingsdaten werden nicht verteilt.

## Erzeugung und Prüfung

Der Katalog kann ohne API-Zugriff neu aufgebaut werden:

```powershell
.\scripts\New-OpenAiCedarStimulusPack.ps1 -CatalogOnly
```

Die Azure-Erzeugung benötigt eine eigene Azure-Speech-Ressource; Schlüssel und
Region werden aus den Umgebungsvariablen `AZURE_SPEECH_KEY` und
`AZURE_SPEECH_REGION` gelesen. Ohne `-ForceApi` bleiben vorhandene
Azure-Rohdateien unverändert; `-ForceNormalization` erneuert ausschließlich die
Messnormalisate.

```powershell
$env:AZURE_SPEECH_KEY = '<Schlüssel>'
$env:AZURE_SPEECH_REGION = '<region>'
.\scripts\New-AzureStandardNeuralStimulusPack.ps1 -ForceApi -ForceNormalization
.\scripts\Test-ActiveStimulusPack.ps1
```

Das frühere Piper-/Thorsten-Paket unter `../personal-relative-v1/` bleibt als
separate, reproduzierbare Altversion erhalten und wird nicht überschrieben.
