# Sprachangepasstes Kardinalzahlrauschen v1

Dieses Verzeichnis enthält zwei getrennte, deterministisch erzeugte
FIR-Rauschprofile. Jedes Profil ist über Katalog- und Audioindex-SHA-256 an
genau eines der eingefrorenen Kardinalzahlpakete gebunden. Die Profile dürfen
nicht zwischen Christoph und Katja ausgetauscht oder für anderes
Stimulusmaterial verwendet werden.

`profile.json` enthält die Ziel-Terzbänder und je 4.097 FIR-Koeffizienten für
44,1 und 48 kHz. Rauschsamples werden bei einer späteren Darbietung mit dem
versionierten Generator `xorshift32-fir-v1` aus dem gespeicherten Seed erzeugt;
eine statische Rausch-WAV ist deshalb weder erforderlich noch enthalten.

Regeneration und Offline-Validierung:

```powershell
python scripts/new_cardinal_speech_shaped_noise_profiles.py
```

Das Werkzeug benötigt Python 3 und NumPy; die eingecheckten Profile wurden mit
Python 3.12.14 und NumPy 2.3.5 erzeugt. Der geprüfte Bericht liegt unter
`docs/reports/cardinal-speech-shaped-noise-v1/`. Die Profile sind aus
der eigenen Textzusammenstellung und den für die private Anwendung erzeugten,
eingefrorenen Azure-AI-Speech-Ausgaben abgeleitet. Sie sind nicht klinisch
validiert. Die App bindet jedes Profil anhand der abgenommenen Profil-,
Katalog-, Audioindex- und Koeffizienten-SHA-256 an genau das zugehörige
Stimmenpaket. Die technische Rendererfreigabe ersetzt keine reale
Hör-/Pegelabnahme.
