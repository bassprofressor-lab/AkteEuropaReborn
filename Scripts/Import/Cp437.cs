namespace AkteEuropaReborn.Import;

using System.Collections.Generic;
using System.Text;

/// <summary>
/// Code page 437 — the encoding every string inside the 1997 game uses.
///
/// It matters: read as Latin-1 the German names come out mutilated
/// ("Flak-Geschtz" instead of "Flak-Geschütz", "Wstenwiesel" instead of
/// "Wüstenwiesel"), because cp437 puts ü at 0x81 where Latin-1 has a control
/// character. .NET does not carry cp437 without registering the extra encoding
/// provider, so the high half is spelled out here.
/// </summary>
public static class Cp437
{
    /// <summary>Characters for bytes 0x80..0xFF.</summary>
    private const string High =
        "ÇüéâäàåçêëèïîìÄÅ" +
        "ÉæÆôöòûùÿÖÜ¢£¥₧ƒ" +
        "áíóúñÑªº¿⌐¬½¼¡«»" +
        "░▒▓│┤╡╢╖╕╣║╗╝╜╛┐" +
        "└┴┬├─┼╞╟╚╔╩╦╠═╬╧" +
        "╨╤╥╙╘╒╓╫╪┘┌█▄▌▐▀" +
        "αßΓπΣσµτΦΘΩδ∞φε∩" +
        "≡±≥≤⌠⌡÷≈°∙·√ⁿ²■ ";

    /// <summary>One byte as the character it stands for — what a bitmap font
    /// needs to give its glyphs a code point.</summary>
    public static char Char(byte b) => b < 0x80 ? (char)b : High[b - 0x80];

    /// <summary>Decode up to the first zero byte.</summary>
    public static string GetString(byte[] b, int at, int len)
    {
        var sb = new StringBuilder(len);
        for (int i = at; i < at + len && i < b.Length; i++)
        {
            byte x = b[i];
            if (x == 0) break;
            if (x < 0x20) continue;                 // control bytes carry no text
            sb.Append(x < 0x80 ? (char)x : High[x - 0x80]);
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Der RÜCKWEG: ein Zeichen als das Byte, für das es steht — 0 wenn die
    /// Tafel es nicht kennt.
    ///
    /// <para>⚠ Angelegt am 14.08.2026, weil diese Klasse bis dahin nur lesen
    /// konnte. Der Karteneditor schreibt seit dem Rückweg (<c>MapOpen</c>) auch
    /// wieder Namen in eine <c>CwmFile</c>, und ohne einen Schreiber hier führte
    /// er die Umkehrtafel ein zweites Mal. Eine gemessene Tafel zweimal im Baum
    /// ist eine Falle: berichtigt jemand die eine, läuft die andere still
    /// daneben. Sie wird hier aus <see cref="High"/> selbst aufgebaut, kann also
    /// gar nicht davon abweichen.</para>
    /// </summary>
    public static byte Byte(char c)
    {
        if (c < 0x80) return (byte)c;
        _back ??= Build();
        return _back.TryGetValue(c, out byte b) ? b : (byte)0;
    }

    private static Dictionary<char, byte>? _back;

    private static Dictionary<char, byte> Build()
    {
        var d = new Dictionary<char, byte>(High.Length);
        for (int i = 0; i < High.Length; i++) d[High[i]] = (byte)(0x80 + i);
        return d;
    }

    /// <summary>Eine Zeichenkette in ein Feld fester Länge, mit Null aufgefüllt
    /// — so, wie die Sätze der <c>.CWM</c> ihre Namen tragen.</summary>
    public static void PutString(string s, byte[] dst, int at, int len)
    {
        for (int i = 0; i < len; i++)
            dst[at + i] = i < s.Length ? Byte(s[i]) : (byte)0;
    }
}
