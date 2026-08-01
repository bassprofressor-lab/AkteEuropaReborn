namespace AkteEuropaReborn.Legacy;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Godot;
using AkteEuropaReborn.Core.Math;
using AkteEuropaReborn.Simulation.Components;

public static class LegacyReaders
{
    public const int CwpFrameWidth = 512;
    private const int CwpSizeTableOffset = 0x480;
    private const int CwpFrameHeaderSize = 8;

    public static string CwpMagic => "CWP";

    private static byte[] ReadResBytes(string path)
    {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
            throw new System.IO.FileNotFoundException($"Cannot open {path}: {Godot.FileAccess.GetOpenError()}");
        return file.GetBuffer((long)file.GetLength());
    }

    public static bool IsCwp(byte[] data)
        => data.Length >= 4 && data[0] == (byte)'C' && data[1] == (byte)'W' && data[2] == (byte)'P';

    public static int GetCwpFrameCount(byte[] data)
    {
        int count = 0;
        int offset = CwpSizeTableOffset;
        while (offset + 4 <= data.Length)
        {
            uint size = BitConverter.ToUInt32(data, offset);
            if (size == 0) break;
            count++;
            offset += 4;
        }
        return count;
    }

    public static int[] GetCwpFrameSizes(byte[] data)
    {
        var sizes = new List<int>();
        int offset = CwpSizeTableOffset;
        while (offset + 4 <= data.Length)
        {
            uint size = BitConverter.ToUInt32(data, offset);
            if (size == 0) break;
            sizes.Add((int)size);
            offset += 4;
        }
        return sizes.ToArray();
    }

    private static int ReadUInt32Le(byte[] data, int offset)
        => BitConverter.ToInt32(data, offset);

    private static void DecodeWestwoodRle(byte[] data, int start, int end, out byte[] pixels)
    {
        int len = Math.Min(end, data.Length) - start;
        if (len < 0) len = 0;
        pixels = new byte[len];
        Array.Copy(data, start, pixels, 0, len);
    }

    private static Image PixelsToImage(byte[] pixels, int width, int height, Color[] palette)
    {
        var rgba = new byte[pixels.Length * 4];
        for (int p = 0; p < pixels.Length; p++)
        {
            int idx = pixels[p];
            Color c = idx < palette.Length ? palette[idx] : new Color(1, 0, 1, 1);
            rgba[p * 4 + 0] = (byte)(c.R * 255f);
            rgba[p * 4 + 1] = (byte)(c.G * 255f);
            rgba[p * 4 + 2] = (byte)(c.B * 255f);
            rgba[p * 4 + 3] = (byte)(c.A * 255f);
        }
        return Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
    }

    public static Image DecodeCwp(string path, Color[] palette) { var data = ReadResBytes(path); return DecodeCwp(data, palette); }
    public static Image DecodeCwp(byte[] data, Color[] palette)
    {
        var frames = DecodeCwpFrames(data, palette);
        return frames.Length > 0 ? frames[0] : Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
    }

    public static Image[] DecodeCwpFrames(string path, Color[] palette) { var data = ReadResBytes(path); return DecodeCwpFrames(data, palette); }
    public static Image[] DecodeCwpFrames(byte[] data, Color[] palette)
    {
        if (!IsCwp(data)) throw new InvalidDataException("Not a CWP file");
        int[] sizes = GetCwpFrameSizes(data);
        var images = new List<Image>();
        int framesStart = CwpSizeTableOffset + sizes.Length * 4;
        for (int i = 0; i < sizes.Length; i++)
        {
            int frameStart = framesStart + sizes.Take(i).Sum();
            int frameDataStart = frameStart + CwpFrameHeaderSize;
            int width = CwpFrameWidth;
            int approxPixels = sizes[i] - CwpFrameHeaderSize;
            int height = approxPixels / width;
            int pixelCount = width * height;
            if (frameDataStart + pixelCount > data.Length)
                pixelCount = data.Length - frameDataStart;
            if (pixelCount <= 0) break;
            if (pixelCount % width != 0) continue;
            DecodeWestwoodRle(data, frameDataStart, frameDataStart + pixelCount, out var pixels);
            images.Add(PixelsToImage(pixels, width, height, palette));
        }
        return images.ToArray();
    }

    public static Color[] LoadPal(string path) { var data = ReadResBytes(path); return LoadPal(data); }
    public static Color[] LoadPal(byte[] data)
    {
        const int headerSize = 8;
        if (data.Length < headerSize + 768) throw new InvalidDataException($"PAL too small: {data.Length}");
        var colors = new Color[256];
        for (int i = 0; i < 256; i++)
        {
            int o = headerSize + i * 3;
            colors[i] = new Color(data[o] / 255f, data[o + 1] / 255f, data[o + 2] / 255f, i == 0 ? 0f : 1f);
        }
        return colors;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LevHeader
    {
        public ushort Width, Height, TileWidth, TileHeight;
        public uint TerrainOffset, ObjectOffset, TriggerOffset, StringOffset;
    }

    public static LevelData LoadLev(string path) { var data = ReadResBytes(path); return LoadLev(data); }
    public static LevelData LoadLev(byte[] data)
    {
        if (data.Length < 24) throw new InvalidDataException("LEV too small");
        var header = MemoryMarshal.Cast<byte, LevHeader>(data)[0];
        var level = new LevelData { Width = header.Width, Height = header.Height, TileWidth = header.TileWidth, TileHeight = header.TileHeight };
        if (header.TerrainOffset > 0 && header.TerrainOffset < data.Length)
        {
            int terrainSize = header.Width * header.Height;
            var terrain = new byte[terrainSize];
            Buffer.BlockCopy(data, (int)header.TerrainOffset, terrain, 0, Math.Min(terrainSize, data.Length - (int)header.TerrainOffset));
            level.Terrain = terrain;
        }
        if (header.ObjectOffset > 0 && header.ObjectOffset < data.Length)
            level.Objects = ParseObjects(data, (int)header.ObjectOffset);
        return level;
    }

    private static List<MapObject> ParseObjects(byte[] data, int offset)
    {
        var objects = new List<MapObject>();
        return objects;
    }

    public struct LevelData { public ushort Width, Height, TileWidth, TileHeight; public byte[] Terrain; public List<MapObject> Objects; }
    public struct MapObject { public ushort Type, X, Y; public byte Owner, Flags; public byte[] Data; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CwiEntry
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Name;
        public ushort Health, Cost; public byte BuildTime, Speed, Range, Damage, Armor, Sight;
        public byte WeaponType, ArmorType, Prerequisite, TechLevel, Owner, Flags;
        public ushort GraphicId, IconId, SoundId;
        public byte[] Padding;
    }

    public static List<UnitStats> LoadCwi(string path) { var data = ReadResBytes(path); return LoadCwi(data); }
    public static List<UnitStats> LoadCwi(byte[] data)
    {
        int entrySize = Marshal.SizeOf<CwiEntry>();
        int count = data.Length / entrySize;
        var units = new List<UnitStats>();
        for (int i = 0; i < count; i++)
        {
            var entry = MemoryMarshal.Cast<byte, CwiEntry>(data.AsSpan(i * entrySize))[0];
            var name = System.Text.Encoding.ASCII.GetString(entry.Name).TrimEnd('\0');
            units.Add(new UnitStats
            {
                Name = name,
                MaxHealth = entry.Health, CostCredits = entry.Cost,
                BuildTime = entry.BuildTime * 20, MoveSpeed = Fixed.FromFloat(entry.Speed * 0.5f),
                AttackRange = Fixed.FromFloat(entry.Range), AttackDamage = entry.Damage,
                Armor = entry.Armor, SightRange = entry.Sight,
                WeaponType = (WeaponType)entry.WeaponType, ArmorType = (ArmorType)entry.ArmorType,
                Prerequisite = entry.Prerequisite, TechLevel = entry.TechLevel,
                Faction = (Faction)entry.Owner, GraphicId = entry.GraphicId, IconId = entry.IconId
            });
        }
        return units;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WsaHeader { public uint Magic, Version, Count, IndexOffset; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WsaEntry { public uint Offset, Size, SampleRate; public ushort Channels, BitsPerSample; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Name; }
    public static Dictionary<string, byte[]> ExtractWsa(string path) { var data = ReadResBytes(path); var header = MemoryMarshal.Cast<byte, WsaHeader>(data)[0]; if (header.Magic != 0x20415357) throw new InvalidDataException("Not a WSA file"); var entries = MemoryMarshal.Cast<byte, WsaEntry>(data.AsSpan((int)header.IndexOffset)); var result = new Dictionary<string, byte[]>(); for (int i = 0; i < header.Count; i++) { var entry = entries[i]; var name = System.Text.Encoding.ASCII.GetString(entry.Name).TrimEnd('\0'); var audioData = new byte[entry.Size]; Buffer.BlockCopy(data, (int)entry.Offset, audioData, 0, (int)entry.Size); result[name] = audioData; } return result; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VqaHeader { public uint Magic, Version; public ushort Width, Height, FrameRate, FrameCount; public uint DataOffset; }
    public static VideoInfo LoadVqa(string path) { var data = ReadResBytes(path); var header = MemoryMarshal.Cast<byte, VqaHeader>(data)[0]; if (header.Magic != 0x415156) throw new InvalidDataException("Not a VQA file"); return new VideoInfo { Width = header.Width, Height = header.Height, FrameRate = header.FrameRate, FrameCount = header.FrameCount, DataOffset = (int)header.DataOffset }; }
    public struct VideoInfo { public int Width, Height, FrameRate, FrameCount, DataOffset; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MixHeader { public uint FileCount, IndexOffset; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MixEntry { public uint Offset, Size, NameHash, Flags; }
    public static Dictionary<string, byte[]> ExtractMix(string path) { var data = ReadResBytes(path); var header = MemoryMarshal.Cast<byte, MixHeader>(data)[0]; var entries = MemoryMarshal.Cast<byte, MixEntry>(data.AsSpan(8, (int)header.FileCount * 16)); var result = new Dictionary<string, byte[]>(); for (int i = 0; i < header.FileCount; i++) { var entry = entries[i]; var fileData = new byte[entry.Size]; Buffer.BlockCopy(data, (int)entry.Offset, fileData, 0, (int)entry.Size); result[$"file_{i:D4}"] = fileData; } return result; }
}
