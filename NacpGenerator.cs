using System.Buffers.Binary;
using System.Text;

namespace NXVideoMaker;

/// <summary>
/// Generates a Nintendo Switch control.nacp binary and stages the full
/// control romfs folder that hacpack expects.
/// </summary>
public static class NacpGenerator
{
    private static readonly string[] Languages =
    [
        "AmericanEnglish",
        "BritishEnglish",
        "Japanese",
        "French",
        "German",
        "LatinAmericanSpanish",
        "Spanish",
        "Italian",
        "Dutch",
        "CanadianFrench",
        "Portuguese",
        "Russian",
        "Korean",
        "TraditionalChinese",
        "SimplifiedChinese",
        "BrazilianPortuguese",
    ];

    public static void Generate(
        string title,
        string developer,
        string displayVersion,
        string titleIdHex,      // e.g. "0400000000400000" with or without 0x
        string iconSourcePath,
        string controlRomfsDir)
    {
        Directory.CreateDirectory(controlRomfsDir);

        ulong titleId = Convert.ToUInt64(
            titleIdHex.Replace("0x", "").Replace("0X", ""), 16);

        var nacp = new byte[0x4000]; // zero-initialised

        // ── Title entries: 16 × 0x300 bytes ─────────────────────────────────
        // Each entry: name[0x200] + developer[0x100]
        for (int i = 0; i < 16; i++)
        {
            int baseOff = i * 0x300;
            WriteStr(nacp, baseOff,          0x200, title);
            WriteStr(nacp, baseOff + 0x200,  0x100, developer);
        }

        // ── SupportedLanguageFlag @ 0x302C — all 16 languages set ────────────
        WriteU32(nacp, 0x302C, 0x0000FFFF);

        // ── StartupUserAccount @ 0x3025 = 0 (None) ──────────────────────────
        nacp[0x3025] = 0;

        // ── Screenshot @ 0x3034 = 0 (Allow) ─────────────────────────────────
        nacp[0x3034] = 0;

        // ── VideoCapture @ 0x3035 = 1 (Manual) ──────────────────────────────
        nacp[0x3035] = 1;

        // ── PresenceGroupId @ 0x3038 ─────────────────────────────────────────
        WriteU64(nacp, 0x3038, titleId);

        // ── DisplayVersion @ 0x3060 ──────────────────────────────────────────
        WriteStr(nacp, 0x3060, 0x10, displayVersion);

        // ── AddOnContentBaseId @ 0x3070 ──────────────────────────────────────
        WriteU64(nacp, 0x3070, 0);

        // ── SaveDataOwnerId @ 0x3078 ─────────────────────────────────────────
        WriteU64(nacp, 0x3078, 0);

        // ── UserAccountSaveDataSize @ 0x3080 ─────────────────────────────────
        WriteU64(nacp, 0x3080, 0);

        // ── UserAccountSaveDataJournalSize @ 0x3088 ───────────────────────────
        WriteU64(nacp, 0x3088, 0);

        // ── UserAccountSaveDataSizeMax @ 0x3148 ──────────────────────────────
        WriteU64(nacp, 0x3148, 0);

        // ── UserAccountSaveDataJournalSizeMax @ 0x3150 ────────────────────────
        WriteU64(nacp, 0x3150, 0);

        // ── LogoType @ 0x30F0 = 0 (LicensedByNintendo) ───────────────────────
        nacp[0x30F0] = 0;

        // ── LogoHandling @ 0x30F1 = 0 (Auto) ────────────────────────────────
        nacp[0x30F1] = 0;

        // ── CrashReport @ 0x30F6 = 1 (Allow) ────────────────────────────────
        nacp[0x30F6] = 1;

        // ── JitConfiguration @ 0x33B0 ────────────────────────────────────────
        WriteU64(nacp, 0x33B0, 0x0000000000000000);
        WriteU64(nacp, 0x33B8, 0x0000000004000000);

        File.WriteAllBytes(Path.Combine(controlRomfsDir, "control.nacp"), nacp);

        // ── Write icon for every language ────────────────────────────────────
        foreach (string lang in Languages)
            File.Copy(iconSourcePath,
                      Path.Combine(controlRomfsDir, $"icon_{lang}.dat"),
                      overwrite: true);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void WriteStr(byte[] buf, int offset, int maxLen, string value)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(value);
        int    count   = Math.Min(encoded.Length, maxLen - 1);
        Array.Copy(encoded, 0, buf, offset, count);
    }

    private static void WriteU32(byte[] buf, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset), value);

    private static void WriteU64(byte[] buf, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(offset), value);
}