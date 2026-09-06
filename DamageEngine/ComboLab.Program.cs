using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NightView.Gameplay;

/// <summary>
/// 조합 탐색 랩 (Unity 비의존) — 모드·탄약 조합 중 상위 데미지 빌드를 찾고,
/// 모드를 "상위 그룹 / 하위 그룹"으로 분류한다.
///
/// 전수 조합은 불가능하다(소총만 C(17,6)×C(35,4) ≈ 6.5억 × 시퀀스).
/// 대신 시퀀스 아키타입을 고정하고 슬롯을 하나씩 채우는 <b>빔 서치</b>로 상위 빌드를 수집한다.
/// 빔 폭 안에서 곱연쇄·조건 충족이 함께 살아남으므로 "혼자선 약한데 셋이 모이면 강한" 조합도 잡힌다.
///
/// 모드·탄약 수치는 .asset(YAML)을 직접 파싱한다 — 수치를 고치고 재실행하면 바로 반영된다(하드코딩 드리프트 없음).
///
/// 실행: dotnet run --project Tools/ComboLab -- [--beam N] [--top N] [--gun 소총]
/// </summary>
internal static class Program
{
    /// <summary>
    /// 평가 기준 스테이지 — 감전 증폭(ShockState)이 표적 요구치에 반비례하므로 고정 기준이 필요하다.
    /// 기본 6 = 밸런스 목표 지점(스테6 클리어율 80%).
    ///
    /// ⚠ 표적 축 도입(2026-08-14) 이후 <b>기준이 낮으면 출력이 포화한다</b> — 스테6의 표적 1기 체력은
    ///   312인데 초기 세트 기관총도 발당 300을 넘겨 '발당 1킬'이 되고, 여러 축이 같은 값에서 만난다.
    ///   축 격차를 보려면 --stage 12나 15처럼 체력이 큰 구간에서 재야 변별이 산다.
    /// </summary>
    private static int _refStage = 6;

    private static float ReferenceRequired => StageProgression.GetRequired(_refStage);
    /// <summary>동시 필드 표적 수 — 물량전 전환(§17) 후 스테이지 편성 테이블에서 온다.</summary>
    private static int ReferenceTargets => StageProgression.GetStage(_refStage).FieldCap;
    /// <summary>표적 1기 체력 — ⚠ 더는 요구치에서 파생되지 않는다. 보스 주기 계단 테이블 값이다(§17.3).</summary>
    private static float ReferenceHealth => StageProgression.GetStage(_refStage).TargetHealth;

    private static int _beam = 120;
    private static int _top = 3;
    private static string _gunFilter = null;
    private static bool _whatIf = false;
    private static bool _tier1 = false;

    /// <summary>
    /// --tier1 에서 살아남는 탄약 id — <b>해금 전 초기 세트 가정</b>(0-b 큐레이션 대상, 미확정).
    /// 물리 2단(납·중탄)과 속성 1단(화염·독·전기)만. 상위 탄약(강철·텅스텐·점화·맹독·중화염)과
    /// 광역(관통·폭발)은 해금으로 연다 — 광역은 다중 표적 전투 자체를 바꾸므로 해금 가치가 크다.
    /// ⚠ 모드 쪽 잠금은 목록이 필요 없다 — 사슬 상위는 "누군가의 NextTier"로 파생 판정한다(런타임과 같은 규칙).
    /// </summary>
    private static readonly HashSet<string> Tier1Ammo = new HashSet<string>
    {
        "base", "silver", "incendiary", "toxic", "shock",
    };

    private static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        ParseArgs(args);

        string root = FindRepoRoot();
        if (root == null)
        {
            Console.Error.WriteLine("❌ 저장소 루트를 찾지 못했습니다 (Assets/00.GameData 기준).");
            Environment.Exit(1);
        }

        List<ModDef> mods = LoadMods(root);
        List<AmmoDef> ammo = LoadAmmo(root);
        if (_tier1) ApplyTier1(mods, ammo);
        if (_whatIf) ApplyWhatIf(mods, ammo);
        SetAmmoIndex(ammo);
        List<SeqDef> seqs = BuildSequences(ammo);

        Console.WriteLine("==================================================");
        Console.WriteLine(" GunRogue 조합 탐색 랩 (ComboLab)");
        Console.WriteLine($" 모드 {mods.Count}종 (총기 {CountGun(mods)} / 탄창 {mods.Count - CountGun(mods)}) · 탄약 {ammo.Count}종 · 시퀀스 {seqs.Count}종");
        Console.WriteLine($" 빔 폭 {_beam} · 기준 스테{_refStage} 요구치 {ReferenceRequired:0} · 필드 {ReferenceTargets}기 " +
                          $"(1기 체력 {ReferenceHealth:0} — 계단 테이블) · 탄약 예산 무시(이상화)");
        Console.WriteLine("==================================================");

        var guns = new List<GunFrame>
        {
            new GunFrame("리볼버", 6, 4, 8, true),
            new GunFrame("소총", 8, 4, 6, false),
            new GunFrame("기관총", 12, 4, 4, false),
            new GunFrame("저격총", 4, 4, 12, false),
        };
        if (_gunFilter != null) guns = guns.FindAll(g => g.Name == _gunFilter);

        var allTop = new List<Scored>();
        foreach (GunFrame gun in guns)
        {
            List<Scored> tops = SearchGun(gun, mods, seqs);
            allTop.AddRange(tops);
            PrintGunSection(gun, tops, mods);
        }

        GunFrame refGun = guns.Find(g => g.Name == "소총") ?? (guns.Count > 0 ? guns[0] : null);
        PrintSoloTable(refGun, mods, seqs);
        PrintAxisCeiling(allTop);
        PrintPickTable(allTop, mods);
        PrintAmmoTable(allTop, seqs);
        Console.WriteLine();
        Console.WriteLine(" ※ 빔 서치 = 근사 탐색. 빔 폭을 키우면(--beam 400) 더 넓게 보지만 결론은 대개 같다.");
        Console.WriteLine(" ※ 감전 증폭(ShockState)은 반영 — LabEngine.Walk엔 빠져 있어 감전 빌드가 거기선 과소평가된다.");
        Console.WriteLine(" ※ 보스 기믹·상점 RNG·재화는 미반영. '이상적 조합의 천장'을 재는 도구다.");
    }

    private static void ParseArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--beam" && i + 1 < args.Length) _beam = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--top" && i + 1 < args.Length) _top = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--gun" && i + 1 < args.Length) _gunFilter = args[++i];
            else if (args[i] == "--whatif") _whatIf = true;
            else if (args[i] == "--tier1") _tier1 = true;
            else if (args[i] == "--stage" && i + 1 < args.Length) _refStage = int.Parse(args[++i], CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// --tier1 : <b>해금 전 초기 상태</b>로 후보를 좁힌다 — 첫 런의 천장이 어디인지 재기 위한 모드.
    ///
    /// 모드는 사슬 상위(= 누군가의 NextTier)를 제외한다. 런타임 ShopManager.PruneLockedMods와 같은
    /// 파생 판정이라 별도 목록이 필요 없고, 빌더에서 사슬을 바꾸면 여기도 자동으로 따라온다.
    /// 탄약은 Tier1Ammo 화이트리스트(가정값)만 남긴다.
    ///
    /// ⚠ 이 수치는 "초기 세트로 도달 가능한 이상적 천장"이다 — 재화·추첨 운은 여전히 미반영이므로
    ///   실제 첫 런은 이보다 낮게 나온다. 목표(8~9스테 정지)와 비교할 때 그 여유를 감안할 것.
    /// </summary>
    private static void ApplyTier1(List<ModDef> mods, List<AmmoDef> ammo)
    {
        var upper = new HashSet<string>();
        foreach (ModDef m in mods)
            if (!string.IsNullOrEmpty(m.NextTierGuid)) upper.Add(m.NextTierGuid);

        int modsBefore = mods.Count, ammoBefore = ammo.Count;
        mods.RemoveAll(m => m.Guid != null && upper.Contains(m.Guid));
        ammo.RemoveAll(a => !Tier1Ammo.Contains(a.Id));

        Console.WriteLine($" 🔒 TIER1: 해금 전 초기 세트 — 모드 {modsBefore}→{mods.Count} (사슬 상위 {upper.Count}종 잠금) · " +
                          $"탄약 {ammoBefore}→{ammo.Count}");
    }

    /// <summary>
    /// 물리 축 보강 실험 — **게임 데이터는 건드리지 않는다**(메모리 상에서만 치환).
    /// 두 레버를 동시에 건다:
    ///  ① 물리 탄약 P4·P6 티어 신설 — 현재 물리 라인이 중탄(P2)에서 끊겨 있다(화염은 P6까지 3티어).
    ///  ② 크리 수치 대폭 상향 — 크리는 물리에만 걸리므로(속성은 크리 미적용) 물리만 올리는 유일한 레버다.
    ///     기대배율 = (1-c) + c·m 이라 c와 m이 곱으로 붙는다 → 둘 다 키우면 초선형(= DoT 누적의 대응물).
    /// 실행: dotnet run --project Tools/ComboLab -- --whatif
    /// </summary>
    private static void ApplyWhatIf(List<ModDef> mods, List<AmmoDef> ammo)
    {
        // ① 물리 상위 티어 (화염 중화염탄 P4 / 점화탄 P6과 같은 자리)
        ammo.Add(new AmmoDef { Id = "steel", Name = "강철탄", Price = 4, Spec = new AmmoSpec(22f) });
        ammo.Add(new AmmoDef { Id = "tungsten", Name = "텅스텐탄", Price = 6, Spec = new AmmoSpec(30f) });

        // ② 크리 축 — 조준경 크리율 +50%→+90% · 할로우 포인트 배율 +0.5→+3.0
        //    결과: 크리율 100%(기본 10 + 90) · 배율 5.0(기본 2.0 + 3.0) → 기대배율 5.0 (현행 최대 1.9)
        foreach (ModDef m in mods)
        {
            if (m.Id == "scope") m.Spec = new ModSpec(ModType.CritChance, 0.9f);
            else if (m.Id == "hollow_point") m.Spec = new ModSpec(ModType.CritMultiplier, 3.0f);
        }

        Console.WriteLine(" ⚗ WHAT-IF: 물리 탄약 P4(22)·P6(30) 신설 + 조준경 0.9 / 할로우 포인트 3.0 (게임 데이터 미변경)");
    }

    private static int CountGun(List<ModDef> mods)
    {
        int n = 0;
        for (int i = 0; i < mods.Count; i++) if (mods[i].IsGunMod) n++;
        return n;
    }

    // ══════════════════════════════════════════════
    //  데이터 로딩 — .asset(YAML) 직접 파싱
    // ══════════════════════════════════════════════

    private sealed class ModDef
    {
        public string Id;
        public string Name;
        public ModSpec Spec;   // what-if에서 치환 가능하도록 가변 필드
        public int Rarity;
        public int Price;
        public bool IsGunMod;
        public string Guid;         // .meta의 자기 guid — 사슬 대조용
        public string NextTierGuid; // 상위 티어 참조 (없으면 null)
    }

    private static readonly Regex GuidRx = new Regex(@"guid:\s*([0-9a-fA-F]+)", RegexOptions.Compiled);

    private static string ExtractGuid(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        Match m = GuidRx.Match(s);
        return m.Success ? m.Groups[1].Value : null;
    }

    private sealed class AmmoDef
    {
        public string Id;
        public string Name;
        public AmmoSpec Spec;
        public int Price;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "00.GameData"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static List<ModDef> LoadMods(string root)
    {
        var list = new List<ModDef>();
        string dir = Path.Combine(root, "Assets", "00.GameData", "ModData");
        foreach (string path in Directory.GetFiles(dir, "*.asset", SearchOption.AllDirectories))
        {
            Dictionary<string, string> f = ParseAsset(path);
            if (!f.ContainsKey("ModId")) continue;

            var spec = new ModSpec(
                (ModType)Int(f, "ModType"),
                Flt(f, "Value"),
                (TriggerType)Int(f, "Trigger"),
                Int(f, "TriggerParam"),
                (ConditionType)Int(f, "Condition"),
                (ElementType)Int(f, "CondElement"),
                Int(f, "CondParam"),
                (ElementType)Int(f, "TargetElement"));

            list.Add(new ModDef
            {
                Id = f["ModId"],
                Name = Get(f, "DisplayName", f["ModId"]),
                Spec = spec,
                Rarity = Int(f, "Rarity"),
                Price = Int(f, "Price"),
                IsGunMod = spec.IsGunMod,
                Guid = ExtractGuid(File.Exists(path + ".meta") ? File.ReadAllText(path + ".meta") : null),
                NextTierGuid = ExtractGuid(Get(f, "NextTier", null)),
            });
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return list;
    }

    private static List<AmmoDef> LoadAmmo(string root)
    {
        var list = new List<AmmoDef>();
        string dir = Path.Combine(root, "Assets", "00.GameData", "AmmoData");
        foreach (string path in Directory.GetFiles(dir, "*.asset", SearchOption.AllDirectories))
        {
            Dictionary<string, string> f = ParseAsset(path);
            if (!f.ContainsKey("AmmoId")) continue;

            list.Add(new AmmoDef
            {
                Id = f["AmmoId"],
                Name = Get(f, "DisplayName", f["AmmoId"]),
                Spec = new AmmoSpec(Flt(f, "BaseDamage"), (ElementType)Int(f, "Element"), Flt(f, "ElementalBase"),
                                    statusMultiplier: f.ContainsKey("StatusMultiplier") ? Flt(f, "StatusMultiplier") : 1f),
                Price = Int(f, "Price"),
            });
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return list;
    }

    private static readonly Regex FieldRx = new Regex(
        @"<(\w+)>k__BackingField:[ ]?(.*?)(?=\r?\n\s*<\w+>k__BackingField:|\r?\n---|\z)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex UnicodeRx = new Regex(@"\\u([0-9a-fA-F]{4})", RegexOptions.Compiled);
    private static readonly Regex WsRx = new Regex(@"\s+", RegexOptions.Compiled);

    private static Dictionary<string, string> ParseAsset(string path)
    {
        string text = File.ReadAllText(path, Encoding.UTF8);
        var dict = new Dictionary<string, string>();
        foreach (Match m in FieldRx.Matches(text))
            dict[m.Groups[1].Value] = Clean(m.Groups[2].Value);
        return dict;
    }

    private static string Clean(string s)
    {
        s = WsRx.Replace(s, " ").Trim();
        if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2);
        s = UnicodeRx.Replace(s, m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        return s.Replace("\\xB7", "·");
    }

    private static string Get(Dictionary<string, string> f, string key, string fallback)
        => f.TryGetValue(key, out string v) && v.Length > 0 ? v : fallback;

    private static int Int(Dictionary<string, string> f, string key)
        => f.TryGetValue(key, out string v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float r)
           ? (int)r : 0;

    private static float Flt(Dictionary<string, string> f, string key)
        => f.TryGetValue(key, out string v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float r)
           ? r : 0f;

    // ══════════════════════════════════════════════
    //  총기 골격 · 시퀀스 아키타입
    // ══════════════════════════════════════════════

    private sealed class GunFrame
    {
        public readonly string Name;
        public readonly int MagSize, MagCount, GunSlots;
        public readonly bool SizeLocked;
        public int MagSlots => MagSize / GameConfig.MagModSlotDivisor;

        public GunFrame(string name, int magSize, int magCount, int gunSlots, bool sizeLocked)
        {
            Name = name; MagSize = magSize; MagCount = magCount; GunSlots = gunSlots; SizeLocked = sizeLocked;
        }
    }

    /// <summary>시퀀스 아키타입 — 패턴을 탄창 크기만큼 순환 배치. "-" = 빈 칸.</summary>
    private sealed class SeqDef
    {
        public string Name;
        public string[] Pattern;
        public string Axis;   // 물리 / 화염 / 독 / 전기 / 혼합 — 축별 채택률 집계 단위
    }

    /// <summary>축 목록 — 출력 열 순서.</summary>
    private static readonly string[] Axes = { "물리", "화염", "독", "전기", "혼합" };

    private static string AxisOf(ElementType e, string ammoId)
    {
        if (e == ElementType.Fire) return "화염";
        if (e == ElementType.Poison) return "독";
        if (e == ElementType.Shock) return "전기";
        return "물리";
    }

    private static List<SeqDef> BuildSequences(List<AmmoDef> ammo)
    {
        var list = new List<SeqDef>();

        // ① 단일 탄종 × 빈칸 비율 3종 — 빈칸 축(과적 장전·텅 빈 약실·빈칸 반동)을 탐색에 노출
        foreach (AmmoDef a in ammo)
        {
            string ax = AxisOf(a.Spec.Element, a.Id);
            list.Add(new SeqDef { Name = a.Name + " 채움", Pattern = new[] { a.Id }, Axis = ax });
            list.Add(new SeqDef { Name = a.Name + " ¾", Pattern = new[] { a.Id, a.Id, a.Id, "-" }, Axis = ax });
            list.Add(new SeqDef { Name = a.Name + " ½", Pattern = new[] { a.Id, "-" }, Axis = ax });
        }

        // ② 다속성 — 무지개·교대 사격·속성 연쇄·정예 분대 축
        list.Add(new SeqDef { Name = "화독전 교대", Pattern = new[] { "incendiary", "toxic", "shock" }, Axis = "혼합" });
        list.Add(new SeqDef { Name = "고급 화독전 교대", Pattern = new[] { "inferno", "toxic_heavy", "shock" }, Axis = "혼합" });
        list.Add(new SeqDef { Name = "무화독전 교대", Pattern = new[] { "base", "incendiary", "toxic", "shock" }, Axis = "혼합" });
        list.Add(new SeqDef { Name = "화독전+빈칸", Pattern = new[] { "incendiary", "toxic", "shock", "-" }, Axis = "혼합" });
        list.Add(new SeqDef { Name = "4종만(나머지 빈칸)", Pattern = new[] { "silver", "inferno", "toxic_heavy", "shock", "-", "-", "-", "-", "-", "-", "-", "-" }, Axis = "혼합" });
        list.Add(new SeqDef { Name = "화염 위주+독", Pattern = new[] { "inferno", "inferno", "inferno", "toxic_heavy" }, Axis = "혼합" });
        list.Add(new SeqDef { Name = "중탄+소이 교대", Pattern = new[] { "silver", "incendiary_heavy" }, Axis = "혼합" });

        // ③ 감전 오프너 — 감전은 '뒤에 오는 즉발'을 증폭하므로 선두 배치가 제자리다.
        //    단일 전기 시퀀스로는 증폭 대상이 전기 즉발뿐이라 감전의 진짜 값을 못 잰다.
        //    패턴을 12칸으로 길게 깔아 순환을 막는다(기관총 12칸까지 커버).
        list.Add(new SeqDef { Name = "전기2 오프너+중화염", Axis = "혼합", Pattern = new[]
            { "shock", "shock", "incendiary_heavy", "incendiary_heavy", "incendiary_heavy", "incendiary_heavy",
              "incendiary_heavy", "incendiary_heavy", "incendiary_heavy", "incendiary_heavy", "incendiary_heavy", "incendiary_heavy" } });
        list.Add(new SeqDef { Name = "전기2 오프너+맹독", Axis = "혼합", Pattern = new[]
            { "shock", "shock", "toxic_heavy", "toxic_heavy", "toxic_heavy", "toxic_heavy",
              "toxic_heavy", "toxic_heavy", "toxic_heavy", "toxic_heavy", "toxic_heavy", "toxic_heavy" } });
        list.Add(new SeqDef { Name = "전기2 오프너+중탄", Axis = "혼합", Pattern = new[]
            { "shock", "shock", "silver", "silver", "silver", "silver",
              "silver", "silver", "silver", "silver", "silver", "silver" } });
        list.Add(new SeqDef { Name = "전기1 오프너+지옥불", Axis = "혼합", Pattern = new[]
            { "shock", "inferno", "inferno", "inferno", "inferno", "inferno",
              "inferno", "inferno", "inferno", "inferno", "inferno", "inferno" } });

        // ⚠ ②·③은 탄약 id를 직접 적는다 — 후보에서 빠진 탄약(--tier1 등)을 쓰는 시퀀스는 여기서 걸러야 한다.
        //   걸러내지 않으면 ApplySequence가 사전 조회에서 KeyNotFound로 죽는다.
        var have = new HashSet<string>();
        foreach (AmmoDef a in ammo) have.Add(a.Id);
        list.RemoveAll(s =>
        {
            foreach (string id in s.Pattern)
                if (id != "-" && !have.Contains(id)) return true;
            return false;
        });

        return list;
    }

    // ══════════════════════════════════════════════
    //  평가 — 턴 워크 (BattleController 재현: 화상·독 DoT + 감전 증폭 + 표적 상태 조건)
    // ══════════════════════════════════════════════

    private static void ApplySequence(RunBuild build, SeqDef seq, Dictionary<string, AmmoDef> byId, out int ammoCost)
    {
        ammoCost = 0;
        int size = build.MagazineSize;
        for (int i = 0; i < size; i++)
        {
            string id = seq.Pattern[i % seq.Pattern.Length];
            if (id == "-") { build.Sequence.Set(i, AmmoEntry.CreateEmpty(), out _); continue; }
            AmmoDef a = byId[id];
            build.Sequence.Set(i, new AmmoEntry(a.Spec, a.Price, false), out _);
            ammoCost += a.Price;
        }
    }

    /// <summary>
    /// 피해 분해 — 즉발 / DoT / 감전 증폭을 나눠 본다. <b>축 간 격차의 원인</b>을 짚는 진단용.
    /// 총합만 보면 "독이 세다"까지밖에 못 읽는데, 나눠 보면 그게 즉발인지 누적인지가 드러난다.
    /// </summary>
    internal struct DamageBreak
    {
        public float Phys;                          // 물리 즉발 (크리 기대배율 반영)
        public float FireHit, PoisonHit, ShockHit;  // 속성 즉발
        public float FireDot, PoisonDot;            // 턴 종료 DoT 틱 (감전은 DoT가 없다 — 증폭이다)
        public float ShockAmp;                      // 감전 증폭이 더한 몫 (다른 피해에 곱해진 초과분)

        public float Hit => Phys + FireHit + PoisonHit + ShockHit;
        public float Dot => FireDot + PoisonDot;
        public float Total => Hit + Dot + ShockAmp;
    }

    private static float Walk(RunBuild build, float required) => Walk(build, required, out _);

    /// <summary>표적 버퍼 — 빔 서치가 Walk를 수만 번 부르므로 매번 할당하지 않는다(단일 스레드).</summary>
    /// <summary>직전 Walk의 발수·킬수·실피해 합 — 출력이 포화했는지(발당 1킬) 판별하는 진단값.
    /// 표적 구조에서는 "총 출력"만으로 빌드 강도를 못 읽는다. 체력 문턱을 넘었는지가 결과를 가른다.</summary>
    internal static int DbgShots, DbgKills;
    internal static float DbgRawSum;

    private static readonly TargetState[] TargetBuf = { new TargetState(), new TargetState(), new TargetState() };

    /// <summary>
    /// 스테이지 출력 = <b>처치 포인트 누적</b> — 표적 체력·오버킬 버림·처치 시 DoT 소멸을 반영한 턴 워크.
    ///
    /// ⚠ 2026-08-14 표적 축 도입 전에는 표적 개념 없이 4턴을 통째로 누적했다. 그 모델에서 독은
    ///   스택이 4턴 내내 쌓여(무감쇠 x10) 즉발의 7배를 냈지만, 실제로는 <b>표적이 죽는 순간
    ///   그 표적의 독 스택이 사라진다</b>(TargetState.Reset). 스테6 기준 표적 1기 체력이 312라
    ///   강한 빌드는 첫 턴에 죽여버려 스택이 쌓일 시간이 없다. DoT 축이 통째로 과대평가돼 있었다.
    ///
    /// 반영한 것 — 표적별 체력(요구치 ÷ 표적수 ÷ 웨이브수) · 오버킬 버림 · 처치 시 상태이상 소멸 ·
    ///   표적별 화상/중독/감전(조준 표적 조건도 그 표적 기준) · 죽으면 다음 표적으로 조준 전환.
    /// ⚠ 한계 — 표적이 죽으면 <b>즉시 새 표적으로 교체</b>한다(무한 공급). 실제 리필은 턴 종료지만,
    ///   그대로 두면 강빌드가 첫 턴에 전멸시켜 출력이 상한에 붙고 빔 서치의 변별력이 사라진다.
    ///   "표적이 떨어져 남은 탄이 버려지는" 손실은 여기서 안 잡힌다(ReachStage 쪽 과제).
    /// ⚠ 광역(관통·폭발)은 여전히 단일 타격으로 계산한다 — 진형 좌표가 없다.
    /// </summary>
    private static float Walk(RunBuild build, float required, out DamageBreak br)
        => Walk(build, required, ReferenceTargets, out br);

    private static float Walk(RunBuild build, float required, int targetCount, out DamageBreak br)
    {
        br = default;
        BuildStats baseStats = build.ToBuildStats();
        int magSize = build.MagazineSize;

        if (targetCount < 1) targetCount = 1;
        if (targetCount > TargetBuf.Length) targetCount = TargetBuf.Length;
        // ⚠ 2026-08-26 물량전 전환 — 체력이 요구치에서 파생되지 않는다(§17.3 계단 테이블).
        //   종전 `required / (표적수 × 웨이브)`는 요구치가 오르면 체력도 정비례로 올라 성장이 안 보였다.
        float hp = ReferenceHealth;
        if (hp <= 0f) hp = 1f;
        for (int t = 0; t < targetCount; t++) TargetBuf[t].Spawn(hp);

        float points = 0f;
        int aim = 0;
        DbgShots = 0; DbgKills = 0; DbgRawSum = 0f;

        for (int turn = 1; turn <= build.MagazineCount; turn++)
        {
            for (int i = 0; i < magSize; i++)
            {
                AmmoEntry entry = build.GetShotEntry(i);
                if (entry.IsEmpty) continue;

                // 조준 대상 — 죽은 표적은 즉시 교체되므로 항상 살아 있다
                TargetState tgt = TargetBuf[aim];

                BuildStats s = build.GetShotStats(i, baseStats);

                // 조준 표적 조건(L2-b) — ⚠ 전역이 아니라 '이 표적'의 상태다
                int mask = 0;
                if (tgt.Burn.IsActive) mask |= 1 << (int)ElementType.Fire;
                if (tgt.Poison.IsActive) mask |= 1 << (int)ElementType.Poison;
                if (tgt.Shock.IsActive) mask |= 1 << (int)ElementType.Shock;

                // ⚠ 진형은 <b>근사</b>다 — ComboLab은 격자를 시뮬하지 않는다(조합 탐색이 목적이라
                //   표적을 배열로만 둔다). §17.4 균등 배치 실측에 맞춘 회귀식:
                //     인접 ≈ (n−1)×0.40  ·  같은 행 ≈ 1 + (n−1)×0.28   (8기에서 2.8 / 3.0 — 실측 2.78 / 2.97)
                //   클러스터 구간(4기 이하)은 과소평가되지만 기준 스테는 균등 구간이라 영향이 작다.
                int adjApprox = (int)MathF.Round((targetCount - 1) * 0.40f);
                int rowApprox = 1 + (int)MathF.Round((targetCount - 1) * 0.28f);
                float hpRatio = tgt.MaxHealth > 0f ? tgt.CurrentHealth / tgt.MaxHealth : 0f;
                var ctx = new TargetContext(mask, hpRatio, tgt.CurrentHealth, tgt.Burn.ProjectedTick,
                                            tgt.Poison.Stack, adjApprox, rowApprox, isBoss: false);
                s = build.ApplyTargetStatus(s, ctx, entry.Spec.Element);

                int pellets = DamageCalculator.EffectivePellets(s.MultiShot, entry.Spec.MultiShot);
                float phys = DamageCalculator.CalcPerShotBeforeCrit(s, entry.Spec.PhysicalBase)
                             * DamageCalculator.CritExpectedMultiplier(s.CritChance, s.CritMultiplier);
                float elem = DamageCalculator.CalcElemental(s, entry.Spec);

                float amp = 1f + tgt.Shock.Magnitude;
                float raw = (phys + elem) * pellets;
                float dealt = raw * amp;
                DbgShots++; DbgRawSum += dealt;

                // ⭐ 오버킬은 버려진다 — 체력을 넘긴 몫은 포인트도 분해 집계도 받지 못한다
                float effective = dealt < tgt.CurrentHealth ? dealt : tgt.CurrentHealth;
                float ratio = dealt > 0f ? effective / dealt : 0f;

                br.Phys += phys * pellets * ratio;
                if (entry.Spec.Element == ElementType.Fire) br.FireHit += elem * pellets * ratio;
                else if (entry.Spec.Element == ElementType.Poison) br.PoisonHit += elem * pellets * ratio;
                else if (entry.Spec.Element == ElementType.Shock) br.ShockHit += elem * pellets * ratio;
                br.ShockAmp += raw * (amp - 1f) * ratio;

                if (tgt.ApplyDamage(dealt))
                {
                    DbgKills++;
                    points += tgt.MaxHealth;   // 처치 포인트 = 그 표적의 최대 체력 (08-07 구조)
                    tgt.Spawn(hp);             // ⭐ 여기서 그 표적의 화상·중독·감전이 전부 사라진다
                    aim = (aim + 1) % targetCount;
                    continue;                  // 죽은 대상에 상태이상을 새로 걸지 않는다
                }

                if (!entry.Spec.HasElement) continue;
                if (entry.Spec.Element == ElementType.Fire)
                    tgt.Burn.Apply(elem * (1f + s.StatusIntensity));
                else if (entry.Spec.Element == ElementType.Poison)
                    tgt.Poison.Apply(elem * (1f + s.StatusIntensity) * pellets);
                else if (entry.Spec.Element == ElementType.Shock)
                    tgt.Shock.ApplyFromHit(DamageCalculator.CalcShockHit(s, entry.Spec), required, s.StatusIntensity);
            }

            // 턴 종료 DoT — 표적마다 따로 틱하고, 여기서 죽어도 포인트가 들어간다
            for (int t = 0; t < targetCount; t++)
            {
                TargetState tt = TargetBuf[t];
                float burnTick = tt.Burn.TickAtTurnEnd();
                float poisonTick = tt.Poison.TickAtTurnEnd();
                float tick = burnTick + poisonTick;
                if (tick > 0f)
                {
                    float eff = tick < tt.CurrentHealth ? tick : tt.CurrentHealth;
                    float r = eff / tick;
                    br.FireDot += burnTick * r;
                    br.PoisonDot += poisonTick * r;
                    if (tt.ApplyDamage(tick))
                    {
                        points += tt.MaxHealth;
                        tt.Spawn(hp);
                        continue;              // Spawn이 감전까지 끊었으므로 아래 Clear 불필요
                    }
                }
                tt.Shock.ClearAtTurnEnd();     // 감전은 해당 턴만
            }
        }
        return points;
    }

    // ══════════════════════════════════════════════
    //  빔 서치
    // ══════════════════════════════════════════════

    private sealed class Scored
    {
        public string GunName;
        public string SeqName;
        public string Axis;
        public int[] Mods;
        public float Total;
        public int ModCost;
        public int AmmoCost;
        public int ReachStage;
        public DamageBreak Break;   // 즉발/DoT 분해 — 축별 진단 출력용
        public int Shots, Kills;
        public float RawSum;
    }

    /// <summary>축별 상위 N — 한 축(예: 독 DoT)이 전체를 독점해도 다른 축의 모드 채택률이 관측되도록 분리 집계.</summary>
    private const int PerAxisKeep = 100;

    private static List<Scored> SearchGun(GunFrame gun, List<ModDef> mods, List<SeqDef> seqs)
    {
        var byAxis = new Dictionary<string, List<Scored>>();
        foreach (string ax in Axes) byAxis[ax] = new List<Scored>();

        foreach (SeqDef seq in seqs) byAxis[seq.Axis].AddRange(SearchSequence(gun, seq, mods));

        var result = new List<Scored>();
        foreach (string ax in Axes)
        {
            List<Scored> l = byAxis[ax];
            l.Sort((a, b) => b.Total.CompareTo(a.Total));
            result.AddRange(l.GetRange(0, Math.Min(l.Count, PerAxisKeep)));
        }
        result.Sort((a, b) => b.Total.CompareTo(a.Total));
        return result;
    }

    private static Dictionary<string, AmmoDef> _ammoById;

    private static List<Scored> SearchSequence(GunFrame gun, SeqDef seq, List<ModDef> mods)
    {
        var build = new RunBuild(10f, gun.MagSize, gun.MagCount, gun.GunSlots, gun.SizeLocked);
        ApplySequence(build, seq, _ammoById, out int ammoCost);

        var current = new List<int[]> { new int[0] };
        int totalSlots = gun.GunSlots + gun.MagSlots;

        for (int step = 0; step < totalSlots; step++)
        {
            var nextKeys = new HashSet<string>();
            var next = new List<KeyValuePair<int[], float>>();

            foreach (int[] c in current)
            {
                int usedGun = 0, usedMag = 0;
                for (int k = 0; k < c.Length; k++) { if (mods[c[k]].IsGunMod) usedGun++; else usedMag++; }

                for (int mi = 0; mi < mods.Count; mi++)
                {
                    if (Contains(c, mi)) continue;
                    bool isGun = mods[mi].IsGunMod;
                    if (isGun && usedGun >= gun.GunSlots) continue;
                    if (!isGun && usedMag >= gun.MagSlots) continue;

                    var arr = new int[c.Length + 1];
                    Array.Copy(c, arr, c.Length);
                    arr[c.Length] = mi;
                    Array.Sort(arr);
                    if (!nextKeys.Add(string.Join(",", arr))) continue;

                    next.Add(new KeyValuePair<int[], float>(arr, Eval(build, mods, arr)));
                }
            }

            if (next.Count == 0) break;
            next.Sort((x, y) => y.Value.CompareTo(x.Value));
            int keep = Math.Min(next.Count, _beam);
            current = new List<int[]>(keep);
            for (int i = 0; i < keep; i++) current.Add(next[i].Key);
        }

        var result = new List<Scored>(current.Count);
        foreach (int[] c in current)
        {
            float total = Eval(build, mods, c, out DamageBreak br);
            int cost = 0;
            for (int k = 0; k < c.Length; k++) cost += mods[c[k]].Price;
            result.Add(new Scored
            {
                GunName = gun.Name, SeqName = seq.Name, Axis = seq.Axis, Mods = c, Total = total,
                ModCost = cost, AmmoCost = ammoCost, Break = br,
                Shots = DbgShots, Kills = DbgKills, RawSum = DbgRawSum,
                ReachStage = ReachStage(build, mods, c),
            });
        }
        return result;
    }

    private static bool Contains(int[] arr, int v)
    {
        for (int i = 0; i < arr.Length; i++) if (arr[i] == v) return true;
        return false;
    }

    private static float Eval(RunBuild build, List<ModDef> mods, int[] picks) => Eval(build, mods, picks, out _);

    private static float Eval(RunBuild build, List<ModDef> mods, int[] picks, out DamageBreak br)
    {
        build.ClearMods();
        for (int i = 0; i < picks.Length; i++) build.EquipMod(mods[picks[i]].Spec);
        return Walk(build, ReferenceRequired, out br);
    }

    /// <summary>스테1부터 연속 클리어 최대치 — 스테이지마다 요구치가 달라 감전 증폭이 바뀌므로 매번 워크한다.</summary>
    private static int ReachStage(RunBuild build, List<ModDef> mods, int[] picks)
    {
        build.ClearMods();
        for (int i = 0; i < picks.Length; i++) build.EquipMod(mods[picks[i]].Spec);

        int reached = 0;
        for (int stage = 1; stage <= 15; stage++)
        {
            float req = StageProgression.GetStage(stage).RequiredDamage;
            if (Walk(build, req) >= req) reached = stage; else break;
        }
        return reached;
    }

    // ══════════════════════════════════════════════
    //  출력
    // ══════════════════════════════════════════════

    private static void PrintGunSection(GunFrame gun, List<Scored> tops, List<ModDef> mods)
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine($" {gun.Name} — 상위 빌드 (총기 {gun.GunSlots}칸 · 탄창 {gun.MagSlots}칸 · 시퀀스 {gun.MagSize}칸 × {gun.MagCount}턴)");
        Console.WriteLine("--------------------------------------------------");

        int n = Math.Min(_top, tops.Count);
        for (int i = 0; i < n; i++)
        {
            Scored s = tops[i];
            Console.WriteLine($"  #{i + 1}  출력 {s.Total,10:N0}  |  도달 스테{s.ReachStage,-2}  |  시퀀스: {s.SeqName}  (모드가 {s.ModCost} + 탄약가 {s.AmmoCost})");
            var gunNames = new List<string>();
            var magNames = new List<string>();
            for (int k = 0; k < s.Mods.Length; k++)
            {
                ModDef m = mods[s.Mods[k]];
                (m.IsGunMod ? gunNames : magNames).Add(m.Name);
            }
            Console.WriteLine($"        총기: {string.Join(" · ", gunNames)}");
            Console.WriteLine($"        탄창: {string.Join(" · ", magNames)}");
        }
    }

    /// <summary>단독 실효 배율 — 모드 1개만 장착했을 때의 최대 배율(전 시퀀스 중 최고)과 그 시퀀스.</summary>
    private static Dictionary<string, KeyValuePair<float, string>> _solo;

    private static void PrintSoloTable(GunFrame refGun, List<ModDef> mods, List<SeqDef> seqs)
    {
        if (refGun == null) return;
        _solo = new Dictionary<string, KeyValuePair<float, string>>();

        foreach (SeqDef seq in seqs)
        {
            var build = new RunBuild(10f, refGun.MagSize, refGun.MagCount, refGun.GunSlots, refGun.SizeLocked);
            ApplySequence(build, seq, _ammoById, out _);

            float base0 = Eval(build, mods, new int[0]);
            if (base0 <= 0f) continue;

            for (int i = 0; i < mods.Count; i++)
            {
                float v = Eval(build, mods, new[] { i }) / base0;
                if (!_solo.TryGetValue(mods[i].Id, out KeyValuePair<float, string> cur) || v > cur.Key)
                    _solo[mods[i].Id] = new KeyValuePair<float, string>(v, seq.Name);
            }
        }
    }

    private static void PrintAxisCeiling(List<Scored> allTop)
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 축별 천장 — 각 빌드 축의 최강 출력 (축 간 격차 = 밸런스 진단)");
        Console.WriteLine("--------------------------------------------------");

        var best = new Dictionary<string, Scored>();
        foreach (Scored s in allTop)
            if (!best.TryGetValue(s.Axis, out Scored cur) || s.Total > cur.Total) best[s.Axis] = s;

        float top = 0f;
        foreach (string ax in Axes) if (best.TryGetValue(ax, out Scored s) && s.Total > top) top = s.Total;

        foreach (string ax in Axes)
        {
            if (!best.TryGetValue(ax, out Scored s)) continue;
            Console.WriteLine($"  {Pad(ax, 6)} {s.Total,10:N0}  ({(top > 0f ? s.Total / top : 0f),5:P0} of 최강)  스테{s.ReachStage,-2}  {s.GunName} / {s.SeqName}");
        }

        // ── 즉발 / DoT 분해 — 축 격차의 '원인'을 읽는 자리 ──
        // 총합만 보면 "독이 세다"까지지만, 나눠 보면 그게 한 발의 크기인지 턴에 걸쳐 쌓인 누적인지가 갈린다.
        // 물리는 DoT가 구조적으로 없으므로(크리는 즉발 배율) 즉발 100%가 정상 — 비교 기준이 된다.
        Console.WriteLine();
        Console.WriteLine("  원소별 즉발 / DoT 분해 (각 축 1위 빌드)");
        Console.WriteLine($"  ※ 표적 1기 체력 {ReferenceHealth:N0} — 발당 실피해가 이걸 넘으면 '발당 1킬'이라 출력이 포화한다");
        Console.WriteLine("  축     |    즉발합  (비중) |     DoT합  (비중) | 킬/발  발당실피해 | 물리    화염    독     전기   | 화상DoT 중독DoT");
        Console.WriteLine("  -------+-------------------+-------------------+-------------------+-------------------------------+----------------");
        foreach (string ax in Axes)
        {
            if (!best.TryGetValue(ax, out Scored s)) continue;
            DamageBreak b = s.Break;
            float t = b.Total > 0f ? b.Total : 1f;
            Console.WriteLine($"  {Pad(ax, 6)} | {b.Hit,9:N0} ({b.Hit / t,4:P0}) | {b.Dot,9:N0} ({b.Dot / t,4:P0}) | {s.Kills,3}/{s.Shots,-3} {(s.Shots > 0 ? s.RawSum / s.Shots : 0f),8:N0} |" +
                              $" {b.Phys,6:N0} {b.FireHit,7:N0} {b.PoisonHit,7:N0} {b.ShockHit,6:N0} |" +
                              $" {b.FireDot,7:N0} {b.PoisonDot,7:N0}");
        }
    }

    private static void PrintPickTable(List<Scored> allTop, List<ModDef> mods)
    {
        // 축별 모집단 — 축마다 따로 세지 않으면 최강 축(DoT)이 전체를 덮어 다른 축 모드가 전부 0%가 된다.
        var pop = new Dictionary<string, int>();
        var count = new Dictionary<string, Dictionary<int, int>>();
        foreach (string ax in Axes) { pop[ax] = 0; count[ax] = new Dictionary<int, int>(); }

        foreach (Scored s in allTop)
        {
            pop[s.Axis]++;
            Dictionary<int, int> c = count[s.Axis];
            for (int k = 0; k < s.Mods.Length; k++)
                c[s.Mods[k]] = c.TryGetValue(s.Mods[k], out int v) ? v + 1 : 1;
        }

        Console.WriteLine();
        Console.WriteLine("==================================================");
        Console.WriteLine(" 모드 분류 — 단독 실효 배율 × 축별 상위 빌드 채택률");
        Console.WriteLine("==================================================");
        Console.WriteLine("  구분   | 모드              | 계열 | 가격 | 단독배율 | 물리  화염   독  전기  혼합 | 최적 시퀀스");
        Console.WriteLine("  -------+-------------------+------+------+----------+-----------------------------+------------------");

        var rows = new List<Row>();
        for (int i = 0; i < mods.Count; i++)
        {
            ModDef m = mods[i];
            float solo = 1f; string seqName = "-";
            if (_solo != null && _solo.TryGetValue(m.Id, out KeyValuePair<float, string> sv)) { solo = sv.Key; seqName = sv.Value; }

            var picks = new float[Axes.Length];
            float maxPick = 0f;
            for (int a = 0; a < Axes.Length; a++)
            {
                int p = pop[Axes[a]];
                picks[a] = p > 0 && count[Axes[a]].TryGetValue(i, out int c) ? (float)c / p : 0f;
                if (picks[a] > maxPick) maxPick = picks[a];
            }
            rows.Add(new Row { Mod = m, Solo = solo, Picks = picks, MaxPick = maxPick, Seq = seqName });
        }

        // 경계 — 단독 1.25배(P5~6 기대치) / 어느 한 축에서라도 채택 25%면 '쓰이는 모드'
        const float SoloCut = 1.25f, PickCut = 0.25f;
        rows.Sort((a, b) => b.MaxPick != a.MaxPick ? b.MaxPick.CompareTo(a.MaxPick) : b.Solo.CompareTo(a.Solo));

        foreach (Row r in rows)
        {
            string quad = r.Solo >= SoloCut
                ? (r.MaxPick >= PickCut ? "핵심" : "함정")
                : (r.MaxPick >= PickCut ? "시너지" : "사각");

            var sb = new StringBuilder();
            for (int a = 0; a < Axes.Length; a++) sb.Append($"{r.Picks[a],5:P0} ");

            Console.WriteLine($"  {Pad(quad, 6)} | {Pad(r.Mod.Name, 17)} | {(r.Mod.IsGunMod ? "총기" : "탄창"),-4} | {r.Mod.Price,4} | {r.Solo,8:0.000} | {sb}| {r.Seq}");
        }

        Console.WriteLine();
        Console.WriteLine($"  핵심   = 단독 ≥{SoloCut:0.00} · 어느 축에서든 채택 ≥{PickCut:P0}  → 건드리지 말 것");
        Console.WriteLine("  시너지 = 단독 낮음 · 채택 높음  → 조합 전용. 정상(의도된 설계)");
        Console.WriteLine("  함정   = 단독 높음 · 채택 낮음  → 단독 성능은 있으나 상위 빌드가 안 고름 (경쟁 모드에 밀림)");
        Console.WriteLine("  사각   = 단독 낮음 · 채택 낮음  → 상향 대상");

        var dead = new List<string>();
        foreach (Row r in rows) if (r.MaxPick <= 0f) dead.Add(r.Mod.Name);
        if (dead.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  ⚠ 전 축 채택 0% ({dead.Count}종): {string.Join(" · ", dead)}");
        }
    }

    private sealed class Row
    {
        public ModDef Mod;
        public float Solo;
        public float[] Picks;
        public float MaxPick;
        public string Seq;
    }

    private static void PrintAmmoTable(List<Scored> allTop, List<SeqDef> seqs)
    {
        var count = new Dictionary<string, int>();
        foreach (Scored s in allTop)
            count[s.SeqName] = count.TryGetValue(s.SeqName, out int c) ? c + 1 : 1;

        var list = new List<KeyValuePair<string, int>>(count);
        list.Sort((a, b) => b.Value.CompareTo(a.Value));

        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 시퀀스 아키타입 채택률 (상위 빌드가 어떤 탄약 구성을 골랐나)");
        Console.WriteLine("--------------------------------------------------");
        int shown = 0;
        foreach (KeyValuePair<string, int> kv in list)
        {
            if (shown++ >= 14) break;
            Console.WriteLine($"  {Pad(kv.Key, 22)} {kv.Value,4}건  ({(float)kv.Value / allTop.Count:P0})");
        }

        var dead = new List<string>();
        foreach (SeqDef s in seqs) if (!count.ContainsKey(s.Name)) dead.Add(s.Name);
        if (dead.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  ⚠ 상위 빌드에 한 번도 못 든 시퀀스 ({dead.Count}종): {string.Join(" · ", dead)}");
        }
    }

    /// <summary>한글 폭 보정 패딩 — 한글은 2칸으로 센다.</summary>
    private static string Pad(string s, int width)
    {
        int w = 0;
        for (int i = 0; i < s.Length; i++) w += s[i] >= 0x1100 ? 2 : 1;
        return w >= width ? s : s + new string(' ', width - w);
    }

    private static void SetAmmoIndex(List<AmmoDef> ammo)
    {
        _ammoById = new Dictionary<string, AmmoDef>();
        foreach (AmmoDef a in ammo) _ammoById[a.Id] = a;
    }
}
