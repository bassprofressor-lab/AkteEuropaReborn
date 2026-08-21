"""Das Programmsymbol zeichnen — als .ico für Windows und als .png für Godot.

⚠ WARUM PROGRAMMIERT UND NICHT GEMALT: ein Symbol muss bei **16 px** noch
lesbar sein, und das entscheidet sich nicht am schönen 256er. Alles hier ist
darum aus wenigen grossen Flächen gebaut, mit einer einzigen kräftigen Farbe
als Blickfang. Feine Linien und Farbverläufe verschwinden bei 16 px zu Matsch —
was hier steht, ist bei jeder Grösse dieselbe Form.

Gezeichnet wird 8-fach überabgetastet und dann heruntergerechnet; das gibt
saubere Kanten ohne eine Zeichenbibliothek für Kantenglättung.

⚠ UNSERE ZUTAT, vollständig: das Original von 1997 hat ein eigenes Symbol, das
wir NICHT verwenden — es gehört uns nicht, und der Nachbau liefert grundsätzlich
kein Byte des Originals mit. Die Form (Sichtschlitz eines Kampflaufers) ist eine
Anspielung, keine Kopie.

Aufruf:
    python packaging/make_icon.py
"""
from PIL import Image, ImageDraw
import os

S = 8                      # Überabtastung
GROESSEN = [16, 24, 32, 48, 64, 128, 256]

# Die Farben sind die des Spiels, nicht frei gewählt: Grund und Bernstein
# stammen aus BriefingScreen/CreditsScreen.
GRUND_OBEN = (18, 22, 28)
GRUND_UNTEN = (5, 7, 10)
STAHL = (74, 86, 102)
STAHL_DUNKEL = (42, 51, 64)
BERNSTEIN = (230, 204, 89)
BERNSTEIN_HELL = (255, 240, 170)
RAHMEN = (58, 70, 86)


def zeichne(px):
    """Ein Bild der Kantenlaenge px, ueberabgetastet gezeichnet."""
    n = px * S
    bild = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(bild)

    # --- Grundplatte, abgerundet, mit senkrechtem Verlauf -------------------
    r = int(n * 0.18)
    platte = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    ImageDraw.Draw(platte).rounded_rectangle([0, 0, n - 1, n - 1], radius=r,
                                             fill=(255, 255, 255, 255))
    verlauf = Image.new("RGBA", (n, n))
    vd = ImageDraw.Draw(verlauf)
    for y in range(n):
        t = y / max(n - 1, 1)
        vd.line([(0, y), (n, y)], fill=(
            int(GRUND_OBEN[0] + (GRUND_UNTEN[0] - GRUND_OBEN[0]) * t),
            int(GRUND_OBEN[1] + (GRUND_UNTEN[1] - GRUND_OBEN[1]) * t),
            int(GRUND_OBEN[2] + (GRUND_UNTEN[2] - GRUND_OBEN[2]) * t), 255))
    bild.paste(verlauf, (0, 0), platte)

    # dünner Rahmen — gibt dem Symbol auf hellem Untergrund eine Kante
    d.rounded_rectangle([0, 0, n - 1, n - 1], radius=r,
                        outline=RAHMEN + (255,), width=max(1, int(n * 0.012)))

    # --- DER KOPF eines Kampflaeufers ---------------------------------------
    # ⚠ Hier stand zuerst ein schlichtes Trapez, oben breiter. Es las sich als
    # EIMER. Ein Symbol wird nicht dadurch gut, dass die Bauteile stimmen — es
    # muss auf den ersten Blick das Richtige sein, und das sieht man erst am
    # gerenderten Bild. Jetzt ist es achteckig mit abgeschraegtem Kinn.
    cx, cy = n * 0.50, n * 0.53
    W, H = n * 0.30, n * 0.27                    # halbe Breite / Hoehe
    kopf = [
        (cx - 0.66 * W, cy - 1.00 * H),
        (cx + 0.66 * W, cy - 1.00 * H),
        (cx + 1.00 * W, cy - 0.42 * H),
        (cx + 0.86 * W, cy + 0.48 * H),
        (cx + 0.42 * W, cy + 1.00 * H),
        (cx - 0.42 * W, cy + 1.00 * H),
        (cx - 0.86 * W, cy + 0.48 * H),
        (cx - 1.00 * W, cy - 0.42 * H),
    ]
    d.polygon(kopf, fill=STAHL_DUNKEL + (255,))

    # Lichtkante oben und links — gibt der Flaeche eine Richtung
    lw = max(1, int(n * 0.030))
    d.line([kopf[0], kopf[1]], fill=STAHL + (255,), width=lw)
    d.line([kopf[7], kopf[0]], fill=STAHL + (255,), width=lw)
    d.line([kopf[6], kopf[7]], fill=STAHL + (255,), width=max(1, int(n * 0.020)))

    # --- Der SICHTSCHLITZ: der Blickfang ------------------------------------
    # ⚠ Er sitzt im oberen Drittel, nicht mittig: mittig sieht ein Schlitz aus
    # wie ein durchgestrichenes Feld.
    sy = cy - H * 0.30
    sh = H * 0.34
    sb = W * 1.52
    d.polygon([(cx - sb / 2, sy - sh / 2), (cx + sb / 2, sy - sh / 2),
               (cx + sb / 2 - sh * 0.35, sy + sh / 2),
               (cx - sb / 2 + sh * 0.35, sy + sh / 2)],
              fill=BERNSTEIN + (255,))
    # heller Kern oben — bei 16 px verschmilzt er, bei 256 gibt er Tiefe
    d.rectangle([cx - sb / 2 + sh * 0.10, sy - sh / 2,
                 cx + sb / 2 - sh * 0.10, sy - sh * 0.10],
                fill=BERNSTEIN_HELL + (255,))

    # --- Das KINN: eine dunkle Kerbe, damit der Kopf nicht als Klotz liest ---
    if px >= 24:
        kb = W * 0.46
        d.polygon([(cx - kb, cy + 0.52 * H), (cx + kb, cy + 0.52 * H),
                   (cx + kb * 0.66, cy + 1.00 * H), (cx - kb * 0.66, cy + 1.00 * H)],
                  fill=(GRUND_UNTEN[0] + 6, GRUND_UNTEN[1] + 8, GRUND_UNTEN[2] + 10, 255))

    # --- Zielklammern in den Ecken ------------------------------------------
    # ⚠ Nur ab 32 px: bei 16 px sind sie ein Pixel und sehen aus wie Dreck.
    if px >= 32:
        k = n * 0.13          # Schenkellänge
        m = n * 0.085         # Abstand vom Rand
        w = max(1, int(n * 0.022))
        for sx, sgx in ((m, 1), (n - m, -1)):
            for sy2, sgy in ((m, 1), (n - m, -1)):
                d.line([(sx, sy2), (sx + k * sgx, sy2)], fill=STAHL + (255,), width=w)
                d.line([(sx, sy2), (sx, sy2 + k * sgy)], fill=STAHL + (255,), width=w)

    return bild.resize((px, px), Image.LANCZOS)


def main():
    hier = os.path.dirname(os.path.abspath(__file__))
    wurzel = os.path.dirname(hier)
    bilder = [zeichne(p) for p in GROESSEN]

    ico = os.path.join(hier, "AkteEuropaReborn.ico")
    bilder[-1].save(ico, format="ICO",
                    sizes=[(p, p) for p in GROESSEN])
    print(f"{ico}  ({os.path.getsize(ico)} B, {len(GROESSEN)} Groessen)")

    png = os.path.join(wurzel, "Assets", "Textures", "Icon.png")
    bilder[-1].save(png, format="PNG")
    print(f"{png}  ({os.path.getsize(png)} B, 256x256)")


if __name__ == "__main__":
    main()
