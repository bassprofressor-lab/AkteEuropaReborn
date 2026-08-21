namespace AkteEuropaReborn.Simulation;

using System.Collections.Generic;

/// <summary>
/// <b>»Besteht eine Verbindung zu diesem Gebäude?«</b> — die Flutsuche des
/// Originals, gelesen am 20.08.2026 aus C <c>0x4CE710</c> / F <c>0x4CE2C0</c>.
///
/// <para>Sie hängt am Depotknopf <b>»Transportieren«</b> (Fensterart 6,
/// Element 6). Schlägt sie fehl, kommt die Zeile »Es besteht keine Verbindung
/// zu diesem Gebäude« (C <c>0x4FBBDC</c>); gelingt sie, wird je ausgewählter
/// Einheit <b>Befehl 0x206</b> <c>(Einheit, Quelle, Ziel)</c> abgeschickt.</para>
///
/// <para><b>Was die Kanten sind:</b> die <b>SPOJ-Bahnlinien</b>
/// (<c>0xA89220</c>, 80 Sätze zu 214), deren <c>+0x00</c>/<c>+0x01</c> die zwei
/// Knoten nennen. <b>Keine</b> Nachbarschaft, <b>keine</b> Strasse, <b>keine</b>
/// Entfernung — nur wer mit wem durch ein Gleis verbunden ist.</para>
///
/// <para><b>Die Knoten</b> stehen in der Tafel <c>0xA8D508</c> (F
/// <c>0xA8C568</c>), 120 Sätze zu 8: <c>+0x00</c> u16 Gebäude, <c>+0x02</c>
/// Gebäudetyp (<b>0 = Satz frei</b>), <c>+0x03..+0x06</c> die vier
/// Liniennummern (0xFF leer). Das Gebäude trägt seine Knotennummer auf
/// <c>+0x1A</c>, 0xFF heisst »kein Knoten«.</para>
///
/// <para><b>Die Bedingungen je Kante</b>, aus der Routine: die Linie ist
/// belegt, ihre <c>faze != 3</c>, beide Knotengebäude haben <b>denselben
/// Besitzer</b>, und der Knotentyp ist nicht 0.</para>
///
/// <para>⚠ <b>Im Original entsteht im Spiel kein neues Gleis.</b>
/// <c>AllocLine</c> (C <c>0x4AFC70</c>) hat genau einen Aufrufer, und der
/// (C <c>0x4AFCF0</c>) ist toter Code — 0 Aufrufe, 0 Zeigervorkommen. Das Netz
/// kommt als Rohblock aus der Karte und wird nie aufgefrischt. Wer hier eine
/// Auffrischung vermisst, sucht etwas, das es nicht gibt.</para>
/// </summary>
public static class RailNetwork
{
    /// <summary>Ein Knoten der Tafel. <see cref="Type"/> 0 heisst: Satz frei.</summary>
    public struct Node
    {
        public int Building, Type;
        public int[] Links;          // vier Liniennummern, -1 = leer
    }

    /// <summary>Eine Linie: ihre zwei Knoten und ihre Phase.</summary>
    public struct Line
    {
        public int Node1, Node2, Faze;
        public bool Used;
    }

    /// <summary>
    /// <b>DIE ZWEI SCHRANKEN DES ORIGINALS</b>, und sie sind beide Fehler.
    ///
    /// <para><b>1. Nur die Knoten 0…39.</b> Anleger und Flut prüfen
    /// <c>cmp al,0x28</c> — in beiden Bauten. Die Tafel hat aber <b>120</b>
    /// Plätze, und der Lader liest <c>fread(…, 0x3C0)</c> = 960 B = 120×8.
    /// Über 34 Karten gezählt liegen <b>123 von 826</b> Knoten jenseits 39
    /// (NET05 allein hat 77 Knoten).</para>
    ///
    /// <para><b>2. Nur drei der vier Anschlüsse.</b> Die Flut läuft
    /// <c>cmp cl,3</c>, liest also <c>+0x03..+0x05</c> und lässt
    /// <c>+0x06</c> liegen. <b>90 Knoten</b> haben genau diesen vierten
    /// Anschluss belegt.</para>
    ///
    /// <para>Beides zusammen heisst: auf grossen Karten bleiben Verbindungen
    /// unauffindbar, die es gibt. ⚠ <b>In der Kampagne bleibt das so</b> —
    /// sie soll sich verhalten wie das Original, Fehler eingeschlossen. Im
    /// <b>Gefecht</b> fallen beide Schranken, weil dort nicht Originaltreue
    /// zählt, sondern dass zwei Spieler dieselben Möglichkeiten haben.
    /// Entscheidung des Spielers vom 20.08.2026, wörtlich: »kampagne mit
    /// schranke, gefecht ohne«.</para>
    /// </summary>
    public const int OriginalNodeLimit = 40;

    /// <summary>Wieviele Anschlüsse die Flut des Originals liest (von vier).</summary>
    public const int OriginalLinkLimit = 3;

    /// <summary>Ist gerade eine Kampagnenmission geladen? Dann gelten die zwei
    /// Schranken. ⚠ <c>CampaignMission == 0</c> heisst Gefecht — so setzt es
    /// auch <c>NetworkManager</c> (»Netz ist immer Gefecht«).</summary>
    public static bool CampaignRules => UI.SkirmishSetup.CampaignMission > 0;

    /// <summary>
    /// Sucht einen Weg von <paramref name="fromNode"/> nach
    /// <paramref name="toNode"/>. Gibt die Knotenfolge zurück (Start zuerst,
    /// Ziel zuletzt) oder <c>null</c>, wenn keine Verbindung besteht.
    ///
    /// <para>Breitensuche mit Rückverfolgung, wie im Original: es sucht den Weg
    /// mit den <b>wenigsten Umstiegen</b>, nicht den kürzesten in Metern —
    /// Entfernungen kommen in der Routine überhaupt nicht vor.</para>
    ///
    /// <para>⚠ <paramref name="strict"/> schaltet die zwei Schranken oben ein.
    /// Der Aufrufer soll <see cref="CampaignRules"/> übergeben und nicht selbst
    /// entscheiden — sonst driften Kampagne und Gefecht an zwei Stellen
    /// auseinander statt an einer.</para>
    /// </summary>
    public static List<int>? FindRoute(
        IReadOnlyDictionary<int, Node> nodes,
        IReadOnlyDictionary<int, Line> lines,
        int fromNode, int toNode, int owner, bool strict)
    {
        if (fromNode < 0 || toNode < 0) return null;
        if (!nodes.ContainsKey(fromNode) || !nodes.ContainsKey(toNode)) return null;
        int grenze = strict ? OriginalNodeLimit : int.MaxValue;
        int anschluesse = strict ? OriginalLinkLimit : 4;
        if (fromNode >= grenze || toNode >= grenze) return null;
        if (fromNode == toNode) return new List<int> { fromNode };

        var vorher = new Dictionary<int, int> { [fromNode] = -1 };
        var welle = new Queue<int>();
        welle.Enqueue(fromNode);
        while (welle.Count > 0)
        {
            int cur = welle.Dequeue();
            if (!nodes.TryGetValue(cur, out var n) || n.Links == null) continue;
            for (int c = 0; c < anschluesse && c < n.Links.Length; c++)
            {
                int slot = n.Links[c];
                if (slot < 0) continue;
                if (!lines.TryGetValue(slot, out var l)) continue;
                // Die Bedingungen der Routine, in ihrer Reihenfolge.
                if (!l.Used || l.Faze == 3) continue;
                // ⚠ Die Linie muss WIRKLICH an diesem Knoten haengen. Der
                // Anschluss kann auf eine Linie zeigen, die den Knoten gar
                // nicht nennt — dann waere `l.Node1` als »das andere Ende«
                // schlicht falsch, und die Suche liefe ueber eine Kante, die
                // es nicht gibt.
                if (l.Node1 != cur && l.Node2 != cur) continue;
                int other = l.Node1 == cur ? l.Node2 : l.Node1;
                if (other < 0 || other >= grenze) continue;
                if (vorher.ContainsKey(other)) continue;
                if (!nodes.TryGetValue(other, out var o)) continue;
                // ⚠ `typ != 0` UND gleicher Besitzer — beide stehen in der
                // Routine, und ohne den Besitzer liefe ein Transport ueber
                // fremde Bahnhoefe.
                if (o.Type == 0) continue;
                if (owner >= 0 && !SameOwner(o.Building, owner)) continue;
                vorher[other] = cur;
                if (other == toNode) return Weg(vorher, toNode);
                welle.Enqueue(other);
            }
        }
        return null;
    }

    /// <summary>Wer den Besitzer eines Gebäudes kennt, hängt sich hier ein.
    /// ⚠ Als Feld und nicht als Parameter, damit <see cref="FindRoute"/> ohne
    /// den ganzen Kartenzustand geprüft werden kann.</summary>
    public static System.Func<int, int>? OwnerOf;

    private static bool SameOwner(int building, int owner)
        => OwnerOf == null || OwnerOf(building) == owner;

    private static List<int> Weg(Dictionary<int, int> vorher, int ziel)
    {
        var w = new List<int>();
        for (int k = ziel; k >= 0; k = vorher[k]) { w.Add(k); if (vorher[k] < 0) break; }
        w.Reverse();
        return w;
    }
}
