// src/EndfieldEssenceOverlay/Models/MatchResult.cs
namespace EndfieldEssenceOverlay.Models;

public enum MatchStatus { Invalid, ValidUnowned, ValidOwned }

public record MatchResult(
    MatchStatus           Status,
    IReadOnlyList<string> MatchedNames,
    IReadOnlyList<string> UnownedNames,     // 미보유 무기만 (보유 버튼 표시용)
    IReadOnlyList<string> MatchedEssences,  // 매칭 무기의 정제된 기질 (유효 시)
    IReadOnlyList<string> SnappedEssences   // 스냅된 감지 기질 (항상 표시용)
)
{
    public MatchResult(MatchStatus status) : this(status, [], [], [], []) { }
    public string? MatchedName => MatchedNames.Count > 0
        ? string.Join(" / ", MatchedNames)
        : null;
}
