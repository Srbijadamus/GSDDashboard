using System.Text;

namespace GSDDashboard.API.Services;

/// <summary>
/// Generic repair for UTF-8 text that was written into the database after
/// being mis-decoded as Windows-1252 (each UTF-8 byte became one character
/// with the same code point as the byte value — the classic "Ã¼" for "ü"
/// pattern). Repairs by re-encoding the string as Windows-1252 bytes and
/// re-decoding those bytes as UTF-8 — no hardcoded find/replace list, so it
/// generically fixes whatever corrupted sequence shows up, including ones
/// nobody has seen yet (unlike a fixed table of known corrupted pairs).
/// </summary>
public static class Mojibake
{
    // .NET's built-in encodings don't include Windows-1252 (System.Text.Encoding
    // .CodePages package registers it) — needed because the 0x80-0x9F byte
    // range maps to real printable characters in Windows-1252 (e.g. 0x9F -> Ÿ)
    // but to unprintable control characters in plain Latin-1, so only genuine
    // Windows-1252 decoding reverses every corrupted sequence, not just the
    // ones that happen to sit above 0xA0.
    //
    // The registration MUST run before the Encoding fields below are created. With an
    // explicit static constructor the field initializers run FIRST, so a
    // RegisterProvider call in the constructor BODY is too late: GetEncoding(1252)
    // throws, the type initializer fails and stays failed for the whole process
    // (found by RosterServiceTests.Preview_RegionalHoliday_OnlySkippedForThatBundesland,
    // 2026-10-12).
    private static readonly bool CodePagesRegistered = RegisterCodePages();

    private static bool RegisterCodePages()
    {
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        return true;
    }

    private static readonly Encoding Windows1252 = Encoding.GetEncoding(
        1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    private static readonly Encoding Utf8Strict = Encoding.GetEncoding(
        "utf-8", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    /// <summary>
    /// Returns the repaired string, or the original if it isn't corrupted (no
    /// 'Ã' marker) or doesn't round-trip cleanly as Windows-1252-mis-decoded
    /// UTF-8 (so a string that merely happens to contain 'Ã' legitimately is
    /// left untouched rather than guessed at).
    /// </summary>
    public static string Repair(string? s)
    {
        if (string.IsNullOrEmpty(s) || !s.Contains('Ã')) return s ?? "";
        try
        {
            var bytes = Windows1252.GetBytes(s);
            return Utf8Strict.GetString(bytes);
        }
        catch (EncoderFallbackException) { return s; }
        catch (DecoderFallbackException) { return s; }
    }

    public static bool IsCorrupted(string? s) => !string.IsNullOrEmpty(s) && Repair(s) != s;
}
