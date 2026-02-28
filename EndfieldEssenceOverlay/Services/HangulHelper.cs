// src/EndfieldEssenceOverlay/Services/HangulHelper.cs
using System.Text;

namespace EndfieldEssenceOverlay.Services;

/// <summary>
/// 한글 음절을 초성/중성/종성 자모로 분해합니다.
/// OCR 오인식된 모음(ㅡ↔ㅜ, ㅏ↔ㅐ 등)을 퍼지 매칭이 잡을 수 있도록
/// 음절 단위 대신 자모 단위로 비교하기 위해 사용합니다.
/// </summary>
public static class HangulHelper
{
    private const char SyllableStart = '\uAC00';
    private const char SyllableEnd   = '\uD7A3';
    private const int  JungCount     = 28;   // 종성 수
    private const int  ChoCount      = 21 * JungCount; // 588

    private static readonly char[] Cho =
        "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ".ToCharArray();

    private static readonly char[] Jung =
        "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ".ToCharArray();

    private static readonly string[] Jong =
    [
        "",   "ㄱ", "ㄲ", "ㄳ", "ㄴ", "ㄵ", "ㄶ", "ㄷ",
        "ㄹ", "ㄺ", "ㄻ", "ㄼ", "ㄽ", "ㄾ", "ㄿ", "ㅀ",
        "ㅁ", "ㅂ", "ㅄ", "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅊ",
        "ㅋ", "ㅌ", "ㅍ", "ㅎ",
    ];

    /// <summary>
    /// 문자열 내 한글 음절을 자모로 분해합니다. 비한글 문자는 그대로 유지.
    /// 예: "증가" → "ㅈㅡㅇㄱㅏ"
    /// </summary>
    public static string Decompose(string text)
    {
        var sb = new StringBuilder(text.Length * 3);
        foreach (char c in text)
        {
            if (c >= SyllableStart && c <= SyllableEnd)
            {
                int code = c - SyllableStart;
                sb.Append(Cho [code / ChoCount]);
                sb.Append(Jung[(code % ChoCount) / JungCount]);
                sb.Append(Jong[code % JungCount]);
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
