// src/EndfieldEssenceOverlay/Services/EssenceMatcherService.cs
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EndfieldEssenceOverlay.Models;
using FuzzySharp;

namespace EndfieldEssenceOverlay.Services;

// JSON 모델
public record WeaponEntry(
    [property: JsonPropertyName("name")]     string Name,
    [property: JsonPropertyName("star")]     int Star,
    [property: JsonPropertyName("essences")] List<string> Essences);

public record WeaponsData(
    [property: JsonPropertyName("gameVersion")] string GameVersion,
    [property: JsonPropertyName("weapons")]     List<WeaponEntry> Weapons);

public class EssenceMatcherService
{
    private readonly string _ownedPath;
    private List<WeaponEntry> _weapons = [];
    private HashSet<string> _ownedNames = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _vocabulary = [];  // 모든 유효 키워드 플랫 목록

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public EssenceMatcherService(string weaponsPath, string ownedPath)
    {
        _ownedPath = ownedPath;
        LoadWeapons(weaponsPath);
        LoadOwned(ownedPath);
        RebuildVocabulary();
    }

    public MatchResult Match(IList<string> rawLines)
    {
        // OCR 결과를 각각 가장 가까운 기질 키워드로 스냅 (점수 포함)
        var snappedWithScore = rawLines
            .Select(StripNonKorean)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(SnapToKeywordWithScore)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .GroupBy(x => x.Keyword, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Score).First())
            .ToList();

        // 매칭용: SnapThreshold 이상인 모든 스냅 결과
        var snapped = snappedWithScore
            .Select(x => x.Keyword)
            .ToList();

        // 표시용: SnapDisplayThreshold 이상인 고신뢰 결과만
        var displaySnapped = snappedWithScore
            .Where(x => x.Score >= Config.SnapDisplayThreshold)
            .Select(x => x.Keyword)
            .ToList();

        var validHits = _weapons.Where(w => SetMatch(snapped, w.Essences)).ToList();
        if (validHits.Count == 0)
            return new MatchResult(MatchStatus.Invalid, [], [], [], SortByCategory(displaySnapped));

        var unowned = validHits
            .Where(w => !_ownedNames.Contains(w.Name))
            .Select(w => w.Name)
            .ToList();

        var sortedEssences = SortByCategory(validHits[0].Essences);

        if (unowned.Count == 0)
            return new MatchResult(MatchStatus.ValidOwned,
                validHits.Select(w => w.Name).ToList(),
                [],
                sortedEssences,
                SortByCategory(snapped));

        return new MatchResult(MatchStatus.ValidUnowned,
            validHits.Select(w => w.Name).ToList(),
            unowned,
            sortedEssences,
            SortByCategory(snapped));
    }

    public void MarkOwned(IList<string> weaponNames, IList<string> keywords)
    {
        foreach (var name in weaponNames)
            _ownedNames.Add(name);
        SaveOwned();
        RebuildVocabulary();
    }

    /// <summary>전체 무기 목록 (weapons.json 순서 보존)</summary>
    public IReadOnlyList<(string Name, int Star, IReadOnlyList<string> Essences)> AllWeapons =>
        _weapons.Select(w => ((string)w.Name, w.Star, (IReadOnlyList<string>)w.Essences)).ToList();

    /// <summary>현재 보유 중인 무기 이름 집합</summary>
    public IReadOnlySet<string> OwnedWeaponNames => _ownedNames;

    /// <summary>보유 목록을 weaponNames 기준으로 재구성하고 파일에 저장</summary>
    public void RebuildOwned(IList<string> weaponNames)
    {
        _ownedNames = new HashSet<string>(weaponNames, StringComparer.OrdinalIgnoreCase);
        SaveOwned();
        RebuildVocabulary();
    }

    // ── JSON 로드/저장 ─────────────────────────────────────────

    private void LoadWeapons(string path)
    {
        if (!File.Exists(path)) return;
        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<WeaponsData>(json);
        if (data?.Weapons != null)
            _weapons = data.Weapons;
    }

    private void LoadOwned(string path)
    {
        if (!File.Exists(path)) return;
        var json = File.ReadAllText(path);
        var names = JsonSerializer.Deserialize<List<string>>(json);
        if (names != null)
            _ownedNames = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }

    private void SaveOwned()
    {
        var sorted = _ownedNames.OrderBy(n => n).ToList();
        File.WriteAllText(_ownedPath, JsonSerializer.Serialize(sorted, _jsonOpts));
    }

    // ── 어휘 & 매칭 로직 ──────────────────────────────────────

    private void RebuildVocabulary()
    {
        _vocabulary = _weapons
            .SelectMany(w => w.Essences)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string StripNonKorean(string text)
        => Regex.Replace(text, @"[^\uAC00-\uD7A3\s]", "").Trim();

    private (string Keyword, int Score)? SnapToKeywordWithScore(string line)
    {
        if (_vocabulary.Count == 0) return null;

        var lineJamo  = HangulHelper.Decompose(line);
        string? best  = null;
        int bestScore = -1;

        foreach (var keyword in _vocabulary)
        {
            var score = Fuzz.Ratio(lineJamo, HangulHelper.Decompose(keyword));
            if (score > bestScore)
            {
                bestScore = score;
                best      = keyword;
            }
        }
        return bestScore >= Config.SnapThreshold ? (best!, bestScore) : null;
    }

    private static bool SetMatch(IList<string> snapped, List<string> target)
        => target.All(t => snapped.Contains(t, StringComparer.OrdinalIgnoreCase));

    // ── 기질 카테고리 순서 정렬 ─────────────────────────────────

    private static readonly HashSet<string> _primaryEssences = new(StringComparer.OrdinalIgnoreCase)
    {
        "힘 증가", "민첩 증가", "의지 증가", "지능 증가", "주요 능력치 증가"
    };

    private static readonly HashSet<string> _secondaryEssences = new(StringComparer.OrdinalIgnoreCase)
    {
        "공격력 증가", "생명력 증가", "물리 피해 증가", "열기 피해 증가",
        "전기 피해 증가", "냉기 피해 증가", "자연 피해 증가", "치명타 확률 증가",
        "오리지늄 아츠 증가", "궁극기 획득 효율 증가", "아츠 피해 증가", "치유 효율 증가"
    };

    private static int EssenceCategoryOrder(string keyword)
        => _primaryEssences.Contains(keyword) ? 0
         : _secondaryEssences.Contains(keyword) ? 1
         : 2;

    public static List<string> SortByCategory(IEnumerable<string> essences)
        => essences.OrderBy(EssenceCategoryOrder).ToList();
}
