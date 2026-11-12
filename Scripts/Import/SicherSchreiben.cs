namespace AkteEuropaReborn.Import;

using System;
using System.IO;
using System.Text;

/// <summary>
/// ⭐ 12.11.2026 — A5 (Fable-Pruefung des Karteneditors): SCHREIBEN OHNE HALBE DATEIEN.
///
/// <para>Bis 0.7.0 schrieben Editor-Speichern und <c>NetzKarte.Ablegen</c> die
/// Kartendateien und die GEMEINSAMEN Kachelsatztafeln
/// (<c>Buildings/tileset_NN*.png/json</c>, <c>_nebel.png</c>) jedes Mal direkt ans
/// Ziel. Brach das Spiel dabei ab, lag eine halbe Datei da; und ein zweiter Prozess,
/// der gerade eine Karte desselben Kachelsatzes lud, las eine Datei, die eben
/// geschrieben wurde (Hinweis H2 zum Paarabsturz).</para>
///
/// <para>Jetzt: (1) ist der Inhalt gleich dem vorhandenen, wird gar nicht
/// geschrieben; (2) sonst in <c>&lt;ziel&gt;.tmp</c> und dann in einem Zug
/// umbenannt. Gegenschalter <c>--schreiben-alt</c> (direkt, immer). UNSERE ZUTAT.</para>
/// </summary>
public static class SicherSchreiben
{
    public static bool Alt => Array.IndexOf(Core.CommandLine.Args, "--schreiben-alt") >= 0;

    /// <summary>Zahl der Schreibvorgaenge, die wegen gleichen Inhalts entfielen (Pruefzeile).</summary>
    public static int Uebersprungen;

    /// <returns>true, wenn geschrieben wurde.</returns>
    public static bool Bytes(string ziel, byte[] daten)
    {
        if (Alt) { File.WriteAllBytes(ziel, daten); return true; }
        if (Gleich(ziel, daten)) { Uebersprungen++; return false; }
        string tmp = ziel + ".tmp";
        File.WriteAllBytes(tmp, daten);
        File.Move(tmp, ziel, overwrite: true);
        return true;
    }

    public static bool Text(string ziel, string text)
        => Bytes(ziel, new UTF8Encoding(false).GetBytes(text));

    public static bool Png(string ziel, Godot.Image bild)
    {
        if (Alt) { bild.SavePng(ziel); return true; }
        return Bytes(ziel, bild.SavePngToBuffer());
    }

    private static bool Gleich(string ziel, byte[] daten)
    {
        try
        {
            var fi = new FileInfo(ziel);
            if (!fi.Exists || fi.Length != daten.Length) return false;
            return File.ReadAllBytes(ziel).AsSpan().SequenceEqual(daten);
        }
        catch (IOException) { return false; }
    }
}
