namespace AkteEuropaReborn.UI
{
    using Godot;

    /// <summary>
    /// <c>--bertha-demo-check=N[,S]</c> (KayelGee 14.10., Meldung 15: »In einer der
    /// Dicke-Bertha-Demos laufen Einheiten von der Dicken Bertha weg, anstatt zum Angriff
    /// auf sie zu.«). Spielt die Menue-Demo N wie das Menue (StartCampaign(-1), alle Seiten
    /// mit KI) mit fester Bildzeit 1/50 s ueber ihre Laufzeit (oder S Sekunden) und misst
    /// je Sekunde den Abstand jeder feindlichen Einheit im Umkreis von 20 Zellen zur Bertha,
    /// dazu Auftrag/Ziel/Fahrt am Anfang und am Ende. Kein Fix, nur Messung.
    /// </summary>
    public partial class MenuBackdrop
    {
        /// <summary><c>--demo-ki-alle</c> — Stand 0.6.9: jede Seite mit Hauptquartier
        /// bekommt in der Menue-Demo die KI, auch der Menschplatz 0. bug-554.</summary>
        public static bool DemoKiAlle;

        /// <summary>Wie viele Demos seit dem Programmstart geladen wurden (WinMain 0x41511B
        /// setzt Spieler 0 nur fuer die allererste auf Rechner).</summary>
        public static int DemosSeitStart;

        public static int BerthaDemoNr;
        public static float BerthaDemoSekunden;

        private void BerthaDemoMessen(int demoNr)
        {
            if (_entities == null || demoNr != BerthaDemoNr) return;
            _entities.SetProcess(false);
            float dauer = BerthaDemoSekunden > 0f ? BerthaDemoSekunden : _leftSoll;
            int bilder = Mathf.RoundToInt(dauer * 50f);
            _entities.BerthaDemoSonde(0f);
            for (int i = 1; i <= bilder; i++)
            {
                _entities._Process(1.0 / 50.0);
                if (i % 50 == 0) _entities.BerthaDemoSonde(i / 50f);
            }
            GD.Print(_entities.BerthaDemoBericht(demoNr, dauer));
            GetTree().Quit(0);
        }
    }
}
