"""Stimmen alle Versionsangaben ueberein? — vor jedem Bau laufen lassen.

⚠ WARUM ES DAS GIBT: am 21.08.2026 wurde auf 0.7.0 gesprungen. Gesetzt wurden
`AppVersion` in der .iss und `file_version`/`product_version` in
export_presets.cfg — und `application/config/version` in project.godot wurde
vergessen. Ausgerechnet die: sie ist die EINZIGE, die der Spieler zu sehen
bekommt, unten rechts im Hauptmenue. Gemeldet mit »im Menue steht immer noch
die 0.6.0«.

Im Quelltext stand dazu die Beruhigung, die .iss trage »the same string ... so
there is one place to change it«. Das war schlicht falsch. Ein Kommentar, der
»es gibt nur eine Stelle« behauptet, ist schlimmer als gar keiner: er haelt den
Naechsten davon ab nachzusehen.

Dieses Skript BEHAUPTET nichts, es rechnet nach.

Aufruf:
    python packaging/pruefe_version.py            # prueft
    python packaging/pruefe_version.py 0.8.0      # setzt ueberall und prueft
"""
import io, os, re, sys

# ⚠ Die Windows-Konsole faehrt cp1252. Ohne diese zwei Zeilen stirbt das
# Skript beim Drucken des Hakens — also ausgerechnet im ERFOLGSZWEIG — und
# meldet Rueckgabe 1, obwohl alle vier Stellen uebereinstimmen. Ein Pruefer,
# der bei Erfolg »Fehler« sagt, ist schlimmer als keiner.
for strom in (sys.stdout, sys.stderr):
    if hasattr(strom, "reconfigure"):
        strom.reconfigure(encoding="utf-8", errors="replace")

WURZEL = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# (Datei, Beschreibung, Suchmuster) — die Gruppe 1 ist die Versionsangabe.
# ⚠ Die vierte Zeile ist die, die der Spieler sieht. Sie steht darum zuerst.
STELLEN = [
    ("project.godot", "im Hauptmenue sichtbar",
     r'config/version="([^"]*)"'),
    ("packaging/AkteEuropaReborn.iss", "Dateiname und Eintrag in der Systemsteuerung",
     r'#define AppVersion\s+"([^"]*)"'),
    ("export_presets.cfg", "Dateieigenschaften der .exe (FileVersion)",
     r'application/file_version="([^"]*)"'),
    ("export_presets.cfg", "Dateieigenschaften der .exe (ProductVersion)",
     r'application/product_version="([^"]*)"'),
]


def lies(pfad, muster):
    p = os.path.join(WURZEL, pfad)
    s = io.open(p, encoding="utf-8-sig", newline="").read()
    m = re.search(muster, s)
    return (m.group(1) if m else None), s, p


def normal(v):
    """»0.7.0.0« und »0.7.0« sind dieselbe Fassung — die .exe-Eigenschaften
    verlangen vier Glieder, die uebrigen drei."""
    if v is None:
        return None
    teile = [t for t in v.split(".") if t != ""]
    while len(teile) > 3 and teile[-1] == "0":
        teile.pop()
    return ".".join(teile)


def main():
    setzen = sys.argv[1] if len(sys.argv) > 1 else None

    if setzen:
        for pfad, was, muster in STELLEN:
            alt, s, p = lies(pfad, muster)
            neu = setzen + (".0" if "file_version" in muster or "product_version" in muster else "")
            s2 = re.sub(muster, lambda m: m.group(0).replace(m.group(1), neu), s, count=1)
            io.open(p, "w", encoding="utf-8", newline="").write(s2)
            print(f"  gesetzt  {pfad:34} {was:44} {alt} -> {neu}")
        print()

    werte = []
    for pfad, was, muster in STELLEN:
        v, _, _ = lies(pfad, muster)
        werte.append((pfad, was, v))
        print(f"  {pfad:34} {was:44} {v}")

    einzig = {normal(v) for _, _, v in werte}
    fehlt = [p for p, _, v in werte if v is None]

    print()
    if fehlt:
        print(f"  ✘ NICHT GEFUNDEN in: {', '.join(fehlt)}")
        return 1
    if len(einzig) != 1:
        print(f"  ✘ SIE LAUFEN AUSEINANDER: {sorted(einzig)}")
        print("     ⚠ Die erste Zeile ist die, die der Spieler sieht — wenn eine")
        print("       falsch sein muesste, dann bitte nicht die.")
        return 1
    print(f"  ✔ alle vier Stellen sagen {einzig.pop()}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
