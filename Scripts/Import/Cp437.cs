namespace AkteEuropaReborn.Import;

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
}
