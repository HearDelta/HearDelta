# Sprachangepasstes Kardinalzahlrauschen v1

Status: fachlicher und technischer Vertrag; Offline-Generator, validierte
materialgebundene FIR-Profile und technisch geprüfte Renderer-/App-Integration
vorhanden. Reale Hör-/Pegelabnahme weiterhin separat offen.

## Zweck und Grenze

Das spätere stationäre Rauschen soll ausschließlich das Langzeitspektrum eines
einzelnen eingefrorenen Kardinalzahlpakets nachbilden. Christoph und Katja
erhalten getrennte Profile, weil Stimme, Katalog und Audioindex zusammen die
Materialidentität bilden. Ein Profil darf weder auf das Phonemmaterial noch auf
das vorhandene bandbegrenzte weiße Rauschen umgedeutet werden.

Der Vertrag definiert einen digitalen SNR in dB. Ohne Kupplerkalibrierung folgt
daraus weder dB SPL noch eine klinische oder perzeptive Gleichwertigkeit.

## Unveränderliche Eingaben

- genau eines der Pakete `de-DE-personal-cardinal-numbers-christoph-v1` oder
  `de-DE-personal-cardinal-numbers-katja-v1`;
- alle 900 finalen Mess-WAVs, nicht die Azure-Rohdateien;
- PCM16, mono, 22.050 Hz sowie die erfolgreich geprüften Datei-SHA-256;
- SHA-256 von `catalog.json` und `audio-index.json` als Materialbindung;
- Protokoll-ID `cardinal-speech-shaped-noise`, Version 1.

Eine fehlende, zusätzliche oder hashabweichende Datei blockiert die Ableitung.
Bei einem späteren Stimuluswechsel entsteht eine neue Rauschprofilversion.

## Sprachaktivität, RMS und Langzeitspektrum

1. Jede WAV wird unverändert dekodiert. Die Analyse verwendet Hann-Fenster von
   25 ms mit 10 ms Schrittweite bei 22.050 Hz und eine auf 2.048 Punkte mit
   Nullen aufgefüllte DFT.
2. Ein Fenster gilt als aktiv, wenn sein RMS sowohl mindestens -70 dBFS als
   auch höchstens 40 dB unter dem lautesten Fenster derselben Datei liegt. Eine
   Datei ohne aktives Fenster blockiert die Erzeugung.
3. Das Sprach-RMS eines Stimulus wird über den Bereich vom Beginn des ersten bis
   zum Ende des letzten aktiven Fensters berechnet. Innere Pausen bleiben Teil
   dieses Bereichs; führende und nachlaufende Stille nicht.
4. Für die Spektralform wird jeder aktive Stimulus zunächst auf Einheits-RMS
   skaliert. Pro Stimulus werden die Fenster-Leistungsdichten gemittelt;
   anschließend werden die 900 Item-Leistungsdichten gleich gewichtet
   gemittelt. Damit zählt jede mögliche Zahl einmal und lange Dateien
   dominieren nicht allein wegen ihrer Dauer.
5. Die gemittelte Leistung wird in Terzbändern mit Mittenfrequenzen
   `125 × 2^(n/3)` für `n = 0..18` zusammengefasst. Die Leistungswerte werden
   zwischen den Mittenfrequenzen logarithmisch interpoliert. Die 19 Zielbänder
   reichen von der 125-Hz- bis zur 8.000-Hz-Mitte; der Filter umfasst jeweils
   auch die definierte halbe Terzbandbreite an beiden Außenkanten.
6. Die Quadratwurzel der interpolierten Leistungsantwort wird per
   Frequenzabtastung in eine symmetrische Impulsantwort überführt, mit einem
   Blackman-Fenster begrenzt und als linearphasiger FIR-Filter mit 4.097
   Koeffizienten gespeichert. Für 44,1 und 48 kHz entstehen getrennte
   Koeffizientensätze. Zielkurve, Koeffizienten, Werkzeugversion und SHA-256
   gehören zum späteren Profil.

Die Festlegungen heißen im Core `equal-item-welch-power-v1` und
`third-octave-log-power-v1`, die Filterableitung
`frequency-sampling-blackman-v1` und das RMS-Fenster
`first-to-last-active-frame-v1`. Änderungen benötigen eine neue
Protokollversion; sie dürfen nicht unter denselben Namen erfolgen.

## Spätere deterministische Erzeugung und Mischung

Der spätere Renderer erzeugt pro Darbietung weißes Ausgangsrauschen mit dem
gespeicherten Seed (`xorshift32-fir-v1`) und filtert es mit dem zum Paket
gehörenden FIR-Profil. Er erzeugt 200 ms Vorlauf, den vollständigen
Stimuluszeitraum und 200 ms Nachlauf. Nur die äußeren 20 ms erhalten eine
Kosinusblende. Das Rauschen ist während der Sprache stationär und wird nicht an
Silben oder Pegelverläufe moduliert.

Für den SNR wird das Sprach-RMS nach der eingestellten digitalen Absenkung im
oben definierten aktiven Bereich verwendet. Das Rausch-RMS wird über exakt die
zeitlich ausgerichteten Rauschsamples desselben Bereichs skaliert. Danach wird
der Mix nicht normalisiert. Überschreitet irgendein Sample den digitalen
Vollpegel oder eine gespeicherte Pegelgrenze, wird die Darbietung blockiert.
Ausgabe erfolgt weiterhin nur auf dem geprüften Ohr und dem exakt gespeicherten
WASAPI-Endpunkt.

Ist im Hardware-Snapshot eine Kopfhörerentzerrung hinterlegt, formt sie den
fertigen Mix aus Sprache und Rauschen gemeinsam, vor der äußeren Kosinusblende
und vor der Vollpegelprüfung. Der SNR bleibt über die unentzerrten Anteile
definiert; Seed, Profil und Aktivfenster ändern sich dadurch nicht. Die
Entzerrung ist Teil des Messaufbaus und wird mit ihrer Prüfsumme im
Rendernachweis festgehalten.

Der Zustand des XorShift32-Generators wird direkt aus dem vorzeichenlos
interpretierten 32-Bit-Seed gesetzt; Seed 0 verwendet fest `0x9E3779B9`.

## Erzeugung und Validierung

Der Offline-Generator `scripts/new_cardinal_speech_shaped_noise_profiles.py`
setzt diese Strategie um und muss bei jeder Regeneration:

1. Paket, Katalog, Audioindex, 900 WAVs und alle Hashes prüfen;
2. Aktivfenster, Item-RMS, gleich gewichtetes Langzeitspektrum und FIR-Profil
   deterministisch erzeugen;
3. ein kanonisch serialisiertes Profil mit Material-, Algorithmus- und
   Werkzeugidentität sowie Hashes schreiben;
4. bei identischer Eingabe und identischem Seed bitgleiche Ausgabe erzeugen und
   bei anderem Seed eine andere Ausgabe mit gleicher Spektralform nachweisen;
5. einen RMS-/Spektralbericht für beide Stimmen getrennt erstellen; 60 Sekunden
   Validierungsrauschen müssen jedes Ziel-Terzband mit höchstens ±1 dB
   Abweichung treffen, der gemessene SNR höchstens ±0,1 dB abweichen;
6. alle 900 Stimuli offline bei 44,1 und 48 kHz über den gesamten später
   zulässigen SNR- und Digitalpegelbereich rendern;
7. SNR, Mixspitzen, endliche Samples, Vor-/Nachlauf, Kanaltrennung,
   Pegelgrenzen und fehlenden Endpunkt-Fallback automatisiert prüfen;
8. Rechtehinweise der eigenen Textzusammenstellung, der eingefrorenen
   Azure-Ausgaben und des daraus abgeleiteten Rauschprofils dokumentieren.

Diese Prüfung beurteilt Rechen- und Materialkonsistenz. Aussprache,
Maskierungswirkung, Itemschwierigkeit, Listenäquivalenz und Wiederholbarkeit
benötigen danach eine getrennte Hör- beziehungsweise empirische Abnahme.

Die erzeugten Profile liegen unter
`stimuli/de-DE/cardinal-speech-shaped-noise-v1/`, der maschinenlesbare und der
lesbare Prüfbericht unter
`docs/reports/cardinal-speech-shaped-noise-v1/`. Die App lädt nur die
abgenommenen Profil-SHA-256, prüft Material- und Koeffizientenbindung und
rendert ohne Ersatzprofil oder nachträgliche Mixnormalisierung. Fehlendes
Profil, unpassende Abtastrate und Vollpegelüberschreitung blockieren die
Darbietung. Diese technische Freigabe ersetzt keine reale Hör-/Pegelabnahme.
