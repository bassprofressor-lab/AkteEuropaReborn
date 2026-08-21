"""Das Programmsymbol zeichnen — als .ico für Windows und als .png für Godot.

⚠ WARUM PROGRAMMIERT UND NICHT GEMALT: ein Symbol muss bei **16 px** noch
lesbar sein, und das entscheidet sich nicht am schönen 256er. Alles hier ist aus
wenigen grossen Flächen gebaut. Gezeichnet wird 8-fach überabgetastet und dann
heruntergerechnet; das gibt saubere Kanten ohne eine eigene Bibliothek.

DAS MOTIV: **die Übersichtskarte des Spiels**, im Rahmen der Bedienleiste.

⚠ Der erste Entwurf (der Sichtschlitz eines Kampfläufers) war handwerklich in
Ordnung und trotzdem falsch: er hätte zu jedem Weltraumspiel gepasst. Das hier
ist ein Aufbauspiel mit Draufsicht, dessen Wiedererkennungszeichen die kleine
Karte unten rechts ist — Wasser, Land, der helle Ausschnittrahmen und ein paar
farbige Punkte für die Parteien. Wer das Spiel kennt, erkennt es sofort; wer es
nicht kennt, sieht wenigstens, dass es um eine Landkarte geht.

⚠ UNSERE ZUTAT, vollständig: das Original von 1997 hat ein eigenes Symbol, das
wir NICHT verwenden — der Nachbau liefert grundsätzlich kein Byte des Originals
mit. Übernommen sind allein die **Farbwerte**, aus unseren eigenen
Bildschirmfotos gemessen (`docs/screenshots/02-basis.png`): sie machen den
Farbklang des Spiels aus, und ein paar RGB-Zahlen sind keine Grafik.

Aufruf:
    python packaging/make_icon.py
"""
from PIL import Image, ImageDraw
import os

S = 8                      # Überabtastung
GROESSEN = [16, 24, 32, 48, 64, 128, 256]

# --- die Farben, gemessen an der Übersichtskarte des Spiels -----------------
WASSER = (18, 37, 51)          # das dunkle Blaugrau des Meeres
WASSER_HELL = (28, 54, 72)     # die Kante zum Ufer
LAND = (47, 87, 0)             # das Grün der Wiese
LAND_DUNKEL = (29, 47, 7)      # Wald
LAND_TROCKEN = (83, 75, 7)     # der ockerfarbene Streifen
RAHMEN_HELL = (140, 148, 153)  # das Grau der Bedienleiste
RAHMEN_DUNKEL = (52, 56, 60)
GRUND = (12, 14, 16)
AUSSCHNITT = (236, 240, 236)   # der helle Rahmen des Kartenausschnitts
PARTEI_A = (228, 64, 48)       # rot
PARTEI_B = (232, 196, 72)      # bernstein, die Farbe der Schrift


def zeichne(px):
    """Ein Bild der Kantenlaenge px, ueberabgetastet gezeichnet."""
    n = px * S
    bild = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(bild)
    r = int(n * 0.17)

    # --- die Platte ---------------------------------------------------------
    maske = Image.new("L", (n, n), 0)
    ImageDraw.Draw(maske).rounded_rectangle([0, 0, n - 1, n - 1], radius=r, fill=255)

    innen = Image.new("RGBA", (n, n), GRUND + (255,))
    di = ImageDraw.Draw(innen)

    # --- das WASSER füllt die ganze Karte, das Land liegt darauf ------------
    # ⚠ Mit einem leichten Verlauf statt flach. Eine gleichmässige Fläche wirkt
    # bei 256 px wie ein Farbeimer; der Verlauf ist so schwach, dass er bei
    # 16 px nichts kostet, gibt dem grossen Symbol aber Tiefe.
    for y in range(n):
        t = y / max(n - 1, 1)
        di.line([(0, y), (n, y)], fill=(
            int(WASSER[0] * (0.72 + 0.42 * t)),
            int(WASSER[1] * (0.72 + 0.42 * t)),
            int(WASSER[2] * (0.72 + 0.42 * t)), 255))

    # ⚠ Die Küstenlinie ist EINE grosse Diagonale, keine gezackte Küste. Eine
    # naturgetreue Küste zerfällt bei 16 px in Rauschen; eine klare Schräge
    # bleibt bei jeder Grösse dieselbe Aussage: hier Wasser, dort Land.
    #
    # ⚠ Und sie läuft durch die MITTE. Der erste Entwurf schob das Land in die
    # Ecke — dann ist das Symbol zu vier Fünfteln dunkelblau und liest sich als
    # leere Platte. Ungefähr halbe/halbe ist das, was eine Karte ausmacht.
    land = [
        (n * -0.05, n * 0.86),
        (n * 0.38, n * 0.52),
        (n * 0.72, n * 0.26),
        (n * 1.05, n * 0.02),
        (n * 1.05, n * 1.05),
        (n * -0.05, n * 1.05),
    ]
    landmaske = Image.new("L", (n, n), 0)
    ImageDraw.Draw(landmaske).polygon(land, fill=255)
    landbild = Image.new("RGBA", (n, n))
    dl = ImageDraw.Draw(landbild)
    for y in range(n):
        t = y / max(n - 1, 1)
        dl.line([(0, y), (n, y)], fill=(
            int(LAND[0] * (1.18 - 0.46 * t)),
            int(LAND[1] * (1.18 - 0.46 * t)),
            int(LAND[2] * (1.18 - 0.46 * t)) + int(10 * (1 - t)), 255))
    innen.paste(landbild, (0, 0), landmaske)
    di.line([land[0], land[1], land[2], land[3]],
            fill=WASSER_HELL + (255,), width=max(1, int(n * 0.028)))

    if px >= 24:
        # Wald als dunklere Fläche in der unteren Ecke
        di.polygon([(n * 0.34, n * 1.05), (n * 0.62, n * 0.66),
                    (n * 1.05, n * 0.40), (n * 1.05, n * 1.05)],
                   fill=(LAND_DUNKEL[0] - 6, LAND_DUNKEL[1] - 8, LAND_DUNKEL[2], 255))
        # ein Fluss, der ins Meer laeuft — im Spiel durchziehen sie jede Karte
        di.line([(n * 1.02, n * 0.74), (n * 0.78, n * 0.80), (n * 0.60, n * 0.70),
                 (n * 0.42, n * 0.74), (n * 0.24, n * 0.62)],
                fill=WASSER_HELL + (255,), width=max(1, int(n * 0.032)), joint="curve")
        # ein trockener Streifen am Ufer — der Ockerton der Karte
        di.line([(n * 0.06, n * 0.80), (n * 0.44, n * 0.50), (n * 0.86, n * 0.16)],
                fill=LAND_TROCKEN + (255,), width=max(1, int(n * 0.040)))

    # --- der AUSSCHNITTRAHMEN: das Zeichen der Übersichtskarte --------------
    # ⚠ Er ist der Blickfang und muss darum gross und MITTIG sein. Ein
    # originalgetreu kleines Rechteck vers ch wände bei 16 px.
    ab, ah = n * 0.44, n * 0.32
    ax, ay = (n - ab) / 2, (n - ah) / 2
    di.rectangle([ax, ay, ax + ab, ay + ah],
                 outline=AUSSCHNITT + (255,), width=max(1, int(n * 0.038)))

    if px >= 32:
        # zwei Parteien als Punkte — Farbe ist im Spiel die Partei
        pr = max(1, int(n * 0.038))
        for (cx, cy, c) in ((n * 0.63, n * 0.63, PARTEI_A),
                            (n * 0.40, n * 0.44, PARTEI_B),
                            (n * 0.80, n * 0.82, PARTEI_A)):
            di.ellipse([cx - pr, cy - pr, cx + pr, cy + pr], fill=c + (255,))

    bild.paste(innen, (0, 0), maske)

    # --- der Rahmen der Bedienleiste ----------------------------------------
    # ⚠ Hier standen zwei `arc`-Aufrufe, die eine Fase andeuten sollten. Ein
    # `arc` über das ganze Feld zeichnet aber eine ELLIPSE, kein abgerundetes
    # Rechteck — im Bild lag ein grosser grauer Kreis quer über der Karte. Am
    # Code sah es plausibel aus; gesehen hat man es erst am gerenderten Symbol.
    # ⚠ ZWEI FEHLER, beide erst am gerenderten Kleinbild sichtbar:
    #
    # 1. Der Rahmen sass INNEN (Kasten [w/2 .. n-1-w/2] bei gleichem Radius).
    #    An den ECKEN ist die Rundung des inneren Kastens dadurch enger als die
    #    der Maske — dazwischen trat das Grün des Landes aus, ein Farbsaum um
    #    jede Ecke. Der Rahmen liegt jetzt auf DERSELBEN Aussenkante wie die
    #    Maske, dann kann nichts dazwischenpassen.
    # 2. Er war zu dick. Bei 16 px frass er die halbe Karte. Die Dicke haengt
    #    jetzt an der Groesse: gross genug zum Sehen, duenn genug zum Lesen.
    w = max(1, int(n * (0.030 if px >= 32 else 0.024)))
    d.rounded_rectangle([0, 0, n - 1, n - 1],
                        radius=r, outline=RAHMEN_HELL + (255,), width=w)
    if px >= 32:
        # der dunkle Absatz nach innen — zusammen die Fase, die im Spiel jedes
        # Bedienfeld hat. Bei 16 und 24 px waere er ein Pixel und saehe aus wie
        # ein Zeichenfehler.
        d.rounded_rectangle([w, w, n - 1 - w, n - 1 - w],
                            radius=max(1, int(r * 0.8)),
                            outline=RAHMEN_DUNKEL + (230,), width=max(1, int(w * 0.55)))

    return bild.resize((px, px), Image.LANCZOS)


def main():
    hier = os.path.dirname(os.path.abspath(__file__))
    wurzel = os.path.dirname(hier)
    bilder = [zeichne(p) for p in GROESSEN]

    ico = os.path.join(hier, "AkteEuropaReborn.ico")
    bilder[-1].save(ico, format="ICO", sizes=[(p, p) for p in GROESSEN])
    print(f"{ico}  ({os.path.getsize(ico)} B, {len(GROESSEN)} Groessen)")

    png = os.path.join(wurzel, "Assets", "Textures", "Icon.png")
    bilder[-1].save(png, format="PNG")
    print(f"{png}  ({os.path.getsize(png)} B, 256x256)")


if __name__ == "__main__":
    main()
