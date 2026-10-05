# Arbeitsregeln für HearDelta

Falls vorhanden, gelten zusätzlich die Regeln aus der lokalen, nicht
versionierten Datei `AGENTS.local.md`.

## Zweck und Messaufbau

- Die Anwendung dient dem persönlichen relativen Vergleich des
  Wortverständnisses mit und ohne ein einzelnes Hörgerät.
- Pro Messung wird nur das Hörgerät am geprüften Ohr getragen. Das Gerät oder
  Mikrofon der Gegenseite ist nicht gekoppelt und wird nicht getragen.
- Eine CROS-/BiCROS-Versorgung ist ausdrücklich nicht Gegenstand des Tests.
- Die Anwendung ist keine klinisch validierte Sprachaudiometrie und ersetzt
  keine medizinische Untersuchung oder Anpassmessung.

## Audio- und Vergleichsregeln

- Jede Messung muss den exakten WASAPI-Endpunkt und einen unveränderlichen
  Snapshot von Kopfhörer, Verstärkerausgang, Gain, Format und Pegelgrenzen
  speichern.
- Nie unbemerkt auf das Windows-Standardgerät oder einen anderen Ausgang
  ausweichen. Ein fehlender gespeicherter Endpunkt blockiert die Messung.
- Ohne dokumentierte Kupplerkalibrierung sind Pegel nur digitale Absenkungen
  in dB und dürfen nicht als dB SPL bezeichnet werden.
- Nur Messungen mit kompatiblen Hardware-Snapshots direkt vergleichen;
  Abweichungen sichtbar kennzeichnen.

## Datenschutz

- Keine persönlichen Messergebnisse, SQLite-Datenbanken, Audiogramme,
  Gesundheitsbefunde, Seriennummern, exportierten Sitzungen, Zugangsdaten oder
  lokalen Pfade committen.
- Generische, lizenzrechtlich zulässige Stimuluslisten und anonymisierte
  Testdaten dürfen versioniert werden.
- Persönliche Notizen, Handoffs und Pilot-Audio gehören nach `diagnostics/`,
  persönliche Hilfsskripte nach `scripts/local/`; beide sind von Git
  ausgeschlossen.

## Entwicklung

- Während der Entwicklungsphase gibt es keine Rückwärtskompatibilität zu alten
  Messungen, Protokollversionen oder Datenbankschemata. Der Code unterstützt nur
  den jeweils aktuellen Stand. Eine lokale Datenbank mit abweichendem oder
  fehlendem Schemastand wird beim Start unverändert in
  `<name>.backup-yyyy-MM-dd_HH-mm-ss.db` umbenannt und frisch erzeugt.
- Migrationen und Kompatibilitätslogik beginnen erst, wenn der Maintainer
  ausdrücklich sagt: „dies ist ein Kontrakt, zu dem wir kompatibel bleiben
  müssen“. Der dann benannte Umfang ist als stabiler Vertrag zu dokumentieren.
- Vereinbarte Kontrakte stehen in `README.md` unter „Stabile Verträge“
  (derzeit: alle Hörschwellenprotokolle ab v9 bleiben lesbar).
- Alte Daten niemals automatisch löschen oder inhaltlich umschreiben; zulässig
  ist nur das oben beschriebene Umbenennen in eine Sicherungsdatei.
- Kanonisches Repository: `https://github.com/HearDelta/HearDelta`.
- Große Audiodateien (`*.wav`) werden über Git LFS versioniert.
- UI-Arbeit bleibt bis zur Abnahme uncommitted; objektiv abgeschlossene
  nicht-iterative Änderungen proportional testen, committen und pushen.
- Vor Änderungen Working Tree prüfen und fremde Änderungen erhalten.
- Build und Kernregeln mindestens mit den Befehlen aus `README.md` prüfen.
