namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐ Paket W4 (22.10.2026, bug-641) — <b>DIE PRÜFZAHL DER KI-SEKTORTAFELN FÜR DAS NETZ.</b>
///
/// <para>Seit W3 rechnet die Gefechts-KI mit den Sektortafeln (Anker 0x541F60, Verbindungen
/// 0x542148, Brückensätze 0x542330). Ein Mitspieler OHNE eingelesene <c>AI5x.CWI</c> rechnet
/// sie zur Laufzeit (<see cref="KiSektorTafelnBauen"/>), der andere liest die Datei — beides
/// ist nur für NET07 als deckungsgleich geprüft. Weichen die Tafeln ab, läuft die Partie
/// auseinander, und das sähe aus wie ein Rechenfehler. Darum geht die Prüfzahl im
/// Vorlaufpaket mit (Network/NetLink.HeadStartPacket), und der Vorlauf bricht bei
/// Abweichung mit klarer Meldung ab (Network/NetGame.Bootstrap). Gegenschalter
/// <c>--net-tafelpruefung-aus</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>FNV-1a 64 über die drei Tafeln, wie sie NACH der Brückennachführung stehen.
    /// 0 heisst: es gibt (noch) keine Tafeln — dann prüft auch das Gegenüber 0.</summary>
    public ulong KiTafelPruefzahl()
    {
        if (!_kiTafelnDa) return 0;
        ulong h = 14695981039346656037UL;
        void Teil(byte[] b) { foreach (byte x in b) { h ^= x; h *= 1099511628211UL; } }
        Teil(_kiAnker); Teil(_kiVerb); Teil(_kiBrueckenSaetze);
        return h == 0 ? 1 : h;
    }

    /// <summary>Die drei Teile einzeln (Anker/Verbindungen/Brueckensaetze), fuer die NET-Liste.</summary>
    public string KiTafelTeilzahlen()
    {
        static uint F(byte[] b) { uint h = 2166136261u; foreach (byte x in b) { h ^= x; h *= 16777619u; } return h; }
        return $"Anker {F(_kiAnker):X8}, Verb {F(_kiVerb):X8}, Bruecken {F(_kiBrueckenSaetze):X8}";
    }
}
