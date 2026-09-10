namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--sieg7-check</c> — <b>endet Mission 7, wenn man tut, was sie verlangt?</b>
/// (10.09.2026, bug-167/171.)
///
/// <para>⚠⚠ Seine Meldung: »mission liess sich nicht beenden, trotz das alles
/// zerstoert«. Die Siegkette ist gelesen (Endregel @0x49B16B):
/// <c>v[102]==2 UND v[12]==2 UND obj_owner(3)==12 UND obj_owner(5)==12</c>.</para>
///
/// <para>⚠⚠⚠ <b>Der erste Wurf dieses Standes hat FALSCH GEMESSEN, und der
/// Fehler steht hier, damit er nicht wiederkommt.</b> Er schoss nach drei
/// Sekunden die Bunker ab und meldete »DURCHGEFALLEN«, weil <c>v[102]</c> noch
/// 1 war. Daraus wurde der Schluss gezogen, die Mission sei unloesbar — beides
/// war voreilig:</para>
/// <list type="number">
///   <item><b>Er stellte das ERSTE Glied nicht her.</b> <c>v[102]</c> geht nur
///   ueber Regel 9 (@0x49AD51) von 1 auf 2, und die haengt an einer Kette, die
///   der SPIELER anstoesst: <c>v[221]</c> setzen die Regeln 5/6, wenn Com.Wiffer
///   (Satz 3000) BESCHAEDIGT wird (@0x49AC5A) oder eine eigene Einheit auf
///   (14,22) tritt. Dann verkauft das Skript Wiffer, Regel 7 setzt <c>v[1]</c>
///   auf 1, Regel 8 zaehlt vierzig Takte, Regel 9 hebt <c>v[102]</c>.</item>
///   <item><b>Er mass zu frueh.</b> In einem natuerlichen Lauf beschiesst
///   Spieler 1 den Wiffer nach etwa 53 Sekunden von selbst — dann kommt Text
///   170 und <c>v[102]</c> wird 2. Wer nach 6 Sekunden urteilt, sieht das nie.
///   ⚠ Schlimmer noch: schiesst man die Bunker frueh ab, kommt Spieler 1 gar
///   nicht mehr nach Norden und der Verkauf bleibt AUS — der Pruefstand hat
///   sich seine eigene Voraussetzung zerstoert.</item>
/// </list>
///
/// <para>Darum jetzt in der Reihenfolge, die auch der Spieler geht: erst
/// Wiffer anschiessen (das erste Glied), dann warten, bis <c>v[102]</c> von
/// selbst auf 2 steht, DANN die Bunker abschiessen, und erst danach urteilen.
/// ⚠ Ein Sieg loest die NACHFRIST aus (<c>Grace</c>), weil Mission 7 offene
/// Untermissionen hat — <c>Grace &gt;= 0</c> zaehlt darum als erreicht.</para>
///
/// <para>Gegenschalter <c>--sieg7-ohne-wiffer</c>: laesst das erste Glied weg
/// und muss dann durchfallen — das ist das Nullmodell.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private int _sieg7 = -1;
    private float _sieg7Uhr;
    private string _sieg7Text = "";
    private readonly System.Text.StringBuilder _sieg7Sb = new();

    /// <summary><c>--sieg7-ohne-wiffer</c> — das Nullmodell: ohne das erste
    /// Glied der Kette darf die Mission nicht enden.</summary>
    public static bool Sieg7OhneWiffer;

    /// <summary>Wie lange auf <c>v[102] == 2</c> gewartet wird, bevor der Stand
    /// aufgibt. ⚠ UNSERE Zahl, und sie ist grosszuegig: die Kette braucht
    /// vierzig Takte, aber der Verkauf haengt an einem Skripttakt.</summary>
    private const float Sieg7Geduld = 30f;

    public void Sieg7Start() { _sieg7 = 0; _sieg7Uhr = 0f; _sieg7Sb.Clear(); }

    public string Sieg7Line() => _sieg7Text;

    public void Sieg7Tick(float dt)
    {
        if (_sieg7 < 0 || _mscript == null) return;
        _sieg7Uhr += dt;

        switch (_sieg7)
        {
            // ---- 0: das Skript anlaufen lassen -----------------------------
            case 0:
                if (_sieg7Uhr < 2f) return;
                _sieg7Sb.Append("sieg7-check\n");
                _sieg7Sb.Append($"  Start: v[102]={_mscript.VarAt(102)} v[12]={_mscript.VarAt(12)} "
                              + $"obj_owner(3)={_mscript.ObjOwner!(3)} "
                              + $"obj_owner(5)={_mscript.ObjOwner!(5)}\n");
                _sieg7 = 1;
                _sieg7Uhr = 0f;
                return;

            // ---- 1: das ERSTE GLIED — Com.Wiffer beschaedigen --------------
            case 1:
            {
                if (Sieg7OhneWiffer)
                {
                    _sieg7Sb.Append("  --sieg7-ohne-wiffer: das erste Glied wird ausgelassen\n");
                    _sieg7 = 2;
                    _sieg7Uhr = 0f;
                    return;
                }
                int wi = -1;
                for (int i = 0; i < _entities.Count; i++)
                    if (!_entities[i].IsBuilding && !_entities[i].Dead && _entities[i].Slot == 3000)
                    { wi = i; break; }
                if (wi < 0)
                {
                    _sieg7Sb.Append("  Satz 3000 (Com.Wiffer) nicht auf der Karte "
                                  + "— der Lauf sagt NICHTS\n");
                    _sieg7 = 4;
                    return;
                }
                var w = _entities[wi];
                // ⚠ NUR BESCHAEDIGEN, nicht toeten: Regel 5/6 fragt
                // `+0x29 > +0x08` (@0x49AC5A), also »hat Schaden genommen«.
                ApplyHit(-1, wi, w, Mathf.Max(1, w.Hp / 2));
                _sieg7Sb.Append($"  Com.Wiffer (Satz 3000) beschaedigt: TP {w.Hp}/{w.HpMax}\n");
                _sieg7 = 2;
                _sieg7Uhr = 0f;
                return;
            }

            // ---- 2: warten, bis v[102] von selbst auf 2 steht --------------
            case 2:
                if (_mscript.VarAt(102) == 2)
                {
                    _sieg7Sb.Append($"  v[102] steht nach {_sieg7Uhr:0.0} s auf 2 "
                                  + "(Regel 5/6 -> 7 -> 8 -> 9)\n");
                    _sieg7 = 3;
                    _sieg7Uhr = 0f;
                    return;
                }
                if (_sieg7Uhr < Sieg7Geduld) return;
                _sieg7Sb.Append($"  ⚠ v[102] steht nach {Sieg7Geduld:0} s immer noch auf "
                              + $"{_mscript.VarAt(102)} — die Kette laeuft nicht an\n");
                _sieg7 = 3;
                _sieg7Uhr = 0f;
                return;

            // ---- 3: jetzt die Bunker ---------------------------------------
            case 3:
            {
                foreach (int platz in _mscript.WatchedSlots())
                {
                    int vi = -1;
                    for (int i = 0; i < _entities.Count; i++)
                        if (_entities[i].IsBuilding && _entities[i].Slot == platz) { vi = i; break; }
                    if (vi < 0) { _sieg7Sb.Append($"  Platz {platz}: kein Satz\n"); continue; }
                    var e = _entities[vi];
                    ApplyHit(-1, vi, e, Mathf.Max(1, e.HpMax));
                    _sieg7Sb.Append($"  Platz {platz} abgeschossen -> obj_owner "
                                  + $"{_mscript.ObjOwner!(platz)}\n");
                }
                _sieg7 = 4;
                _sieg7Uhr = 0f;
                return;
            }

            // ---- 4: Rechenschaft -------------------------------------------
            case 4:
            {
                if (_sieg7Uhr < 4f) return;
                _sieg7 = 5;
                _sieg7Sb.Append($"  Ende: v[102]={_mscript.VarAt(102)} v[12]={_mscript.VarAt(12)} "
                              + $"obj_owner(3)={_mscript.ObjOwner!(3)} "
                              + $"obj_owner(5)={_mscript.ObjOwner!(5)}\n");
                bool ende = _mscript.Ended;
                bool nachfrist = _mscript.Grace >= 0;
                _sieg7Sb.Append($"  Skript beendet: {ende}"
                              + (ende ? $", gewonnen: {_mscript.Success}" : "")
                              + $" | Nachfrist: {(nachfrist ? _mscript.Grace.ToString() : "keine")}\n");
                // ⚠ Ein Sieg loest bei offenen Untermissionen zuerst die
                // NACHFRIST aus — die zaehlt hier als erreicht.
                bool ok = (ende && _mscript.Success) || nachfrist;
                _sieg7Sb.Append($"  Gegenschalter --sieg7-ohne-wiffer: {Sieg7OhneWiffer}\n");
                _sieg7Sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
                _sieg7Text = _sieg7Sb.ToString();
                GD.Print(_sieg7Text);
                return;
            }
        }
    }

    /// <summary>0 bestanden, 1 durchgefallen. ⚠ Ein Lauf, der nichts messen
    /// konnte, gibt 0 und sagt es in seiner Zeile.</summary>
    public int Sieg7Rc()
        => _sieg7Text.Length == 0 ? 0 : _sieg7Text.Contains("BESTANDEN") ? 0 : 1;
}
