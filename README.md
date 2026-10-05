# HearDelta

## In English

HearDelta is a local Windows application (C#/WPF, .NET 10) for comparing your
own speech understanding with and without a single hearing aid. Each ear is
measured separately, and results are stored on your computer in a local SQLite
database; nothing is sent anywhere.

- paired word and number tests without and with the hearing aid, in quiet or
  in noise, including an adaptive number test that reports a 50 % threshold
- pure-tone hearing thresholds from 62.5 Hz to 10 kHz, optionally with
  narrow-band masking of the other ear
- history and side-by-side comparison of measurements and hearing aids,
  printable reports
- every measurement records the exact audio output device and headphone
  profile; optional headphone equalization based on AutoEq

**HearDelta is not a medical device.** It is meant for personal, relative
comparisons only and does not replace clinical audiometry or a hearing aid
fitting. Without coupler calibration, levels are digital attenuations in dB,
not dB SPL.

The user interface, the documentation below and the speech material are
currently German only. Contributions of word lists and speech material for
other languages are welcome.

To build: install the .NET 10 SDK and [Git LFS](https://git-lfs.com) (the
stimulus audio is stored in LFS), clone the repository and run
`dotnet build HearDelta.sln`; see “Bauen, testen und bereitstellen” for the
full commands. HearDelta is licensed under `GPL-3.0-or-later` (see
[`LICENSE`](LICENSE)); the bundled AutoEq data remains under the MIT license.

---

HearDelta ist eine lokale C#-/WPF-Anwendung zur wiederholbaren,
seitengetrennten Erprobung des Wortverständnisses mit und ohne ein einzelnes Hörgerät.

Quellrepository: <https://github.com/HearDelta/HearDelta>. Die Audiodateien der
Stimuluspakete liegen in Git LFS; vor dem Klonen `git lfs install` ausführen.

## Zielbild

- Zahlen, Ein- und Mehrsilber mit und ohne Störgeräusch testen
- linkes und rechtes Ohr getrennt messen
- gepaarte Messungen ohne und mit Hörgerät vergleichen
- frühere Messungen und verschiedene Hörgeräte übersichtlich gegenüberstellen
- den verwendeten Audioausgang und Kopfhörer als Teil jeder Messung festhalten
- persönliche Hörschwellen ohne Hörgerät mit ausgewählten Sinustönen relativ
  vergleichen, optional mit Vertäubung des Gegenohrs
- Kopfhörer optional per parametrischem Equalizer (AutoEq) auf eine gemeinsame
  Zielkurve entzerren

Die Anwendung dient dem persönlichen relativen Vergleich. Sie ersetzt keine
kalibrierte Sprachaudiometrie und keine medizinische Diagnose. Ohne Messkuppler
sind die eingetragenen Pegel digitale Absenkungen in dB, keine bestätigten
dB-SPL-Werte am Ohr.

## Aktueller Stand

Implementiert sind die WPF-Grundlage, Personen, Messprofile, Messprotokoll, die
abgesicherte Stimulus-/Audio-Pipeline und der geführte gepaarte Testablauf:

- Personen als oberste Ebene; Wort-, Zahlen- und Hörschwellentests, Übungen
  und Messreihen werden direkt der ausgewählten Person zugeordnet, und neue
  Messungen lassen sich nur mit ausgewählter Person starten
- Verwaltung mehrerer Hörgeräte je Person (anlegen, bearbeiten mit optionaler
  eigener Bezeichnung, löschen nach Rückfrage); Änderungen wirken nur auf
  künftige Messungen, gespeicherte Messungen behalten ihren Geräte-Snapshot
- Name und Kommentar je Wort- und Hörschwellentest; der Name ist per Vorgabe
  das verwendete Hörgerät, bei Hörschwellentests „Hörschwelle“ bzw.
  „Hörschwelle mit Vertäubung“, und lässt sich wie der Kommentar später im
  Verlauf ändern

- explizite Auswahl eines aktiven Windows-WASAPI-Ausgangs
- keine unbemerkte Ausweichroute auf das Windows-Standardgerät
- getrennte leise Kanalprüfung links/rechts
- frei erfassbares Kopfhörerprofil mit Hersteller, Modell, Bauform und Impedanz
- Dokumentation von Verstärkerausgang, Gain, Startpegel und Pegelobergrenze
- lokale Speicherung der Profile in SQLite
- unveränderlicher Hardware-Snapshot für spätere Messungen
- Vergleichbarkeitsregel für unterschiedliche Hardwareprofile
- versioniertes Protokoll für gepaarte Messsitzungen ohne/mit Hörgerät
- reproduzierbar randomisierte Bedingungsreihenfolge mit getrennten Listen
- lokale SQLite-Speicherung von Messungen und unveränderten Rohantworten
- fünf versionierte Listen mit je 25 geschlossenen Phonemkontrastgruppen
- 125 deutsche Azure-Ralf-Mono-WAVs mit Roh-/Messdatei-SHA-256 und KI-Kennzeichnung
- zwei wählbare Kardinalzahlpakete mit Christoph und Katja, jeweils 900 Werte
  von 100 bis 999 in 36 getrennten Listen mit freier Zahleneingabe
- adaptiver Zahlentest (Vorgabe für Zahlen): in Ruhe wird der digitale
  Sprachpegel, im Störgeräusch der Signal-Rausch-Abstand je Antwort angepasst;
  Ergebnis ist die Schwelle für 50 % richtig ohne und mit Hörgerät und deren
  Differenz in dB statt eines Prozentwerts
- deterministisches Störgeräusch mit reproduzierbarem Seed/SNR: bandbegrenzt
  für Phonemkontraste und materialgebunden sprachgeformt für Kardinalzahlen
- seitengetrenntes Rendering nur für das geprüfte Ohr
- Wiedergabe nur über Endpunkt und Format des Hardware-Snapshots
- harte digitale Pegelobergrenze ohne automatische Vollpegelnormalisierung
- seed-stabile Auswahl zweier getrennter Listen und randomisierte Wortfolge
- Blockvorbereitung mit expliziten Prüfungen für Hörseite und Kopfhörersitz
- einmalige Wiedergabe je Wort mit fünf geschlossenen Antworten und
  `Nicht verstanden`
- lokales Zwischenspeichern jeder unveränderten Rohantwort einschließlich
  Katalog-, Datei-, Endpunkt- und Rendernachweis
- getrennte deskriptive Kennwerte ohne/mit Hörgerät als richtige Antworten von
  25, Prozentwert und Differenz in Prozentpunkten
- jederzeitiger Abbruch in den aktiven Wort- und Hörschwellentests mit
  gespeichertem Abbruchstatus und Anzeige der bis dahin erfassten Teilergebnisse
- Pause und Fortsetzung in beiden aktiven Testabläufen; der beim Pausieren
  laufende Stimulus wird verworfen und nach dem Fortsetzen vollständig wiederholt
- eigener Hörschwellentest mit 14 festgelegten Frequenzen von 62,5 Hz bis 10 kHz
- 500 Hz immer zuerst; danach feste Folge aufwärts bis 10 kHz und abwärts bis
  62,5 Hz oder eine seed-stabil zufällige Reihenfolge
- adaptiver Start jedes Folgetons 6 dB unter dem nächsten bereits gehörten
  Ton in Richtung des 500-Hz-Zentrums, begrenzt durch den initialen Startpegel
- seitengetrenntes Tonsignal je Pegelstufe: zweimal „3 kurze Töne · 500 ms
  Pause · 3 kurze Töne“ (150 ms Ton, 100 ms Lücke, 750 ms Pause nach jedem
  Signal), danach 3 dB lauter bis zur Pegelobergrenze des Messprofils
- 25-ms-Kosinusblenden auf exakt Null an beiden Tonenden zur Unterdrückung
  breitbandiger Einschalttransienten, insbesondere bei hohen Prüffrequenzen
- Erfassung per Knopfdruck beim ersten Höreindruck sowie automatische
  Kennzeichnung, wenn ein Ton bis zur Pegelobergrenze nicht gehört wurde
- audiogrammähnliche Ergebnisdarstellung mit logarithmischer Frequenzachse,
  nach unten zunehmendem Digitalpegel und eigenen Symbolen für linkes/rechtes
  Ohr sowie nicht gehörte Frequenzen
- Hörschwelle immer ohne Hörgerät, optional mit Vertäubung des Gegenohrs durch
  terzbreites Schmalbandrauschen um die Prüffrequenz mit wählbarem festem Pegel
  und Hörprobe
- lokales Hörschwellenprotokoll mit unveränderlichem Tonplan, Hardware-Snapshot,
  Vertäubungseinstellung und vollständigem Wiedergabenachweis
- Messverlauf für Hörschwellentests mit chronologischer Übersicht,
  Statuskennzeichnung, Frequenzdiagramm, Messaufbau und bestätigtem Löschen
- vollständiger lokaler Messverlauf einschließlich begonnener Worttests
- erneutes Öffnen gespeicherter Einzel- und Teilergebnisse direkt aus dem
  Worttestverlauf
- kombinierbare Worttest-Filter für Ohr, Hörgerät, Zeitraum, Status, Material,
  Hörumgebung und Freitext
- Gegenüberstellung zweier abgeschlossener Messungen mit 95-%-Intervallen für
  beide Bedingungen und deren Differenz
- direkte Vergleichbarkeit nur bei übereinstimmender Hörseite,
  Messbedingung, Katalogversion und kompatiblen Hardware-Snapshots
- sichtbare Warnung bei Profil-, Pegel-, Katalog-, SNR- oder sonstigen
  relevanten Messabweichungen

Der reale WASAPI-Treiberpfad ist mit einem Topping DX3 Pro+ sowie weiteren
aktiven Ausgängen geprüft. Die Anwendung speichert den jeweils
verwendeten Kopfhörer im Messprofil.

## Technik

- .NET 10
- WPF und MVVM mit CommunityToolkit.Mvvm
- NAudio 3 für die explizite WASAPI-Ausgabe
- Microsoft.Data.Sqlite für lokale Daten

## Stimuluspaket und Audio

Das aktive gebündelte Paket `de-DE-personal-phoneme-contrast-v1` enthält 25
geschlossene Gruppen mit je fünf ähnlich klingenden Antwortalternativen. Fünf
Listen rotieren jedes Gruppenwort genau einmal als Ziel; die Zufallstrefferquote
beträgt 20 Prozent. Acht Gruppen kontrastieren den Wortanfang, neun den Vokal
und acht das Wortende. IPA, Zielphonem und grobe Cue-Kategorie sind im Katalog
dokumentiert. Natürliche Wörter isolieren keine schmalen Frequenzbänder; das
Paket ist ausdrücklich nicht klinisch oder phonetisch äquivalent validiert.

Alle 125 aktiven Mono-WAVs enthalten die KI-generierte Azure-Stimme
`de-DE-RalfNeural` und keine menschliche Aufnahme. Kataloggebundene IPA-
Aussprache, SSML-Tempo `-30 %`, dienstverwalteter Modellstand, API-Rohdateien,
Normalisierung und sämtliche Hashes sind unter
`stimuli/de-DE/personal-phoneme-contrast-v1/` festgehalten. Da auch Azure-TTS
nicht bitidentisch regeneriert, bleiben die PCM16-/24-kHz-Rohdateien als
versionierter Provenienznachweis erhalten; die App liefert nur die zweipassig
auf Mono/22,05 kHz und -6,0 dBFS normalisierten Messdateien aus.

Das frühere Paket `de-DE-personal-relative-v1` mit 240 deterministisch über
Piper `2023.11.14-2` und `de_DE-thorsten-high` erzeugten WAVs bleibt als
unveränderte Altversion gebündelt, wird von der App aber nicht mehr als aktives
Paket ausgewählt. Sein Modellstand bleibt auf
`39ab474be869e9181350af6a65e4953eef67aaa0` gepinnt.

Für den persönlichen Zahlentest stehen zusätzlich die getrennten Pakete
`de-DE-personal-cardinal-numbers-christoph-v1` und
`de-DE-personal-cardinal-numbers-katja-v1` bereit. Sie enthalten jeweils alle
900 deutschen Kardinalzahlen von 100 bis 999 als KI-generierte Azure-Stimme,
per SSML-Kardinalzahl gesprochen und mit Tempo -30 %. Die 36 Listen enthalten
je 25 durch den Zahlenraum verteilte Werte; die Antwort erfolgt als freie,
exakt dreistellige Zahl. Die Zufallstrefferquote beträgt damit 1/900
(0,111... %). Stimme, Katalog, Audioindex und Prüfsummen gehören zur
unveränderlichen Materialidentität; Messungen verschiedener Stimmen sind daher
nicht als direkter Hörgerätevergleich gekennzeichnet. Der Zahlentest ist in
Ruhe sowie mit dem jeweils materialgebundenen sprachgeformten Rauschprofil
verfügbar. Das vorhandene bandbegrenzte weiße Rauschen wird bewusst nicht als
Zahlenrauschen verwendet.

Für das sprachangepasste Zahlenrauschen ist der Vertrag
[`cardinal-speech-shaped-noise` v1](docs/cardinal-speech-shaped-noise-v1.md)
festgelegt. Jedes Stimmenpaket erhält ein eigenes, an Katalog und Audioindex
gebundenes Profil. Sprachaktivität, RMS-Fenster, gleich gewichtetes
Langzeitspektrum, Filter, Seed, Vor-/Nachlauf und die Offline-Prüfung bei 44,1
und 48 kHz sind versioniert. Der Offline-Generator und die validierten
FIR-Profile liegen unter `scripts/new_cardinal_speech_shaped_noise_profiles.py`
beziehungsweise `stimuli/de-DE/cardinal-speech-shaped-noise-v1/`; der Bericht
steht unter `docs/reports/cardinal-speech-shaped-noise-v1/`. Die App
prüft Profil-, Material-, Koeffizienten- und Dateihashes, verwendet den
gespeicherten Seed und blockiert fehlende Profile, nicht unterstützte
Abtastraten und Vollpegelüberschreitungen ohne Ersatzrauschen oder
Mixnormalisierung. Die reale Hör-/Pegelabnahme bleibt separat offen.

Zur Wiedergabe wird der Quellstimulus auf -6 dBFS Spitzenwert normalisiert und
anschließend ausschließlich um den eingestellten negativen Digitalpegel
abgesenkt. Dieser Wert ist keine dB-SPL-Angabe. Bei Störgeräusch sind Seed,
Algorithmusversion und SNR Teil des Rendernachweises. Eine fehlende Datei, eine
abweichende SHA-256, ein anderes Audioformat, ein fehlender gespeicherter
WASAPI-Endpunkt oder eine geänderte Endpunktkonfiguration blockiert die
Wiedergabe.

Die selbst erzeugten Stimuli einschließlich der Azure-Audiodateien stehen wie
der übrige Code unter `GPL-3.0-or-later` (siehe Abschnitt „Lizenz“).

Lizenz- und API-Quellen: [Azure Speech Text-to-Speech](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/text-to-speech),
[Piper (MIT)](https://github.com/rhasspy/piper/blob/master/LICENSE.md),
[Thorsten-Modellkarte](https://huggingface.co/rhasspy/piper-voices/blob/39ab474be869e9181350af6a65e4953eef67aaa0/de/de_DE/thorsten/high/MODEL_CARD),
und [Thorsten-Voice (CC0)](https://github.com/thorstenMueller/Thorsten-Voice/blob/master/LICENSE).

Die lokale Datenbank liegt unter
`%LOCALAPPDATA%\HearDelta\heardelta.db`.
Sie und daraus erzeugte Exporte dürfen keine Repository-Inhalte werden.
Während der Entwicklungsphase wird ausschließlich das aktuelle Datenbankschema
unterstützt. Findet die Anwendung beim Start eine Datenbank mit abweichendem
oder fehlendem Schemastand, benennt sie diese samt `-wal`-/`-shm`-Dateien
unverändert in `heardelta.backup-yyyy-MM-dd_HH-mm-ss.db` (lokale Zeit) im
selben Ordner um und legt eine frische Datenbank an. Sie migriert oder löscht
keine alten Daten; gesicherte Messungen erscheinen danach nicht mehr in der App.

## Kopfhörerentzerrung (AutoEq)

Ein Messprofil kann optional eine parametrische Kopfhörerentzerrung enthalten.
Gebündelt ist unter `hardware/headphone-equalization/autoeq-7ae0f56-diffuse-field/`
ein Katalog für 1.378 ohrumschließende Modelle aus 14 Kombinationen von Quelle
und Messaufbau. Er beruht auf [AutoEq](https://github.com/jaakkopasanen/AutoEq)
(MIT) beim Commit `7ae0f56d53074872b028649617a22bbb4232feb7`: AutoEqs
Ergebnisbestand bestimmt die Modellauswahl, die Parameter sind mit AutoEqs
eigenem Code auf ein Diffusfeld-Ziel neu berechnet (siehe unten). Je Modell
gibt es genau eine `ParametricEQ.txt` mit SHA-256. Die Auswahlregeln:

- **Nur Over-Ears.** In-Ears, CIEMs, APEX-Module und Earbuds lassen sich am
  geprüften Ohr nicht zusammen mit einem Hörgerät tragen. Außerdem hängt ihr
  Hochtonfrequenzgang stark von Einführtiefe und Aufsatz ab.
- **Nur passiv betriebene Kopfhörer.** Funk-, Bluetooth- und ANC-Modelle
  bleiben nur mit ausdrücklich passiver oder kabelgebundener Messung, etwa
  „Bose QuietComfort SE (passive)“. Sonst verhindern eigene
  Signalverarbeitung, Lautstärkeregelung und ANC einen kontrollierten Pegel.
- **Eine Messung je Modell.** Bei mehreren Messungen bleibt die von AutoEq
  empfohlene.
- **Keine Varianten.** Baujahr, Polster, Filter, Schalterstellungen oder
  Exemplare werden zu einem Eintrag zusammengefasst, bevorzugt ohne
  Variantenangabe, sonst passiv/kabelgebunden. Impedanz-, Generations- und
  Versionsangaben (z. B. „DT 770 Pro (80 Ohm)“) bezeichnen eigene Produkte und
  bleiben getrennt.

Die Erkennung aktiver Modelle stützt sich auf den Modellnamen. Aktive
Kopfhörer ohne entsprechenden Namensbestandteil können daher im Katalog
verbleiben. AutoEq unterscheidet außerdem nicht zwischen ohrumschließend und
ohraufliegend; ohraufliegende Modelle (z. B. Sennheiser HD 25) drücken auf ein
Hinter-dem-Ohr-Gerät und eignen sich mit Hörgerät schlecht. Der Katalog wird
mit `scripts/new_autoeq_headphone_catalog.py` aus einer lokalen AutoEq-Kopie
reproduzierbar erzeugt (Aufruf siehe README des Katalogordners). Eigene Dateien im AutoEq-Format lassen sich
importieren, etwa für Kopfhörer ohne AutoEq-Eintrag.

Ziel ist das **Diffusfeld ohne Bassanhebung** statt AutoEqs Standardziel
(Harman over-ear 2018 plus 6 dB Bass). Harman bildet eine Hörvorliebe ab; das
Diffusfeld ist der richtungsneutrale Bezug für Kopfhörer. Es hängt vom
Messaufbau ab:

- GRAS 43AG-7 und dazu kompatible Kuppler (oratory1990, crinacle GRAS, Filk,
  Kuulokenurkka, Super Review, kr0mka, Auriculares Argentina, Regan Cipher):
  AutoEq-Target „Diffuse field GRAS KEMAR“.
- B&K 5128 (Rtings, HypetheSonics): AutoEq-Target „Diffuse field 5128“.
- HMS II.3 (Innerfidelity, Headphone.com Legacy, Rtings) und EARS + 711
  (crinacle): AutoEq hat dafür kein Diffusfeld-Target. Verwendet wird
  GRAS-KEMAR-Diffusfeld plus die Aufbauanpassung, die AutoEq für Harman auf
  diesem Aufbau nutzt (Harman des Aufbaus minus Harman GRAS).

Die Berechnung entspricht AutoEqs eigener Ergebniserzeugung (Glättung,
Verstärkungsgrenzen, je 4 Peaking-Filter mit Low- bzw. High-Shelf, 44,1 kHz);
mit dem Harman-Target reproduziert der Generator AutoEqs veröffentlichte
Parameter bis auf Rundung in der letzten Stelle. Grundlage ist die Rohmessung.
crinacle veröffentlicht keine Rohdaten; für dessen 137 Modelle wird die
Messung aus AutoEqs Harman-GraphicEQ zurückgerechnet. Gegenüber echter
Rohmessung wich das in 16 Stichproben zwischen 100 Hz und 8 kHz im
Effektivwert um 0,1 bis 0,8 dB ab, an einzelnen Stellen um bis zu 4 dB, weil die
Rückrechnung nur die bereits geglättete und begrenzte Harman-Entzerrung kennt. Solche Einträge tragen im Katalog
`basis: graphicEq` und in der Quelle der Entzerrung „aus GraphicEQ
rekonstruiert“. Der Katalog enthält die wirksamen Targets als CSV.

Der Nutzen liegt darin, dass verschiedene Kopfhörer einander ähnlicher
klingen. Exemplar- und Sitzstreuung von einigen dB oberhalb etwa 5 kHz bleiben
bestehen. Alle Targets beziehen sich auf das Trommelfell eines Kunstkopfs. Mit
Hörgerät nimmt dessen Mikrofon das Schallfeld unter der Muschel auf; die
Entzerrung wirkt dort nur näherungsweise, aber für beide Bedingungen gleich.

Technik: RBJ-Biquads (Peaking, Low-/High-Shelf mit Güte Q) in doppelter
Genauigkeit, wie AutoEq und Equalizer APO sie verwenden. Die tatsächliche
Vorabsenkung ist der Preamp der Datei, höchstens aber so hoch, dass die
Filterkette zwischen 20 Hz und min(20 kHz; 0,45 × Abtastrate) nirgends über
0 dB verstärkt. Pegelobergrenzen bleiben dadurch hart eingehalten.

- **Sprache und Rauschen:** Die Filterkette formt nach der Mischung beide
  gemeinsam. Der SNR bezieht sich auf die unentzerrten Anteile. Der
  Rendernachweis enthält Prüfsumme und angewendete Vorabsenkung.
- **Hörschwellentest:** Kein Filter im Signalweg. Jeder Ton erhält die exakt
  berechnete Verstärkung der Filterkette bei seiner Frequenz plus Vorabsenkung
  als feste, nie positive Pegelkorrektur. Das entspricht dem eingeschwungenen
  Filterausgang. Gespeichert werden weiterhin die unentzerrten Nennpegel; die
  Korrektur steht je Durchgang als `headphoneCorrectionDb` im Nachweis. Ohne
  Entzerrung fehlt das Feld, ältere Protokolle bleiben unverändert lesbar. Ein
  Vertäubungsrauschen erhält dieselbe Korrektur wie der Ton.
- **Vergleich:** Die Entzerrung gehört zum Hardware-Snapshot. Messungen mit
  anderer oder ohne Entzerrung werden als nicht direkt vergleichbar
  gekennzeichnet.

Die Pegel bleiben digitale dBFS-Werte; eine Entzerrung ist keine
Kupplerkalibrierung.

## Geführte Messung und Bewertung

Eine neue Messung verwendet das ausgewählte Phonemkontrast- oder
Kardinalzahlpaket und erzeugt zwei Blöcke mit unterschiedlichen Listen.
Bedingungsfolge, Listenwahl und die 25 Stimuli innerhalb jedes Blocks sind aus dem gespeicherten Randomisierungs-Seed
nachvollziehbar. Vor jedem Block müssen Hörseite und Kopfhörersitz bestätigt
werden. Ein fehlender oder formatabweichender
gespeicherter Audioausgang blockiert bereits Vorbereitung beziehungsweise
Blockstart; es gibt keinen Ersatzendpunkt.

Jeder Stimulus wird genau einmal wiedergegeben. Beim Phonemkontrasttest wird
eine der fünf Katalogalternativen gewählt; beim Zahlentest wird eine
dreistellige Zahl von 100 bis 999 eingegeben. In beiden Fällen ist
`Nicht verstanden` möglich. Nach jeder Antwort
wird die Messung lokal aktualisiert. Der Wiedergabenachweis hält unter anderem
Stimulus- und Audio-Hash, Katalogversion, Endpunkt, Digitalpegel, Störgeräusch-
Seed und SNR fest.

### Empfohlener Ablauf und Vorbelegung aus Vortests

Die Personenübersicht zeigt je Ohr einen empfohlenen Ablauf in fünf Schritten
mit Status und letztem Ergebnis. Der erste noch nicht durchgeführte Schritt ist
hervorgehoben; jeder Schritt lässt sich trotzdem jederzeit starten, wiederholen
oder überspringen. Freie Tests und die Übung bleiben darunter erreichbar.
`Starten` öffnet den Test mit vorbelegter Einstellung; gestartet wird erst dort.

1. Hörschwelle (ohne Hörgerät, optional mit Vertäubung)
2. Zahlen in Ruhe, adaptiv → Ruheschwelle
3. Zahlen im Störgeräusch, adaptiv → SNR-Schwelle
4. Phonemkontraste in Ruhe
5. Phonemkontraste im Störgeräusch

Lautstärke und SNR des Worttests werden bei jeder Auswahl von Ohr, Material,
Umgebung, Pegelverfahren oder Messprofil aus den Vortests vorbelegt; unter dem
Regler steht die Herkunft. Verwendet werden je Ohr nur abgeschlossene Tests
ohne Hörgerät mit demselben Messprofil und kompatiblem Hardware-Snapshot
(digitale Pegel sind nur innerhalb derselben Wiedergabekette übertragbar),
für die SNR-Schwelle nur Messungen mit Dauerrauschen. Persönliche Faustregeln
(`TestLevelRules`, keine Normwerte):

| Test | Lautstärke | SNR |
| --- | --- | --- |
| Zahlen in Ruhe, adaptiv | Ruheschwelle + 12 dB | – |
| Zahlen in Ruhe, fest | Ruheschwelle + 3 dB | – |
| Zahlen im Störgeräusch, adaptiv | Ruheschwelle + 25 dB | Start bei SNR-Schwelle + 8 dB, ohne Vortest 0 dB |
| Zahlen im Störgeräusch, fest | Ruheschwelle + 25 dB | SNR-Schwelle + 2 dB |
| Phoneme in Ruhe | Ruheschwelle + 10 dB | – |
| Phoneme im Störgeräusch | Ruheschwelle + 25 dB | SNR-Schwelle + 6 dB |

Fehlt die Ruheschwelle, wird sie aus dem Tonmittel 0,5–2 kHz der Hörschwelle
geschätzt (Tonmittel + 30 dB; an den eigenen Daten abgeglichen). Ohne
passenden Vortest bleibt der Startpegel des Messprofils. Die Werte sind auf
die Pegelobergrenze begrenzt und bleiben von Hand änderbar; die Herkunft zeigt
dann „Von Hand eingestellt“. Innerhalb einer Messreihe werden Lautstärke und
SNR zwischen den Paaren nicht neu vorbelegt.

### Lautstärke im Worttest

Beim Einrichten eines Worttests wird die Lautstärke der Sprache mit einem
Regler eingestellt (digitale Absenkung in 1-dB-Schritten zwischen -90 dB und der
Pegelobergrenze des Messprofils; Vorgabe ist der Startpegel des Profils bzw.
die Lautstärke der letzten Messung). Nach jeder Änderung wird – leicht
verzögert, damit Ziehen am Regler nicht jede Zwischenstufe abspielt – ein
festes Testwort des gewählten Materials in Ruhe auf dem geprüften Ohr
abgespielt; `Testwort abspielen` wiederholt es. Testwörter zählen nicht als
Messdarbietung. Die Lautstärke wird als `StartVolumeDb` im Hardware-Snapshot
der Messung gespeichert: fester Pegel, Sprachpegel im Störgeräusch bzw.
Startwert des adaptiven Verfahrens in Ruhe. Abweichende Lautstärken machen
Messungen nicht direkt vergleichbar; ausgenommen ist der adaptive Zahlentest in
Ruhe, bei dem die Startlautstärke nur Ausgangspunkt der Suche ist. Das frühere
Freitextfeld „Lautstärkezustand“ des Hörgeräts entfällt; die Lautstärke des
Hörgeräts kann im Kommentar der Messung festgehalten werden.

### Ablauf und Dauerrauschen

Nach jeder Antwort folgt das nächste Wort erst nach einer Pause von 1 s.

Im Störgeräuschtest läuft das Rauschen während des ganzen Blocks ohne
Unterbrechung über einen durchgehenden WASAPI-Strom auf dem exakt gespeicherten
Endpunkt. Es wird zu Blockbeginn eingeblendet und läuft 1,5 s allein, bevor das
erste Wort kommt; bei Pause, Blockende und Abbruch wird es ausgeblendet und nach
dem Fortsetzen erneut gestartet. Das Rauschen ist eine nahtlos wiederholte
20-s-Schleife (Übergang mit gleichbleibender Leistung) desselben Rauschtyps
wie bisher: materialgebunden sprachgeformt für Zahlen, bandbegrenzt für
Phonemkontraste. Eine Kopfhörerentzerrung wird im eingeschwungenen Zustand auf
die Schleife und getrennt auf die Sprache angewendet.

Der Rauschpegel (RMS) ist für die ganze Messung fest: Er entspricht der
eingestellten Lautstärke beim (Start-)SNR, bezogen auf den mittleren
Sprach-RMS des Pakets. Für jeden Stimulus wird die Sprache so eingemischt,
dass ihr RMS (bei Zahlen im aktiven Bereich) den SNR gegenüber dem Rauschpegel
exakt einhält; der digitale Sprachpegel variiert daher je Stimulus und wird im
Rendernachweis gespeichert. Würde ein Stimulus die Pegelobergrenze
überschreiten oder die Mischung den Vollpegel erreichen, wird die Darbietung
blockiert. Rauschpegel und Referenz-RMS stehen als `continuousNoise` in der
Sitzung; Messungen mit Dauerrauschen sind nur untereinander bei gleichem
Rauschpegel direkt vergleichbar. Die Übung verwendet weiterhin Rauschen nur
während des Stimulus.

### Adaptiver Zahlentest

Ein fester Pegel misst nur in einem schmalen Bereich: zu leise wird nichts,
zu laut alles verstanden, unabhängig vom Hörgerät. Der Zahlentest verwendet
deshalb per Vorgabe ein adaptives 1-hoch/1-runter-Verfahren mit Ziel 50 %
richtig; `Fester Pegel` bleibt wählbar. Nach einer richtigen Antwort wird der
nächste Stimulus schwerer, nach einer falschen leichter: 6 dB bis zur ersten
falschen Antwort, danach 2 dB.

- **Ruhe:** Verändert wird der digitale Sprachpegel. Start ist die eingestellte
  Lautstärke (deutlich hörbar wählen), Grenzen sind -90 dB und die
  Pegelobergrenze des Profils.
- **Störgeräusch:** Das Dauerrauschen behält seinen Pegel; verändert wird der
  Signal-Rausch-Abstand ab dem eingegebenen Start-SNR, indem die Sprache leiser
  oder lauter eingemischt wird (-20 dB bis +30 dB, nach oben zusätzlich so
  begrenzt, dass auch der leiseste Stimulus die Pegelobergrenze einhält).

Die Schwelle eines Blocks ist der Mittelwert aller Werte ab der ersten falschen
Antwort einschließlich des Werts, mit dem der nächste Stimulus dargeboten
worden wäre; sie wird erst ab acht gemittelten Werten angegeben. Zusätzlich
wird die Streuung dieser Werte gezeigt. Wurde eine falsche Antwort an der
Obergrenze gegeben, ist die Schwelle als möglicherweise zu niedrig markiert.
Ergebnis ist der Gewinn mit Hörgerät als Schwelle ohne minus Schwelle mit
Hörgerät; positiv heißt, mit Hörgerät genügt ein leiserer Pegel bzw.
ungünstigerer SNR. Pegel bleiben digitale dB, keine dB SPL.

Die Regel (Größe, Startwert, Schrittweiten, Grenzen) wird unveränderlich in der
Sitzung gespeichert (`adaptiveTrack`, Messvertrag `cardinal-number-adaptive`).
Die Auswertung prüft, dass jeder gespeicherte Wert aus Regel und vorangehenden
Antworten folgt. Adaptive Messungen sind nur mit adaptiven Messungen gleicher
Regel direkt vergleichbar; der veränderte Wert selbst darf dabei variieren.

Die Ergebnisansicht berechnet für beide Bedingungen getrennt
`richtige Antworten / 25 × 100` und zeigt die Differenz `mit minus ohne` in
Prozentpunkten. Groß-/Kleinschreibung und äußerer Leerraum beeinflussen die
Bewertung nicht; die gespeicherte Rohantwort selbst wird nicht verändert.
Diese deskriptiven Kennwerte enthalten keine klinische Einstufung und keine
dB-SPL-Aussage.

Während der Vorbereitung und des aktiven Worttests kann die Messung jederzeit
abgebrochen werden. Der Abbruchzeitpunkt wird ausdrücklich in der Messung
gespeichert. Bereits erfasste Antworten werden als Teilergebnis angezeigt;
Prozentwerte und die Differenz erscheinen nur für Bedingungen, zu denen schon
Antworten vorliegen.

Der aktive Worttest kann pausiert und fortgesetzt werden. Eine laufende oder
bereits abgespielte, aber noch nicht beantwortete Darbietung wird beim Pausieren
nicht protokolliert und nach dem Fortsetzen vollständig neu abgespielt.

## Hörschwellentest

Der Reiter `Hörschwelle` prüft die 14 festgelegten Frequenzen 62,5, 125, 250,
375, 500 und 750 Hz sowie 1, 1,5, 2, 3, 4, 6, 8 und 10 kHz. Der erste Ton ist
immer das 500-Hz-Zentrum. Die feste Reihenfolge läuft von dort zunächst aufwärts
bis 10 kHz und anschließend abwärts bis 62,5 Hz. Bei zufälliger Wiedergabe bleibt
500 Hz ebenfalls zuerst; die übrigen 13 Frequenzen werden aus dem gespeicherten
Seed reproduzierbar gemischt.

500 Hz beginnt bei der eingestellten Startlautstärke. Vorgabe ist -80 dBFS
oder, falls das Messprofil einen noch leiseren Startpegel vorgibt, dieser
leisere Wert. Der Regler `Startlautstärke` reicht von -90 dBFS (bzw. dem
leiseren Profilwert) bis 18 dB unter die Pegelobergrenze des Messprofils
(bei 0 dBFS also -18 dBFS); bei starker Schwerhörigkeit lässt sich der
Test so lauter beginnen, ohne viele unhörbare Stufen zu durchlaufen. Der Wert
sollte deutlich (etwa 10–15 dB) unter der erwarteten Hörschwelle liegen. Er
wird als `startAttenuationDbfs` gespeichert und vom letzten Test der Person
übernommen; Tests mit unterschiedlichem Startpegel werden im Kurvenvergleich
gekennzeichnet. Jeder weitere Ton beginnt 6 dB
unter der Hörschwelle des frequenzmäßig nächsten bereits gehörten Tones auf dem
Weg zurück zum 500-Hz-Zentrum, jedoch nie unter dem initialen Startpegel. Ist in
dieser Richtung noch kein Ton gehört worden, wird ebenfalls der initiale
Startpegel verwendet. Dadurch bleibt die Regel auch bei zufälliger Reihenfolge
eindeutig und reproduzierbar.

Jeder Sinuston wird ausschließlich auf dem gewählten Ohr wiedergegeben. Jede
Pegelstufe spielt das Signal „3 kurze Töne · 500 ms Pause · 3 kurze Töne“
zweimal; jeder Ton dauert 150 ms, zwischen den Tönen einer Dreiergruppe liegen
100 ms, nach jedem Signal 750 ms Stille. Eine Stufe dauert damit 5,1 s. Danach
folgt die nächste, um 3 dB höhere Pegelstufe. Jeder Ton beginnt und endet mit
einer 25-ms-Kosinusblende auf exakt Null, um hörbare Schalttransienten vor allem
bei hohen Frequenzen zu vermeiden. Die letzte Stufe liegt bei der in der
Kopfhörereinstellung (Messprofil) festgelegten Pegelobergrenze; sie wird als
`maximumAttenuationDbfs` gespeichert (Protokolle v9 bis v11: fest -6 dBFS).
Der Startpegel muss mindestens 18 dB darunter liegen. Das Muster
ist als `ThresholdSignalPattern` Teil des Protokolls (Version 12) und wird mit
jeder Messung gespeichert. Der Proband drückt
`ICH HÖRE DEN TON`, sobald er etwas wahrnimmt. Der Knopf wird dabei sofort für
genau diese Wiedergabe gesperrt. Anschließend wird derselbe Ton zur Bestätigung
wiederholt: Die Wiederholung beginnt 9 dB unter dem zuerst erkannten Pegel
(ohne Begrenzung durch den initialen Startpegel) und steigt wieder in 3-dB-Stufen
an. Gespeichert werden beide Wiedergabenachweise; als Hörschwelle gilt der Pegel
der Reaktion in der Wiederholung, und erst danach beginnt der nächste Ton. Bleibt
die Wiederholung bis zur Obergrenze ohne Reaktion, wird der Ton als nicht
gehört („nicht bestätigt“) protokolliert; der zuerst erkannte Pegel bleibt im
Nachweis sichtbar. Ohne Knopfdruck im ersten Durchgang wird der Ton als nicht
gehört protokolliert und der Ablauf ohne Zwischenmeldung mit dem nächsten Ton
fortgesetzt.

### Ohne Hörgerät

Die Hörschwelle wird ausschließlich ohne Hörgerät gemessen. Hörgeräte
unterdrücken gleichbleibende Sinustöne als vermeintliche Rückkopplung oder
Störgeräusch; eine Schwelle „mit Hörgerät“ wäre deshalb nicht aussagekräftig.
Seit Protokoll v12 gibt es im Hörschwellentest keine Hörgerätebedingung mehr.
Ältere Tests (v9 bis v11), die mit Hörgerät gespeichert wurden, bleiben lesbar
und werden im Verlauf, im Ausdruck und im Kurvenvergleich als „Mit Hörgerät
(älteres Protokoll)“ gekennzeichnet.

### Vertäubung des Gegenohrs

Optional wird das nicht geprüfte Ohr vertäubt, damit ein laut dargebotener Ton
nicht über den Kopf zum besseren Ohr hinübergehört wird. Während jedes
Durchgangs (auch der Bestätigungswiederholung) läuft auf dem Gegenkanal
Schmalbandrauschen:

- terzbreit um die jeweilige Prüffrequenz (Bandgrenzen Mittenfrequenz ·
  2^±1/6), als periodische Schleife (mindestens 2 s, Zweierpotenz an
  Abtastwerten) aus gleich starken Spektrallinien mit seed-stabiler
  Zufallsphase (`narrowband-third-octave-multisine-v1`)
- fester RMS-Pegel zwischen -90 und -20 dBFS (Vorgabe -50 dBFS), unabhängig
  vom ansteigenden Tonpegel; der Spitzenwert darf wie der Ton die Pegelobergrenze nicht
  überschreiten, sonst wird die Wiedergabe blockiert
- 50 ms Ein- und Ausblendung; das Rauschen läuft 1 s allein, bevor der erste
  Ton der Pegelrampe beginnt
- eine Kopfhörerentzerrung wirkt mit derselben Korrektur wie beim Ton
  (Verstärkung der Filterkette bei der Prüffrequenz plus Vorabsenkung)

`Rauschen anhören` spielt vor dem Start 3 s des Rauschens um 500 Hz auf dem
Gegenohr, um den Pegel einzustellen (deutlich hörbar, aber nicht unangenehm).
Die Hörprobe wird nicht protokolliert. Die Sitzung speichert die Einstellung als
`masking` (Verfahren, Pegel, Vorlauf, Blende, Seed), jeder Wiedergabenachweis
den tatsächlich gespielten Pegel als `maskingLevelDbfs`. Tests mit
unterschiedlicher oder ohne Vertäubung werden im Kurvenvergleich als
abweichender Messaufbau gekennzeichnet. Der Pegel ist eine digitale Angabe;
ob er wirksam, aber nicht übervertäubend ist, lässt sich ohne Kalibrierung
nicht bestimmen.

### Abbruch, Pause und Ergebnis

Auch der Hörschwellentest kann während jedes Frequenztones jederzeit abgebrochen
werden. Der Abbruch wird gespeichert; alle bis dahin abgeschlossenen Messpunkte
erscheinen in der Ergebnisansicht.

Beim Pausieren wird die laufende Frequenz nicht gewertet, auch nicht während
der Bestätigungswiederholung. Nach dem Fortsetzen beginnt dieselbe Frequenz
erneut mit dem ersten Durchgang bei ihrem aus den bisherigen Ergebnissen
berechneten Startpegel.

Die Ergebnisansicht zeigt die abgeschlossenen Spektrumstöne in einem
audiogrammähnlichen Frequenzdiagramm. Die Frequenzachse ist logarithmisch; die
Pegelachse läuft wie beim Audiogramm von leise oben nach laut unten. Linkes Ohr
wird blau mit X, rechtes Ohr rot mit Kreis dargestellt. Ein Pfeil an der
Pegelobergrenze des Tests kennzeichnet Frequenzen ohne Hörreaktion. Die Achse bleibt
ausdrücklich als digitaler dBFS-Pegel beschriftet und ist keine dB-HL- oder
dB-SPL-Angabe.

Vor dem Start werden der exakte gespeicherte WASAPI-Endpunkt, dessen Format und
die ausreichende Abtastrate erneut geprüft. Ein fehlender oder abweichender
Endpunkt blockiert den Test ohne Ersatzgerät. Note, Frequenz und laufender Pegel
bleiben während der Darbietung verborgen. Die Ergebnisse sind persönliche
digitale dBFS-Vergleichswerte und ohne dokumentierte Kupplerkalibrierung weder
dB SPL noch ein klinisches Audiogramm.

## Messverlauf

### Worttests

Der Messverlauf lädt alle lokal gespeicherten Worttests der ausgewählten
Person; ohne ausgewählte Person bleibt er leer. Begonnene Messungen und
abgeschlossene Messungen, die nicht mehr mit dem aktiven Stimuluspaket
auswertbar sind, bleiben sichtbar und filterbar; sie sind lediglich nicht für
die Ergebnisgegenüberstellung auswählbar. Zwei abgeschlossene Messungen können
gleichzeitig ausgewählt werden. Über `Anzeigen` lässt sich das Ergebnis einer
einzelnen auswertbaren Messung erneut in der Ergebnisansicht öffnen. Bei
abgebrochenen Messungen wird dort das bis zum Abbruch gespeicherte
Teilergebnis gezeigt; für den Vergleich zweier Messungen bleibt eine solche
Messung gesperrt.

Jede Messung zeigt ihren Namen und, falls vorhanden, den Kommentar. Über
`Bearbeiten` lassen sich beide nachträglich ändern; ein geleerter Name fällt auf
das verwendete Hörgerät zurück. Die Freitextsuche umfasst Name und Kommentar.
Über `Löschen` wird ein Worttest nach einer ausdrücklichen Rückfrage samt
Rohantworten, Name und Kommentar lokal entfernt; andere Messungen und eine
laufende Messreihe bleiben unberührt.
Name und Kommentar liegen in der Tabelle `measurement_annotations` außerhalb
der unveränderlichen Messprotokolle und beeinflussen weder Auswertung noch
Vergleichbarkeit.

Für die Prozentwerte ohne und mit Hörgerät werden 95-%-Wilson-Score-Intervalle
angezeigt. Das Intervall der Differenz `mit minus ohne` wird aus den beiden
Binomialanteilen nach dem Newcombe-Score-Verfahren gebildet. Diese Intervalle
sind eine deskriptive Orientierung, kein klinisches Konfidenzurteil und kein
Nachweis eines Unterschieds zwischen Hörgeräten.

Eine direkte Gegenüberstellung wird nur als kompatibel gekennzeichnet, wenn
Hörseite, Sprachmaterial, Hörumgebung, Stimuluspaket/Katalogversion,
Signal-Rausch-Abstand und der vollständige relevante Hardware-Snapshot
übereinstimmen. Dazu gehören der WASAPI-Endpunkt, Ausgabemodus, Audioformat,
Kopfhörer, Kopfhörerentzerrung, Verstärkerausgang, Gain, digitaler Messpegel,
Pegelobergrenze und Kalibrierstatus. Bei Abweichungen bleiben beide Einzelergebnisse sichtbar, die
Oberfläche warnt aber ausdrücklich vor einem direkten Gerätevergleich.

### Hörschwellentests

Der Reiter `Messverlauf` lädt beim Öffnen alle lokal gespeicherten
Hörschwellentests der ausgewählten Person, neueste zuerst. Abgeschlossene,
abgebrochene und unvollständige Tests werden eindeutig gekennzeichnet; bei abgebrochenen Tests bleiben alle
bis dahin gespeicherten Frequenzpunkte sichtbar. Die Detailansicht zeigt das
Frequenzdiagramm sowie Ohr, Vertäubung (bei älteren Tests ggf. Hörgerät), Messprofil, Audioendpunkt,
Kopfhörer, Protokollversion, Reihenfolge und die für den Test gespeicherte
Pegelobergrenze.

Gelesen werden alle Protokollversionen ab v9 (siehe „Stabile Verträge“) mit dem
aktuellen Tonkatalog; noch ältere gespeicherte Hörschwellentests werden als
nicht lesbar gemeldet und nicht umgedeutet. Ein Test kann über `Ausgewählten Test löschen` nach einer
ausdrücklichen Rückfrage lokal entfernt werden; andere Tests bleiben
unberührt. Name und Kommentar eines Hörschwellentests werden wie beim Worttest
über `Bearbeiten` geändert und mit dem Test gelöscht.

### Drucken

Diagramme und Ergebnisse lassen sich über den Windows-Druckdialog drucken oder
mit „Microsoft Print to PDF“ als PDF speichern:

- Ergebnis eines Hörschwellentests (`Drucken …` nach dem Test, `Drucken` in der
  Detailansicht des Verlaufs): Frequenzdiagramm, Tabelle der Hörschwellen je
  Frequenz, Ohr, Vertäubung (bei älteren Tests Hörgerät), Status und Messaufbau
- Kurvenvergleich von zwei bis vier Hörschwellentests (`Vergleich drucken`):
  Vergleichsdiagramm, Liste der Tests, Hinweis auf abweichenden Messaufbau und
  Frequenztabelle (bei zwei Tests mit Differenz)
- Ergebnis eines Worttests (`Drucken …` auf der Ergebnisseite, auch für aus dem
  Verlauf geöffnete Ergebnisse): Kennwerte ohne/mit Hörgerät, Differenz bzw.
  Gewinn, Verwechslungen im Phonemtest und Messaufbau
- Gegenüberstellung zweier Worttests (`Vergleich drucken`): beide Messungen
  nebeneinander mit 95-%-Intervallen und Vergleichbarkeitsbewertung

Jeder Ausdruck nennt Person, Name, Kommentar und Druckzeitpunkt und endet mit
dem Hinweis, dass die Pegel digitale dBFS-Werte ohne Kupplerkalibrierung sind
und kein klinischer Befund vorliegt. Diagramme werden mit demselben
Steuerelement wie am Bildschirm als Vektorgrafik gedruckt.

## Stabile Verträge

Während der Entwicklungsphase gilt grundsätzlich keine Rückwärtskompatibilität.
Ausdrücklich vereinbarte Ausnahmen:

- **Hörschwellenprotokoll ab v9 (vereinbart am 30.09.2026, auf alle Versionen
  ab v9 erweitert am 30.09.2026):** Gespeicherte Hörschwellentests jeder
  Protokollversion ab 9 bleiben lesbar; das gilt auch für jede künftige
  Version. Ältere Versionen werden angezeigt, verglichen und können gelöscht
  werden, werden aber nie neu erzeugt oder verändert. Neue Tests verwenden
  stets die aktuelle Version. Ein Vergleich zwischen verschiedenen
  Protokollversionen wird als nicht direkt vergleichbar gekennzeichnet.
  - v9: zwei 750-ms-Töne je Stufe, 3 dB je Stufe. Die Umwandlung der alten
    Zeitfelder in `levelStepDb` und `signalPattern` geschieht nur beim Lesen im
    Speicher (`HearingThresholdLegacyPayload`); die gespeicherte Nutzlast bleibt
    unverändert.
  - v10: aktuelles Tonsignal „3 kurze Töne · Pause · 3 kurze Töne“, ohne
    Bestätigungswiederholung; die erste Reaktion ist die Hörschwelle.
  - v11: wie v10, zusätzlich Bestätigungswiederholung ab 9 dB unter der ersten
    Reaktion (`confirmation`).
  - v12: wie v11, aber bis zur Pegelobergrenze des Messprofils statt bis
    -6 dBFS, mit wählbarem Startpegel (ab -90 dBFS bis 18 dB unter die
    Obergrenze, Vorgabe -80 dBFS), nur ohne Hörgerät (`condition` stets ohne Hörgerät,
    `hearingAid` leer) und optional mit Vertäubung des Gegenohrs (`masking`,
    je Wiedergabenachweis `maskingLevelDbfs`). Die Hörgerätebedingung von v9
    bis v11 bleibt lesbar und wird gekennzeichnet; v9 bis v11 kennen keine
    Vertäubung.
- **Additive Tabelle `measurement_annotations` (vereinbart am 02.10.2026):**
  Eine Datenbank der aktuellen Schemageneration, der diese Tabelle fehlt, wird
  beim Start nicht gesichert und neu angelegt, sondern nur um die Tabelle
  ergänzt (`CREATE TABLE IF NOT EXISTS`). Vorhandene Tabellen und Daten bleiben
  unverändert; für abweichende Schemagenerationen gilt weiter die Sicherung.

## Bauen, testen und bereitstellen

Vorausgesetzt werden Windows 10 oder 11 und zum Entwickeln das .NET-10-SDK.
Wiederherstellung, Build und Tests erfolgen aus dem Repository-Stamm:

```powershell
dotnet restore HearDelta.sln --configfile NuGet.Config
dotnet build HearDelta.sln -c Debug --no-restore
dotnet test tests\HearDelta.Core.Tests\HearDelta.Core.Tests.csproj -c Debug --no-build --no-restore
dotnet test tests\HearDelta.App.Tests\HearDelta.App.Tests.csproj -c Debug --no-build --no-restore
```

Starten:

```powershell
& .\src\HearDelta.App\bin\Debug\net10.0-windows\HearDelta.App.exe
```

Für Demo- oder Testdaten kann die Anwendung mit einer anderen Datenbank
gestartet werden; die persönliche Datenbank bleibt dabei unberührt:

```powershell
$env:HEARDELTA_DATABASE = "C:\Temp\heardelta-demo.db"
& .\src\HearDelta.App\bin\Debug\net10.0-windows\HearDelta.App.exe
```

Für die lokale Nutzung ohne separat installierte .NET-Laufzeit lässt sich ein
vollständiges Windows-x64-Paket erzeugen:

```powershell
dotnet publish src\HearDelta.App\HearDelta.App.csproj -c Release -r win-x64 --self-contained true -o artifacts\publish\win-x64
& .\artifacts\publish\win-x64\HearDelta.App.exe
```

Zur Weitergabe muss der gesamte Ordner `artifacts\publish\win-x64` erhalten
bleiben; die EXE allein reicht nicht. `artifacts\` ist von Git ausgeschlossen.
Personen, Profile und Messungen verbleiben ausschließlich in der lokalen
Datenbank unter `%LOCALAPPDATA%\HearDelta\heardelta.db`.

## Messaufbau

Der Test kann mit jedem Kopfhörer durchgeführt werden. Vor der ersten Messung
wird ein Messprofil mit Kopfhörer, Audioausgang, Verstärkerausgang und Gain
angelegt. Nur Messungen mit kompatiblen gespeicherten Hardware-Snapshots sind
direkt vergleichbar.

## Lizenz

HearDelta steht unter der GNU General Public License, Version 3 oder (nach Wahl)
jeder späteren Version (SPDX: `GPL-3.0-or-later`); der vollständige Text der
Version 3 liegt in [`LICENSE`](LICENSE). Das umfasst Quellcode, Tests,
Skripte, Dokumentation sowie die selbst zusammengestellten Stimuluslisten und
die daraus erzeugten Audiodateien.

Ausgenommen sind übernommene Fremddaten, die ihre ursprüngliche Lizenz
behalten: Die AutoEq-Zielkurven und der daraus abgeleitete Katalog unter
`hardware/headphone-equalization/autoeq-7ae0f56-diffuse-field/` stehen unter
der MIT-Lizenz (siehe die dortige `LICENSE`).

HearDelta ist kein Medizinprodukt. Die Software wird ohne jede Gewährleistung
bereitgestellt; Einzelheiten regeln die Abschnitte 15 und 16 der GPL-3.0.
