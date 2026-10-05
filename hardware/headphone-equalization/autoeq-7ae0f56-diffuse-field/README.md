# Kopfhörerentzerrungen auf Diffusfeld, berechnet mit AutoEq 7ae0f56

- Quelle: <https://github.com/jaakkopasanen/AutoEq>
- Commit: `7ae0f56d53074872b028649617a22bbb4232feb7` (2025-07-20, „New measurements and results“)
- Lizenz: MIT, Copyright (c) 2018-2022 Jaakko Pasanen, siehe `LICENSE`
- Inhalt: 1.378 Modelle, alle over-ear, aus 14 Kombinationen von Quelle und
  Messaufbau. Die Modellauswahl richtet sich nach den 8.850
  `results/<Quelle>/<Messaufbau>/<Modell>/<Modell> ParametricEQ.txt` des
  Commits; die Parameter sind neu berechnet.
- Auswahl, in dieser Reihenfolge (Regeln im Generator):
  1. nur over-ear (entfernt In-Ears, CIEMs, APEX-Module, Earbuds);
  2. je Modellname nur die in AutoEqs `results/README.md` empfohlene Messung;
  3. Funk-/Bluetooth-/ANC-Modelle (nach Name) nur mit ausdrücklich passiver
     oder kabelgebundener Messung (`passive`, `wired`, `analog` ohne `ANC on`,
     `active`, `wireless`, `power on`);
  4. Varianten zusammenfassen: Klammerangaben außer Impedanz, Generation,
     Version und Mk werden ignoriert; bevorzugt bleibt der Name ohne
     Variantenangabe, sonst passiv/kabelgebunden/ANC aus/Serienzustand.
- Ziel: Diffusfeld ohne Bassanhebung, je Messaufbau (`targets/`, Herkunft und
  SHA-256 in `catalog.json` unter `targets`):

  | Target | Messaufbauten | Einträge | Herkunft |
  |---|---|---:|---|
  | `diffuse-field-gras-kemar` | GRAS 43AG-7 und kompatible | 680 | AutoEq „Diffuse field GRAS KEMAR“, unverändert |
  | `diffuse-field-hms-ii3` | HMS II.3 | 661 | GRAS KEMAR + (HMS II.3 Harman − Harman GRAS) |
  | `diffuse-field-bk-5128` | B&K 5128 | 26 | AutoEq „Diffuse field 5128“, unverändert |
  | `diffuse-field-ears-711` | EARS + 711 | 11 | GRAS KEMAR + (EARS + 711 Harman − Harman GRAS) |

- Berechnung: AutoEqs Code des Commits wie bei dessen eigener
  Ergebniserzeugung (`dbtools/db.ipynb`, `update_results`): Standardglättung
  und -begrenzung, `min_mean_error`, Bassanhebung 0 dB, Parametrik
  `4_PEAKING_WITH_LOW_SHELF` + `4_PEAKING_WITH_HIGH_SHELF` bei 44,1 kHz. Mit
  dem Harman-Target reproduziert dieser Weg AutoEqs veröffentlichte Parameter
  bis auf Rundung in der letzten Stelle. Das Ergebnis ist deterministisch.
- Grundlage (`basis`): Rohmessung aus `measurements/` (1.241 Einträge). crinacle
  veröffentlicht keine Rohdaten; dessen 137 Einträge (`basis: graphicEq`)
  beruhen auf einer aus AutoEqs Harman-`GraphicEQ.txt` zurückgerechneten
  Messung (Harman-Target mit 6 dB Bassanhebung minus Entzerrung). AutoEqs
  Anhebungsgrenze von 6 dB ist darin schon angewendet. In 16 Stichproben mit
  Rohmessung wich dieser Weg zwischen 100 Hz und 8 kHz im Effektivwert um
  0,1–0,8 dB vom echten ab, an einzelnen Frequenzen um bis zu 4 dB.
- Grenzen: Aktive Modelle ohne entsprechenden Namensbestandteil werden nicht
  erkannt; AutoEq trennt nicht zwischen ohrumschließend und ohraufliegend.
  Die abgeleiteten HMS-II.3- und EARS-Targets übernehmen AutoEqs
  Harman-Aufbauanpassung und sind keine gemessenen Diffusfeldkurven.

`catalog.json` enthält je Eintrag den erzeugten `ParametricEQ.txt`-Text
(`text`) und dessen SHA-256 über die UTF-8-Bytes (`sha256`), dazu Target,
Grundlage, deren Pfad im AutoEq-Commit (`basisPath`) und SHA-256
(`basisSha256`). Die App parst ausschließlich `text` und prüft die Prüfsumme
bei jeder Auswahl.

Erzeugung (ohne Checkout, daher unabhängig von Windows-Pfadlängen; fehlende
Blobs eines Blobless-Klons lädt der Generator gesammelt nach). AutoEq braucht
Python 3.8–3.11 und seine festgelegten Pakete:

```powershell
git clone --filter=blob:none --no-checkout https://github.com/jaakkopasanen/AutoEq.git <autoeq>
uv run --no-project --python 3.11 --with numpy~=1.24.4 --with scipy~=1.10.1 --with matplotlib~=3.7.3 --with Pillow~=10.0.1 --with tabulate~=0.9.0 --with soundfile~=0.12.1 --with pyyaml~=6.0 --with tqdm~=4.66.1 python scripts/new_autoeq_headphone_catalog.py --autoeq-repo <autoeq>
```
