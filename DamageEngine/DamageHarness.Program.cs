using System;
using System.Collections.Generic;
using NightView.Gameplay;
using NightView.Utils;

/// <summary>
/// 데미지 엔진 수치 검증 하니스 (Unity 비의존).
/// GDD 8장 검증표(A/B/C/D)를 재현하고, 가산 vs 승산 성장 곡선을 출력한다.
/// 실행: dotnet run --project Tools/DamageHarness
/// </summary>
internal static class Program
{
    private static int _fails = 0;

    // ── 기대값 보정 헬퍼 ────────────────────────────────────────────────
    // 기본 크리(GameConfig.BaseCritChance·BaseCritMultiplier)는 **물리에만** 걸린다(속성은 크리 미적용).
    // 기대값은 "크리 없는 순수 물리"로 적는 편이 읽기 쉬우므로 비교 직전 여기서 한 번 곱한다.
    // 이렇게 두면 기본 크리 상수를 바꿔도 기대값 전체가 자동으로 따라온다.
    //   ⚠ 2026-07-23 a825856이 BaseCritChance를 0 → 0.10으로 올리면서 하니스 갱신이 누락돼 95건이 죽어 있었다.
    //     다시 숫자를 직접 박지 말 것 — 반드시 Crit()/P()를 거칠 것.

    /// <summary>기본 크리 + 모드 가산분의 기대 배율. 인자 없으면 기본 크리만.</summary>
    private static float Crit(float addChance = 0f, float addMultiplier = 0f)
        => DamageCalculator.CritExpectedMultiplier(GameConfig.BaseCritChance + addChance,
                                                   GameConfig.BaseCritMultiplier + addMultiplier);

    /// <summary>순수 물리 기대값 → 기본 크리 반영값. 속성분은 감싸지 않는다.</summary>
    private static float P(float physical) => physical * Crit();

    /// <summary>부동소수 근사 비교 — AssertBool 안에서 == 대신 쓴다(크리 배율이 곱해지면 딱 떨어지지 않는다).</summary>
    private static bool Near(float actual, float expected) => Math.Abs(actual - expected) < 0.01f;

    private static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("==================================================");
        Console.WriteLine(" GunRogue 데미지 엔진 검증 (GDD 8장)");
        Console.WriteLine(" 기준: 기본댐 10 / 24발 (탄창 6 × 탄창수 4)");
        Console.WriteLine("==================================================\n");

        // A. 가산형 — 증가 +200%
        var a = BuildStats.Create(10f, 6, 4);
        a.Apply(new ModSpec(ModType.Increased, 2.0f));
        Verify("A. 가산형", a, expectedShot: P(30), expectedTotal: P(720));

        // B. 승산 스택 — 증가 +100%, 승산 ×1.5 ×1.5
        var b = BuildStats.Create(10f, 6, 4);
        b.Apply(new ModSpec(ModType.Increased, 1.0f));
        b.Apply(new ModSpec(ModType.More, 0.5f));
        b.Apply(new ModSpec(ModType.More, 0.5f));
        Verify("B. 승산 스택", b, expectedShot: P(45), expectedTotal: P(1080));

        // C. 물량 — 탄창수 2배(48발), 증가 +100%
        var c = BuildStats.Create(10f, 6, 8);
        c.Apply(new ModSpec(ModType.Increased, 1.0f));
        Verify("C. 물량", c, expectedShot: P(20), expectedTotal: P(960));

        // D. 크리 한방 — 크리율 60%(기본 10 + 모드 50) · 배율 3.5배(기본 2.0 + 모드 1.5), 증가 +100%
        var d = BuildStats.Create(10f, 6, 4);
        d.Apply(new ModSpec(ModType.Increased, 1.0f));
        d.Apply(new ModSpec(ModType.CritChance, 0.5f));
        d.Apply(new ModSpec(ModType.CritMultiplier, 1.5f)); // 기본 2.0 + 1.5 = 3.5
        Verify("D. 크리 한방", d, expectedShot: 20f * Crit(0.5f, 1.5f), expectedTotal: 480f * Crit(0.5f, 1.5f));

        // E. 크리율 100% 초과 clamp (2026-08-26 — MOD_AMMO_AUDIT §13.8)
        //    실전투는 `rng < CritChance` 확률 롤이라 100% 초과분이 버려진다. 통계가 clamp를 안 하면
        //    통계·DamageLab·이 하니스가 <b>같은 함수를 공유해 함께</b> 과대평가한다.
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 크리율 clamp — 100% 초과분은 버려진다");
        Console.WriteLine("--------------------------------------------------");
        AssertBool("크리율 140%·배율 3.0 = 3.00배 (clamp 없으면 3.80)",
            Near(DamageCalculator.CritExpectedMultiplier(1.4f, 3.0f), 3.0f));
        AssertBool("크리율 100% 경계 = 3.00배",
            Near(DamageCalculator.CritExpectedMultiplier(1.0f, 3.0f), 3.0f));
        AssertBool("크리율 50% = 2.00배 (100% 미만은 종전 그대로)",
            Near(DamageCalculator.CritExpectedMultiplier(0.5f, 3.0f), 2.0f));

        // 실제로 도달 가능한 조합인지 — 기계식 조준기(+50%) + 조준경(+90%) + 기본 10% = 150%
        var over = BuildStats.Create(10f, 6, 4);
        over.Apply(new ModSpec(ModType.CritChance, 0.50f));
        over.Apply(new ModSpec(ModType.CritChance, 0.90f));
        AssertBool("실 모드 조합으로 크리율 150% 도달 (clamp가 사문이 아님)", over.CritChance > 1f);
        AssertBool("빌드 경로 탄당도 clamp 반영 (배율 = 기본 2.0 그대로)",
            Near(DamageCalculator.CalcPerShot(over), 10f * GameConfig.BaseCritMultiplier));

        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 가산 vs 승산 성장 곡선 (기본댐 10, 24발 고정)");
        Console.WriteLine(" 가산=단계마다 +50%증가 누적 / 승산=단계마다 ×1.5 누적");
        Console.WriteLine("--------------------------------------------------");
        GrowthCurve();
        BuildScenario();
        AccessoryScenario();
        CombatScenario();
        EconomyScenario();
        LoadoutScenario();
        ShopRulesScenario();
        WeightScenario();
        SequenceScenario();
        AmmoLoadoutScenario();
        ElementalScenario();
        BurnScenario();
        TriggerScenario();
        StageScenario();
        GunDiversityScenario();
        ConditionScenario();
        Phase3ConditionScenario();
        ElementalAxisScenario();
        StatusPipelineScenario();
        BossScenario();
        TargetModelScenario();
        TargetConditionScenario();
        GunBalanceScenario();
        BossSeedBalanceScenario(); // 시드 셔플 + 보스 패시브 반영 12완주 도달성 (반드시 마지막 — SetRunSeed가 정적 _order 오염)

        Console.WriteLine();
        Console.WriteLine(_fails == 0
            ? "✅ 전체 검증 통과 — 공식·층2 모두 기대값 일치"
            : $"❌ 검증 실패 {_fails}건");

        Environment.Exit(_fails == 0 ? 0 : 1);
    }

    private static void Verify(string name, in BuildStats s, float expectedShot, float expectedTotal)
    {
        var r = DamageCalculator.Calc(s);
        bool okShot  = Math.Abs(r.PerShot - expectedShot) < 0.01f;
        bool okTotal = Math.Abs(r.StageTotal - expectedTotal) < 0.01f;
        if (!okShot) _fails++;
        if (!okTotal) _fails++;

        Console.WriteLine($"[{name}]");
        Console.WriteLine($"  단계: (기본+가산)={r.AfterFlat:0.##} → ×증가={r.AfterIncreased:0.##} → ×승산={r.AfterMore:0.##} → ×크리({r.CritMult:0.##})");
        Console.WriteLine($"  탄당 {r.PerShot:0.##} (기대 {expectedShot}) {(okShot ? "OK" : "FAIL")}"
                        + $"  |  총 {r.StageTotal:0.##} (기대 {expectedTotal}) {(okTotal ? "OK" : "FAIL")}"
                        + $"  [{r.TotalShots}발]");
        Console.WriteLine();
    }

    private static void GrowthCurve()
    {
        Console.WriteLine(" 투자 | 가산형 탄당 / 총        | 승산형 탄당 / 총");
        for (int i = 1; i <= 6; i++)
        {
            var add = BuildStats.Create(10f, 6, 4);
            add.Apply(new ModSpec(ModType.Increased, 0.5f * i));
            var ra = DamageCalculator.Calc(add);

            var mul = BuildStats.Create(10f, 6, 4);
            for (int k = 0; k < i; k++) mul.Apply(new ModSpec(ModType.More, 0.5f));
            var rm = DamageCalculator.Calc(mul);

            Console.WriteLine($"  {i}회 | {ra.PerShot,7:0.#} / {ra.StageTotal,6:0}        | {rm.PerShot,7:0.#} / {rm.StageTotal,6:0}");
        }
    }

    // 모드 장착 → 출력 즉시 갱신 + 슬롯 한도 (M0 Exit 검증)
    private static void BuildScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 모드 장착 → 출력 즉시 갱신 (RunBuild)");
        Console.WriteLine(" 소총(탄창8·슬롯6) + 철갑탄(10) + 탄창수4 = 32발");
        Console.WriteLine("--------------------------------------------------");

        var build = new RunBuild(baseDamage: 10f, magazineSize: 8, magazineCount: 4, slotCount: 6);
        PrintBuild(build, "(빈 빌드)");

        build.EquipMod(new ModSpec(ModType.Flat, 5f));           PrintBuild(build, "+가산 5");
        build.EquipMod(new ModSpec(ModType.Increased, 0.5f));    PrintBuild(build, "+증가 50%");
        build.EquipMod(new ModSpec(ModType.More, 0.5f));         PrintBuild(build, "+승산 50%");
        build.EquipMod(new ModSpec(ModType.CritChance, 0.5f));   PrintBuild(build, "+크리율 50% (기본배율2.0)");
        build.EquipMod(new ModSpec(ModType.CritMultiplier, 0.5f)); PrintBuild(build, "+크리배율 +0.5 (→2.5)");
        build.EquipMod(new ModSpec(ModType.More, 0.5f));         PrintBuild(build, "+승산 50% (2번째)");

        bool overflow = build.EquipMod(new ModSpec(ModType.Flat, 5f));
        Console.WriteLine($"  +가산 5 (슬롯 초과 시도)         → {(overflow ? "장착됨(FAIL)" : "거부됨 OK")}  [{build.EquippedCount}/{build.SlotCount}]");
        if (overflow) _fails++;

        // MOD_SPLIT Phase1 — 위 6종 전부 상시(Trigger=None·조건 없음) = 전부 탄창 모드로 분류.
        // 탄창 모드 슬롯 = 8÷2 = 4칸이라 앞 4개(가산·증가·승산·크리율)만 장착되고 이후는 거부.
        // 크리 전 = (10+5)×1.5×1.5 × 32발 = 1080 → 크리율(기본10 + 모드50)·배율 2.0 반영.
        AssertNear("RunBuild 최종 총 데미지", build.GetResult().StageTotal, 1080f * Crit(0.5f));
    }

    private static void PrintBuild(RunBuild b, string label)
    {
        var r = b.GetResult();
        Console.WriteLine($"  {label,-28} 탄당 {r.PerShot,7:0.##} | 턴당 {r.PerTurn(b.MagazineSize),7:0.##} | 총 {r.StageTotal,7:0.##}  [{b.EquippedCount}/{b.SlotCount}]");
    }

    private static void AssertNear(string name, float actual, float expected)
    {
        bool ok = Math.Abs(actual - expected) < 0.01f;
        if (!ok) _fails++;
        Console.WriteLine($"  ▶ {name} = {actual:0.##} (기대 {expected}) {(ok ? "OK" : "FAIL")}");
    }

    // 악세서리 → 유효 탄창(크기·수) 반영 (1-B: 무제한 장착)
    private static void AccessoryScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 악세서리 → 유효 탄창 반영 (RunBuild)");
        Console.WriteLine(" 소총(탄창8·수4=32발) + 확장탄창(+2수) + 속사장전(+4크기)");
        Console.WriteLine("--------------------------------------------------");

        var b = new RunBuild(10f, 8, 4, 6);
        PrintMag(b, "(악세 없음)");
        AssertBool("기본 유효 탄창 = 골격값 (8 × 4 = 32발)", b.MagazineSize == 8 && b.MagazineCount == 4 && b.GetResult().TotalShots == 32);

        b.EquipAccessory(new AccessorySpec(AccessoryType.MagazineCount, 2)); // 확장탄창 +2 hands
        PrintMag(b, "+확장탄창(+2수)");
        AssertBool("확장탄창 → 탄창 수 6, 48발", b.MagazineCount == 6 && b.GetResult().TotalShots == 48);

        b.EquipAccessory(new AccessorySpec(AccessoryType.MagazineSize, 4)); // 속사장전 +4 크기
        PrintMag(b, "+속사장전(+4크기)");
        AssertBool("속사장전 → 탄창 크기 12, 72발", b.MagazineSize == 12 && b.GetResult().TotalShots == 72);

        // 무제한 장착 — 같은 악세 중첩 합산
        b.EquipAccessory(new AccessorySpec(AccessoryType.MagazineCount, 1)); // 확장탄창 1개 더
        AssertBool("악세 중첩 합산 (수 7, 84발)", b.MagazineCount == 7 && b.GetResult().TotalShots == 84);

        AssertNear("악세 반영 총 데미지(탄당10 × 84발 × 기본크리)", b.GetResult().StageTotal, P(840f));
    }

    private static void PrintMag(RunBuild b, string label)
    {
        var r = b.GetResult();
        Console.WriteLine($"  {label,-22} 크기 {b.MagazineSize,2} × 수 {b.MagazineCount,2} = {r.TotalShots,3}발 | 총 {r.StageTotal,6:0}");
    }

    // 전투 종료 판정 + 런 진행 (M0 Exit: 클리어/실패 양쪽)
    private static void CombatScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 전투 종료 판정 + 런 진행 (CombatResolver)");
        Console.WriteLine($" 요구치 곡선: 스테1 {StageProgression.GetRequired(1):0} / 스테2 {StageProgression.GetRequired(2):0} / 스테3 {StageProgression.GetRequired(3):0}");
        Console.WriteLine("--------------------------------------------------");

        // 빈빌드 — 강화 없음 (1스테는 강화 없이 클리어 가능해야)
        var empty = new RunBuild(10f, 8, 4, 6);
        RunStages("빈빌드(강화無)", empty, 3);

        // 약빌드 — 가산 + 증가만 (선형)
        var weak = new RunBuild(10f, 8, 4, 6);
        weak.EquipMod(new ModSpec(ModType.Flat, 5f));
        weak.EquipMod(new ModSpec(ModType.Increased, 0.5f));
        RunStages("약빌드(가산+증가)", weak, 3);

        // 강빌드 — 승산 스택 (지수)
        var strong = new RunBuild(10f, 8, 4, 6);
        strong.EquipMod(new ModSpec(ModType.Flat, 5f));
        strong.EquipMod(new ModSpec(ModType.Increased, 1.0f));
        for (int i = 0; i < 4; i++) strong.EquipMod(new ModSpec(ModType.More, 0.5f));
        RunStages("강빌드(승산스택)", strong, 3);

        AssertBool("빈빌드 스테1 클리어 (강화 없이 가능)", CombatResolver.ResolveExpected(empty.ToBuildStats(), StageProgression.GetRequired(1)).Cleared);
        AssertBool("빈빌드 스테2 클리어 (무모드 — 1·2스테 램프)", CombatResolver.ResolveExpected(empty.ToBuildStats(), StageProgression.GetRequired(2)).Cleared);
        AssertBool("강빌드 스테3 클리어 (런 완주)", CombatResolver.ResolveExpected(strong.ToBuildStats(), StageProgression.GetRequired(3)).Cleared);
    }

    private static void RunStages(string label, RunBuild build, int stageCount)
    {
        var stats = build.ToBuildStats();
        Console.WriteLine($" [{label}] 탄당 {DamageCalculator.CalcPerShot(stats):0.#} · 총출력 {build.GetResult().StageTotal:0} ({stats.TotalShots}발)");
        for (int i = 1; i <= stageCount; i++)
        {
            float req = StageProgression.GetRequired(i);
            var o = CombatResolver.ResolveExpected(stats, req);
            Console.WriteLine(o.Cleared
                ? $"   스테{i}(요구 {req:0}): 클리어 ({o.TurnsUsed}/{o.TotalTurns}턴, 누적 {o.Accumulated:0})"
                : $"   스테{i}(요구 {req:0}): 실패 (누적 {o.Accumulated:0}, {o.Progress:P0}) → 런 종료");
            if (!o.Cleared) break;
        }
    }

    private static void AssertBool(string name, bool condition)
    {
        if (!condition) _fails++;
        Console.WriteLine($"  ▶ {name} {(condition ? "OK" : "FAIL")}");
    }

    // 재화 — 잔여 탄창 보너스 (1-A: RunState 경제)
    private static void EconomyScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 재화 — 잔여 탄창 보너스 (RunState)");
        Console.WriteLine($" 공식: 기본 {GameConfig.StageClearBaseReward} + 잔여탄창 × {GameConfig.UnusedTurnBonus}");
        Console.WriteLine("--------------------------------------------------");

        // 1) 형식 검증 — 잔여 탄창 많을수록 보상 증가(단조), 음수 방어, 카운터 증가
        var run = new RunState();
        int rewardFast = run.AwardStageClear(3);                 // 빠른 클리어(3턴 남김)
        int rewardSlow = new RunState().AwardStageClear(0);      // 막판 클리어
        AssertBool("빠른 클리어 보상 > 막판 클리어 보상", rewardFast > rewardSlow);
        AssertBool("음수 잔여 탄창 방어(= 0턴 보상)", new RunState().AwardStageClear(-5) == rewardSlow);
        AssertBool("StagesCleared 증가", run.StagesCleared == 1);

        // 2) 소비 검증 — TrySpend (부족 거부 / 보유 내 성공 / 차감)
        AssertBool("보유보다 비싼 구매 거부", !run.TrySpend(run.Currency + 1));
        int before = run.Currency;
        AssertBool("보유 내 구매 성공", run.TrySpend(before));
        AssertBool("구매 후 재화 0", run.Currency == 0);

        // 3) 플레이스루 시뮬 — 강빌드로 3스테 진행, 스테이지별 보상 출력
        Console.WriteLine();
        var playRun = new RunState();
        var strong = new RunBuild(10f, 8, 4, 6);
        strong.EquipMod(new ModSpec(ModType.Flat, 5f));
        strong.EquipMod(new ModSpec(ModType.Increased, 1.0f));
        for (int i = 0; i < 4; i++) strong.EquipMod(new ModSpec(ModType.More, 0.5f));
        var stats = strong.ToBuildStats();

        for (int s = 1; s <= 3; s++)
        {
            var o = CombatResolver.ResolveExpected(stats, StageProgression.GetRequired(s));
            if (!o.Cleared) { Console.WriteLine($"   스테{s}: 실패 → 런 종료"); break; }
            int unused = o.TotalTurns - o.TurnsUsed;
            int rew = playRun.AwardStageClear(unused);
            Console.WriteLine($"   스테{s}: 클리어 ({o.TurnsUsed}/{o.TotalTurns}턴, 잔여 {unused}) → 보상 +{rew}, 누적 재화 {playRun.Currency}");
        }
        Console.WriteLine($"   ▶ 런 종료 — 총 획득 {playRun.TotalEarned}, 클리어 {playRun.StagesCleared}스테");
    }

    // 장비 권한 — RunLoadout (1-C 2단계: UI_SHOP §12.8 검증 포인트)
    private static void LoadoutScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 장비 권한 — RunLoadout (A안: 장착·보관·판매·프리뷰)");
        Console.WriteLine($" 보관 {GameConfig.ModStashSize}칸 · 환급 이번방문 {GameConfig.ResaleSameTurn:P0} / 이전 {GameConfig.ResalePast:P0} · 악세 환급 0");
        Console.WriteLine("--------------------------------------------------");

        // 1) 회귀 — RunLoadout 경유 장착 = 기존 RunBuild.EquipMod 직접 경로와 동일 결과 (BuildScenario 구성 = 2430)
        var direct = new RunBuild(10f, 8, 4, 6);
        direct.EquipMod(new ModSpec(ModType.Flat, 5f));
        direct.EquipMod(new ModSpec(ModType.Increased, 0.5f));
        direct.EquipMod(new ModSpec(ModType.More, 0.5f));
        direct.EquipMod(new ModSpec(ModType.CritChance, 0.5f));
        direct.EquipMod(new ModSpec(ModType.CritMultiplier, 0.5f));
        direct.EquipMod(new ModSpec(ModType.More, 0.5f));

        var loadout = new RunLoadout(new RunBuild(10f, 8, 4, 6));
        loadout.EquipMod(new ModEntry(new ModSpec(ModType.Flat, 5f), 6, true));
        loadout.EquipMod(new ModEntry(new ModSpec(ModType.Increased, 0.5f), 8, true));
        loadout.EquipMod(new ModEntry(new ModSpec(ModType.More, 0.5f), 15, true));
        loadout.EquipMod(new ModEntry(new ModSpec(ModType.CritChance, 0.5f), 9, true));
        loadout.EquipMod(new ModEntry(new ModSpec(ModType.CritMultiplier, 0.5f), 9, true));
        loadout.EquipMod(new ModEntry(new ModSpec(ModType.More, 0.5f), 15, true));
        AssertNear("회귀: RunLoadout = 직접 EquipMod 경로", loadout.GetResult().StageTotal, direct.GetResult().StageTotal);
        AssertBool("슬롯 풀(6/6) 장착 거부", !loadout.EquipMod(new ModEntry(new ModSpec(ModType.Flat, 5f), 6, true)));

        // 2) 기존 빌드 흡수 — M0 경로(시작 모드 직접 장착) 호환
        // MOD_SPLIT Phase1 — Flat 모드(상시·조건 없음)는 탄창 모드로 분류돼 _magSlots로 흡수됨.
        var preEquipped = new RunBuild(10f, 8, 4, 6);
        preEquipped.EquipMod(new ModSpec(ModType.Flat, 5f));
        var absorbed = new RunLoadout(preEquipped);
        AssertBool("기존 장착 모드 흡수 (탄창 슬롯 1 · 크리 전 480 유지)",
            absorbed.EquippedMagModCount == 1 && Math.Abs(absorbed.GetResult().StageTotal - P(480f)) < 0.01f);

        // 3) 프리뷰 — 현재 불변 + 장착(양수)/판매(음수) 델타 (§5.3)
        // MOD_SPLIT Phase1 — Flat/More(상시·조건 없음)는 탄창 모드로 분류돼 EquipMod가 _magSlots로 라우팅.
        // 이 절은 카테고리 자체가 아니라 프리뷰·보관·환급의 범용 동작 검증이 목적이라 Mag* API로 미러링.
        var lo = new RunLoadout(new RunBuild(10f, 8, 4, 6));
        lo.EquipMod(new ModEntry(new ModSpec(ModType.Flat, 5f), 6, true)); // 탄당 15 → 총 480 (탄창 슬롯 0)
        float current = lo.GetResult().StageTotal;
        AssertNear("프리뷰(승산50% 장착 가정) 델타 크리 전 +240", lo.PreviewEquipMod(new ModSpec(ModType.More, 0.5f)).StageTotal - current, P(240f));
        AssertNear("프리뷰(판매 가정) 델타 크리 전 -160", lo.PreviewRemoveMagMod(0).StageTotal - current, P(-160f));
        AssertNear("프리뷰 후 현재 출력 불변", lo.GetResult().StageTotal, current);

        // 4) 보관함 폐지 (GameConfig.ModStashSize = 0 — 현 레이아웃에 보관 UI 없음)
        //    보관 경로는 전부 거부되고 슬롯·출력이 그대로 유지되는 것이 현재 계약이다.
        //    ⚠ 보관함을 되살리면 이 절을 왕복(탈장착→보관→재장착) 검증으로 되돌릴 것.
        var filler = new ModEntry(new ModSpec(ModType.Flat, 1f), 3, false);
        AssertBool("모드 보관함 폐지 (0칸)", GameConfig.ModStashSize == 0 && !lo.HasFreeStash);
        AssertBool("탈장착 → 보관 거부 (슬롯·출력 유지)",
            !lo.MoveMagSlotToStash(0) && Math.Abs(lo.GetResult().StageTotal - P(480f)) < 0.01f);
        AssertBool("보관 직접 추가 거부", !lo.StashMod(filler));

        // 5) 재판매 환급 — 판매 배율 단일(무르기 삭제 §6, 2026-07-14) + 20% 반올림
        var bought = new ModEntry(new ModSpec(ModType.More, 0.5f), 15, true);
        AssertBool("구매 후 판매도 배율 20% (무르기 삭제 15→3)", RunLoadout.GetRefund(bought) == 3);
        lo.EquipMod(bought);      // 탄창 슬롯 [Flat5, More15]
        lo.ResetVisitFlags();     // 다음 스테이지 진입 (환급엔 영향 없음 — 무르기 삭제)
        AssertBool("스테이지 넘긴 뒤도 동일 20% (15→3)", RunLoadout.GetRefund(lo.MagSlots[1]) == 3);
        AssertBool("20% 환급 반올림 (9→2)", RunLoadout.GetRefund(new ModEntry(default, 9, false)) == 2);
        int refund = lo.SellMagSlotMod(1);
        AssertBool("판매 → 환급 3 + 출력 복귀 (크리 전 480)", refund == 3 && Math.Abs(lo.GetResult().StageTotal - P(480f)) < 0.01f);

        // 6) 악세 — entry 경유 탄창 반영 + 프리뷰 + 판매금 0
        var acc = new RunLoadout(new RunBuild(10f, 8, 4, 6));
        AssertBool("악세 프리뷰 (32→48발, 현재 불변)",
            acc.PreviewEquipAccessory(new AccessorySpec(AccessoryType.MagazineCount, 2)).TotalShots == 48
            && acc.GetResult().TotalShots == 32);
        acc.EquipAccessory(new AccessoryEntry(new AccessorySpec(AccessoryType.MagazineCount, 2), 12, true));
        AssertBool("악세 장착 (48발)", acc.GetResult().TotalShots == 48);
        AssertBool("악세 판매금 0 + 제거 (48→32발)", acc.SellAccessory(0) == 0 && acc.GetResult().TotalShots == 32);
    }

    // 상점 규칙 — 리롤비 누적·악세 주기 + 환급 적립 (1-C 3단계: UI_SHOP §12.8 검증 포인트)
    private static void ShopRulesScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 상점 규칙 — 리롤비 누적·악세 주기 (ShopRules)");
        Console.WriteLine($" 리롤 {GameConfig.RerollCostStart} 시작/+{GameConfig.RerollCostStep} · 악세 주기 {GameConfig.AccessoryRefreshCycle}스테");
        Console.WriteLine("--------------------------------------------------");

        // 리롤비 — 상점 내 선형 누적 (초기화는 ShopManager.Open이 _rerollsUsed=0으로 수행)
        AssertBool("리롤비 시작값", ShopRules.GetRerollCost(0) == GameConfig.RerollCostStart);
        AssertBool("리롤비 2회 누적", ShopRules.GetRerollCost(2) == GameConfig.RerollCostStart + 2 * GameConfig.RerollCostStep);

        // 악세 주기 — 최초/직전/도달/재대기 경계
        int cycle = GameConfig.AccessoryRefreshCycle;
        AssertBool("악세 최초 오픈 = 갱신", ShopRules.ShouldRefreshAccessories(1, 0));
        AssertBool("주기 직전 미갱신", !ShopRules.ShouldRefreshAccessories(1 + cycle - 1, 1));
        AssertBool("주기 도달 = 갱신", ShopRules.ShouldRefreshAccessories(1 + cycle, 1));
        AssertBool("갱신 직후 재대기", !ShopRules.ShouldRefreshAccessories(1 + cycle + 1, 1 + cycle));

        // 환급 적립 — RunState.Earn (판매 → 재화)
        var run = new RunState();
        run.AwardStageClear(0);
        int baseline = run.Currency;
        run.Earn(3);
        AssertBool("판매 환급 적립 (+3)", run.Currency == baseline + 3 && run.TotalEarned == baseline + 3);
        run.Earn(0);
        run.Earn(-5);
        AssertBool("0/음수 적립 무시", run.Currency == baseline + 3);
    }

    // 희귀도 가중 추첨 — 주입식 seam (Phase 2: UI_SHOP §8.1)
    private static void WeightScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 희귀도 가중 추첨 (WeightedRoll)");
        Console.WriteLine($" 가중치 흔함 {GameConfig.RarityWeightCommon} / 비범 {GameConfig.RarityWeightUncommon} / 희귀 {GameConfig.RarityWeightRare}"
                        + $" / 에픽 {GameConfig.RarityWeightEpic} / 전설 {GameConfig.RarityWeightLegendary}");
        Console.WriteLine("--------------------------------------------------");

        // 등급 폴백 — 신규 등급을 append하고 RarityWeight의 case를 빠뜨리면 default(흔함)로 흘러 조용히 오작동한다.
        // 사다리가 단조 감소인지로 그 누락을 잡는다 (2026-08-26 Legendary 신설).
        AssertBool("등급 가중 폴백 = 단조 감소 (흔함 > 비범 > 희귀 > 에픽 > 전설)",
            GameConfig.RarityWeight(Rarity.Common) > GameConfig.RarityWeight(Rarity.Uncommon)
            && GameConfig.RarityWeight(Rarity.Uncommon) > GameConfig.RarityWeight(Rarity.Rare)
            && GameConfig.RarityWeight(Rarity.Rare) > GameConfig.RarityWeight(Rarity.Epic)
            && GameConfig.RarityWeight(Rarity.Epic) > GameConfig.RarityWeight(Rarity.Legendary));
        AssertBool("전설 = 흔함 폴백이 아님 (case 누락 검출)",
            GameConfig.RarityWeight(Rarity.Legendary) != GameConfig.RarityWeight(Rarity.Common));

        var w = new float[] { 60f, 30f, 10f };
        AssertBool("roll 0 → 흔함(0)", WeightedRoll.PickIndex(w, 0.0) == 0);
        AssertBool("roll 0.59 → 흔함(0)", WeightedRoll.PickIndex(w, 0.59) == 0);
        AssertBool("roll 0.61 → 비범(1)", WeightedRoll.PickIndex(w, 0.61) == 1);
        AssertBool("roll 0.95 → 희귀(2)", WeightedRoll.PickIndex(w, 0.95) == 2);
        AssertBool("roll≈1 경계 → 마지막(2)", WeightedRoll.PickIndex(w, 0.999999) == 2);

        // 가중치 0 = 절대 미등장 / 전체 0 = -1
        var w0 = new float[] { 60f, 0f, 10f };
        bool neverMid = true;
        for (double r = 0; r < 1; r += 0.005) if (WeightedRoll.PickIndex(w0, r) == 1) neverMid = false;
        AssertBool("가중치 0 = 절대 미등장", neverMid);
        AssertBool("전체 0 → -1", WeightedRoll.PickIndex(new float[] { 0f, 0f }, 0.5) == -1);

        // 분포 — 흔함 > 비범 > 희귀 (고정 시드)
        var rng = new Random(12345);
        var items = new List<int> { 0, 1, 2 };
        float[] rw = { GameConfig.RarityWeightCommon, GameConfig.RarityWeightUncommon, GameConfig.RarityWeightRare };
        int[] hits = new int[3];
        var buf = new List<int>();
        for (int i = 0; i < 3000; i++)
        {
            WeightedRoll.Pick(items, x => rw[x], 1, () => rng.NextDouble(), buf);
            if (buf.Count > 0) hits[buf[0]]++;
        }
        Console.WriteLine($"  분포 3000회: 흔함 {hits[0]} / 비범 {hits[1]} / 희귀 {hits[2]}");
        AssertBool("분포: 흔함 > 비범 > 희귀", hits[0] > hits[1] && hits[1] > hits[2]);

        // 비복원 distinct (균등 weightOf=null)
        var pool = new List<int> { 0, 1, 2, 3, 4 };
        var res = new List<int>();
        WeightedRoll.Pick(pool, null, 3, () => rng.NextDouble(), res);
        bool distinct = res.Count == 3 && res[0] != res[1] && res[1] != res[2] && res[0] != res[2];
        AssertBool("비복원 3개 distinct", distinct);
        WeightedRoll.Pick(pool, null, 99, () => rng.NextDouble(), res); // 요청 > 후보 → 전부
        AssertBool("요청 초과 시 후보 전부(5개)", res.Count == 5);
    }

    // 탄창 시퀀스 ⭐ — 탄별 차등 합산·빈 칸·스왑·기본탄 보충 (Phase 2 1단계)
    private static void SequenceScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 탄창 시퀀스 ⭐ — 탄별 차등 합산 (MagazineSequence)");
        Console.WriteLine(" 소총(탄창8·수4) 기본탄10 + 특수탄20 혼합");
        Console.WriteLine("--------------------------------------------------");

        // 1) 회귀 — 균일 기본탄 시퀀스 = 기존 단일 계산과 동일
        var uni = new RunBuild(10f, 8, 4, 6);
        AssertBool("기본탄 시퀀스 자동 구성 (8칸·기본탄)", uni.Sequence.Size == 8 && uni.Sequence.Slots[0].IsDefault);
        AssertNear("회귀: 균일 시퀀스 총 데미지 (크리 전 320)", uni.GetResult().StageTotal, P(320f));

        var modded = new RunBuild(10f, 8, 4, 6);
        modded.EquipMod(new ModSpec(ModType.Flat, 5f));
        modded.EquipMod(new ModSpec(ModType.Increased, 0.5f));
        modded.EquipMod(new ModSpec(ModType.More, 0.5f));
        modded.EquipMod(new ModSpec(ModType.CritChance, 0.5f));
        modded.EquipMod(new ModSpec(ModType.CritMultiplier, 0.5f));
        modded.EquipMod(new ModSpec(ModType.More, 0.5f));
        // MOD_SPLIT Phase1 — 전부 탄창 모드(8÷2=4칸)라 앞 4개만 반영 (BuildScenario와 동일 구성)
        AssertNear("회귀: 모드 6종 균일 시퀀스 (크리 전 1080)", modded.GetResult().StageTotal, 1080f * Crit(0.5f));

        // 2) 혼합 시퀀스 — 특수탄(기본댐 20) 2발: 턴당 = 6×10 + 2×20 = 100
        var mix = new RunBuild(10f, 8, 4, 6);
        var special = new AmmoEntry(new AmmoSpec(20f), price: 5, boughtThisVisit: true);
        AssertBool("특수탄 배치 (0·3번 칸)", mix.Sequence.Set(0, special, out _) && mix.Sequence.Set(3, special, out _));
        AssertNear("혼합 턴당 (크리 전 6×10 + 2×20 = 100)", mix.GetResult().PerTurn(mix.MagazineSize), P(100f));
        AssertNear("혼합 총 데미지 (크리 전 100 × 4턴 = 400)", mix.GetResult().StageTotal, P(400f));

        mix.EquipMod(new ModSpec(ModType.Increased, 1.0f)); // 증가 100% — 탄별 동일 적용
        AssertNear("혼합 + 증가100% (크리 전 턴당 200·총 800)", mix.GetResult().StageTotal, P(800f));

        // 3) 빈 칸 — 턴당 합산서 제외 (발사 데미지 0)
        AssertBool("칸 비우기 (1번 → 빈 칸)", mix.Sequence.Set(1, AmmoEntry.CreateEmpty(), out _));
        AssertNear("빈 칸 제외 (크리 전 턴당 180 × 4 = 720)", mix.GetResult().StageTotal, P(720f));

        // 4) 스왑(재배열 확정안) — 순서 변경, 합 불변
        AssertBool("스왑 (0 ↔ 7)", mix.Sequence.Swap(0, 7));
        AssertBool("스왑 결과: 7번 특수탄·0번 기본탄", mix.Sequence.Slots[7].IsSpecial && mix.Sequence.Slots[0].IsDefault);
        AssertNear("스왑 후 총합 불변 (크리 전 720)", mix.GetResult().StageTotal, P(720f));

        // 5) 미동기화 직접 경로 — 시퀀스보다 큰 유효 탄창은 기본탄 보충 (M0·기존 하니스 호환)
        var direct = new RunBuild(10f, 8, 4, 6);
        direct.EquipAccessory(new AccessorySpec(AccessoryType.MagazineSize, 4)); // 유효 12칸 — 시퀀스는 8칸 유지
        AssertNear("초과분 기본탄 보충 (크리 전 12발 × 10 × 4턴 = 480)", direct.GetResult().StageTotal, P(480f));

        // 6) 시퀀스 판정 — 턴당 직접 입력 오버로드 (CombatResolver)
        var clear = CombatResolver.ResolveExpected(100f, 4, 350f);
        AssertBool("턴당 100·요구 350 = 4턴 클리어", clear.Cleared && clear.TurnsUsed == 4);
        AssertBool("턴당 100·요구 401 = 실패", !CombatResolver.ResolveExpected(100f, 4, 401f).Cleared);
    }

    // 탄약 권한 — RunLoadout (Phase 2 1단계 ②: 배치·보관·크기 동기화·환급)
    private static void AmmoLoadoutScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 탄약 권한 — RunLoadout (배치·보관·동기화·환급)");
        Console.WriteLine($" 보관 = 기본탄창 × {GameConfig.AmmoStashRatio:P0} (0% = 보관함 폐지) · 환급 = 모드와 동일 규칙");
        Console.WriteLine("--------------------------------------------------");

        var lo = new RunLoadout(new RunBuild(10f, 8, 4, 6));
        AssertBool("탄약 보관함 폐지 (0칸)", lo.AmmoStashSize == 0 && !lo.HasFreeAmmoStash);

        var sp20 = new AmmoEntry(new AmmoSpec(20f), 5, true); // 환급 = round(5 × 20%) = 1

        // 1) 배치 — 기본탄 교체(소멸·환급 0) + 출력 즉시 반영
        AssertBool("배치 (기존 기본탄 소멸)", lo.PlaceAmmo(0, sp20, out int r0) && r0 == 0 && lo.Sequence.Slots[0].IsSpecial);
        AssertNear("배치 반영 턴당 (크리 전 7×10 + 20 = 90)", lo.GetResult().PerTurn(lo.Build.MagazineSize), P(90f));

        // 2) 점유 칸 재배치 → 보관함이 없으므로 기존 특수탄은 자동 판매(20% 환급)
        AssertBool("점유 칸 배치 → 기존 탄 자동 판매 (5 → 1)",
            lo.PlaceAmmo(0, sp20, out int r1) && r1 == 1 && lo.AmmoStash.Count == 0);

        // 3) 보관 이동 — 전 경로 거부 (칸 상태 불변). ⚠ 보관함 부활 시 왕복 검증으로 되돌릴 것
        AssertBool("칸 → 보관 거부 (칸 유지)", !lo.MoveAmmoSlotToStash(0) && lo.Sequence.Slots[0].IsSpecial);
        AssertBool("보관 → 칸 거부 (보관 비어 있음)", !lo.MoveAmmoStashToSlot(0, 1));
        AssertBool("보관 직접 추가 거부 (탭 구매 경로)", !lo.StashAmmo(sp20));

        // 4) 판매 — 칸은 빈 칸으로(자동 충전 X) + 납탄 +0(비우기 역할)
        AssertBool("칸 특수탄 판매 (+1·빈 칸, 20%)", lo.SellAmmoSlot(0) == 1 && lo.Sequence.Slots[0].IsEmpty);
        AssertBool("납탄 칸 판매 (+0·빈 칸 = 비우기)", lo.SellAmmoSlot(3) == 0 && lo.Sequence.Slots[3].IsEmpty);

        // 5) 크기 동기화 — 악세 확장(기본탄 충전) / 판매 축소(보관 폐지 → 특수탄 자동 판매)
        var lo2 = new RunLoadout(new RunBuild(10f, 8, 4, 6));
        lo2.EquipAccessory(new AccessoryEntry(new AccessorySpec(AccessoryType.MagazineSize, 4), 12, true));
        AssertBool("확장 동기화 (12칸·새 칸 기본탄)", lo2.Sequence.Size == 12 && lo2.Sequence.Slots[11].IsDefault);
        lo2.PlaceAmmo(9, sp20, out _); // 확장 영역에 특수탄
        AssertBool("악세 판매 → 축소·특수탄 자동 판매 (+1)",
            lo2.SellAccessory(0) == 1 && lo2.Sequence.Size == 8 && lo2.AmmoStash.Count == 0);

        // 6) 축소 초과분 — 보관함이 없으니 전량 자동 판매 (3발 × 1)
        var lo3 = new RunLoadout(new RunBuild(10f, 8, 4, 6));
        lo3.EquipAccessory(new AccessoryEntry(new AccessorySpec(AccessoryType.MagazineSize, 4), 12, true));
        lo3.PlaceAmmo(8, sp20, out _);
        lo3.PlaceAmmo(9, sp20, out _);
        lo3.PlaceAmmo(10, sp20, out _);
        int syncRefund = lo3.SellAccessory(0);
        AssertBool("축소: 초과 3발 전량 자동 판매 (+3, 20%)",
            syncRefund == 3 && lo3.AmmoStash.Count == 0 && lo3.Sequence.Size == 8);

        // 7) 프리뷰 — 시퀀스(특수탄) 반영 델타 정확
        var lo4 = new RunLoadout(new RunBuild(10f, 8, 4, 6));
        lo4.PlaceAmmo(0, sp20, out _); // 크리 전 턴당 90 → 총 360
        float cur = lo4.GetResult().StageTotal;
        AssertNear("현재 출력 (크리 전 혼합 360)", cur, P(360f));
        AssertNear("프리뷰(증가100%) 델타 크리 전 +360", lo4.PreviewEquipMod(new ModSpec(ModType.Increased, 1.0f)).StageTotal - cur, P(360f));
        AssertNear("프리뷰(속사장전+4) 델타 크리 전 +160", lo4.PreviewEquipAccessory(new AccessorySpec(AccessoryType.MagazineSize, 4)).StageTotal - cur, P(160f));
        AssertNear("프리뷰 후 현재 출력 불변 (크리 전 360)", lo4.GetResult().StageTotal, P(360f));
    }

    // 속성 파이프라인 — 즉발 합산·모드 분리·크리 물리 전용 (Phase 2 2단계 ①: DAMAGE_SYSTEM §2·4)
    private static void ElementalScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 속성 파이프라인 — 물리/속성 분리 (DAMAGE_SYSTEM 4장)");
        Console.WriteLine(" 소이탄: 물리 6 + 화염 8 (수치 초안 §4.5)");
        Console.WriteLine("--------------------------------------------------");

        var incendiary = new AmmoSpec(6f, ElementType.Fire, 8f);

        // 1) 즉발 합산 — 물리 6(크리 대상) + 화염 8(크리 비대상)
        var basic = BuildStats.Create(10f, 8, 4);
        AssertNear("소이탄 탄당 (물리 6 + 화염 8)", DamageCalculator.CalcPerShot(basic, incendiary), P(6f) + 8f);

        // 2) 물리 모드는 속성 미적용 — 증가 100%: 물리 12 + 화염 8
        var phys = BuildStats.Create(10f, 8, 4);
        phys.Apply(new ModSpec(ModType.Increased, 1.0f));
        AssertNear("물리 증가% 속성 미적용 (12 + 8)", DamageCalculator.CalcPerShot(phys, incendiary), P(12f) + 8f);

        // 3) 속성 증가% — 화염 8 → 12, 물리 무변: 6 + 12
        var elem = BuildStats.Create(10f, 8, 4);
        elem.Apply(new ModSpec(ModType.ElementalIncreased, 0.5f));
        AssertNear("속성 증가 50% (6 + 12)", DamageCalculator.CalcPerShot(elem, incendiary), P(6f) + 12f);
        AssertNear("화상 부여량 기준 = 속성 최종 (12)", DamageCalculator.CalcElemental(elem, incendiary), 12f);

        // 4) 크리 물리 전용 — 크리율 110%(기본 10 + 모드 100)·배율 2.0. 화염 8은 크리 미적용.
        //    ⚠ 크리율은 100%로 clamp되지 않아 110%면 기대배율이 2.1이 된다(초과분이 그대로 곱해짐).
        //      정밀 초과(P4) 설계가 들어오면 이 초과분을 크리배율로 전환하는 쪽으로 정리해야 한다.
        var crit = BuildStats.Create(10f, 8, 4);
        crit.Apply(new ModSpec(ModType.CritChance, 1.0f));
        AssertNear("크리 물리 전용 (물리만 크리 + 화염 8)", DamageCalculator.CalcPerShot(crit, incendiary), 6f * Crit(1.0f) + 8f);

        // 5) RunBuild 통합 — 시퀀스에 소이탄 1발: 턴당 = 물리(7×10 + 6) + 화염 8
        var build = new RunBuild(10f, 8, 4, 6);
        build.Sequence.Set(0, new AmmoEntry(incendiary, 6, true), out _);
        AssertNear("시퀀스 통합 턴당 (물리 76 + 화염 8)", build.GetResult().PerTurn(build.MagazineSize), P(76f) + 8f);

        // 6) 회귀 — 무속성(철갑탄) 경로 불변
        AssertNear("회귀: 철갑탄 균일 시퀀스 (크리 전 320)", new RunBuild(10f, 8, 4, 6).GetResult().StageTotal, P(320f));
    }

    // 화상 — 갱신·감쇠·소멸·조기 셋업 (Phase 2 2단계 ②: DAMAGE_SYSTEM §4.1)
    private static void BurnScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 화상 (BurnState) — max(부여 최강, 직전 × 0.5) · 1 미만 제거");
        Console.WriteLine($" 감쇠 {GameConfig.BurnDecay} · 소멸 임계 {GameConfig.BurnRemoveThreshold}");
        Console.WriteLine("--------------------------------------------------");

        // 1) 기획 검증 예 — 화염 8을 1턴에 부여: 틱 8+4+2+1 = 15, 5턴째 소멸(0.5 < 1)
        var burn = new BurnState();
        burn.Apply(8f);
        AssertBool("적중 즉시 예상 틱 표시 (8)", burn.ProjectedTick == 8f);
        float firstTick = burn.TickAtTurnEnd();
        AssertBool("틱 8 + 잔존 표시 4 (감쇠 피드백)", firstTick == 8f && burn.Intensity == 4f);
        burn.Apply(3f);
        AssertBool("추가 부여는 잔존에 얹힌다 (3 + 4 = 7)", Near(burn.ProjectedTick, 7f));
        float sum = firstTick + burn.TickAtTurnEnd() + burn.TickAtTurnEnd() + burn.TickAtTurnEnd();
        AssertNear("조기 셋업 4틱 합 (8 + 7 + 3.5 + 1.75 = 20.25)", sum, 20.25f);
        AssertBool("이어서 소멸 (0.875 < 1 → 제거·틱 0)", burn.TickAtTurnEnd() == 0f && !burn.IsActive);

        // 2) 중첩 — 신규 부여가 감쇠 잔존 위에 더해진다 (2026-08-01: max → 합산 전환)
        var b2 = new BurnState();
        b2.Apply(8f); b2.TickAtTurnEnd();          // 틱 8 → 잔존 4
        b2.Apply(3f);                               // 잔존 4 + 신규 3
        AssertNear("약한 부여도 누적 (3 + 4 = 7)", b2.TickAtTurnEnd(), 7f);
        b2.Apply(10f);                              // 잔존 3.5 + 신규 10
        AssertNear("강한 부여도 누적 (10 + 3.5 = 13.5)", b2.TickAtTurnEnd(), 13.5f);

        // 3) 같은 턴 다발 = 전부 합산 (발수 비례 — 화염 시퀀스 설계가 DoT에도 의미를 갖는다)
        var b3 = new BurnState();
        b3.Apply(8f); b3.Apply(5f); b3.Apply(8f);
        AssertNear("같은 턴 다발 = 합산 (8+5+8 = 21)", b3.TickAtTurnEnd(), 21f);

        // 3b) 무한 턴 수렴 — 매 턴 같은 양을 부여하면 강도가 A/(1-d)로 수렴(발산하지 않음)
        var b4 = new BurnState();
        float last = 0f;
        for (int i = 0; i < 40; i++) { b4.Apply(10f); last = b4.TickAtTurnEnd(); }
        AssertNear("정상상태 틱 = A/(1-감쇠) (10/0.5 = 20)", last, 10f / (1f - GameConfig.BurnDecay));

        // 4) 리셋 (스테이지 경계)
        b3.Reset();
        AssertBool("리셋 후 비활성·틱 0", !b3.IsActive && b3.TickAtTurnEnd() == 0f);

        // 5) 종합 — 소이탄(화염 8) 1발 1턴 투입(단발이라 중첩 전환과 무관): 즉발 8 + 틱 15 = 23
        var stats = BuildStats.Create(10f, 8, 4);
        var incendiary = new AmmoSpec(6f, ElementType.Fire, 8f);
        float instant = DamageCalculator.CalcElemental(stats, incendiary);
        var sim = new BurnState();
        sim.Apply(instant);
        float dot = sim.TickAtTurnEnd() + sim.TickAtTurnEnd() + sim.TickAtTurnEnd() + sim.TickAtTurnEnd();
        AssertNear("기획 검증 예 — 화염 기여 (즉발 8 + 틱 15 = 23)", instant + dot, 23f);

        // 6) 속성 증가% 동반 — 화염 12 부여: 즉발 12 + 틱 12+6+3+1.5 = 34.5
        var elemStats = BuildStats.Create(10f, 8, 4);
        elemStats.Apply(new ModSpec(ModType.ElementalIncreased, 0.5f));
        float instant2 = DamageCalculator.CalcElemental(elemStats, incendiary);
        var sim2 = new BurnState();
        sim2.Apply(instant2);
        float dot2 = sim2.TickAtTurnEnd() + sim2.TickAtTurnEnd() + sim2.TickAtTurnEnd() + sim2.TickAtTurnEnd();
        AssertNear("속성 증가 50% — 화염 기여 (12 + 22.5 = 34.5)", instant2 + dot2, 34.5f);

        // 7) 매 턴 재부여 — 중첩 전환의 핵심. 8발×4턴 화염 시퀀스의 화상 총량이 발수에 비례한다.
        //    구 방식(max)은 발수 무관 = 부여량 ×4로 고정이었다.
        var seq = new BurnState();
        float seqSum = 0f;
        for (int t = 0; t < 4; t++) { for (int s = 0; s < 8; s++) seq.Apply(7f); seqSum += seq.TickAtTurnEnd(); }
        AssertNear("8발×4턴 화상 총량 = 56 × (4+3d+2d²+d³)", seqSum, 56f * 6.125f);
    }

    // 발사 순번 트리거 — 조건부 모드 (Phase 2 4단계: DAMAGE_SYSTEM §5)
    private static void TriggerScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 발사 순번 트리거 — 첫 탄/막 탄/N번째 (조건부 모드)");
        Console.WriteLine(" 소총(탄창8·수4) 납탄10 기준");
        Console.WriteLine("--------------------------------------------------");

        var lead = new AmmoSpec(10f);
        var incendiary = new AmmoSpec(6f, ElementType.Fire, 8f);

        // 1) 첫 탄 2배 (전체 승산 MoreAll — SO와 동일 구성) — 턴당 = 20 + 7×10 = 90
        var first = new RunBuild(10f, 8, 4, 6);
        first.EquipMod(new ModSpec(ModType.MoreAll, 1.0f, TriggerType.FirstShot));
        AssertNear("첫 탄 2배 (크리 전 20 + 70 = 90)", first.GetResult().PerTurn(first.MagazineSize), P(90f));

        // 2) 막 탄 2배 + 탄창 확장 시 막 탄 위치 이동 (유효 크기 기준)
        var last = new RunBuild(10f, 8, 4, 6);
        last.EquipMod(new ModSpec(ModType.MoreAll, 1.0f, TriggerType.LastShot));
        AssertNear("막 탄 2배 (크리 전 70 + 20 = 90)", last.GetResult().PerTurn(last.MagazineSize), P(90f));
        var ls = last.ToBuildStats();
        AssertBool("막 탄 판정 (7번만 강화)",
            Near(DamageCalculator.CalcPerShot(last.GetShotStats(7, ls), lead), P(20f))
            && Near(DamageCalculator.CalcPerShot(last.GetShotStats(6, ls), lead), P(10f)));
        last.EquipAccessory(new AccessorySpec(AccessoryType.MagazineSize, 4)); // 유효 12칸
        var ls2 = last.ToBuildStats();
        AssertBool("확장 후 막 탄 = 11번으로 이동",
            Near(DamageCalculator.CalcPerShot(last.GetShotStats(11, ls2), lead), P(20f))
            && Near(DamageCalculator.CalcPerShot(last.GetShotStats(7, ls2), lead), P(10f)));

        // 3) N번째 탄 — 3번째(+10): 크리 전 턴당 90
        var nth = new RunBuild(10f, 8, 4, 6);
        nth.EquipMod(new ModSpec(ModType.Flat, 10f, TriggerType.NthShot, 3));
        AssertNear("3번째 탄 +10 (크리 전 턴당 90)", nth.GetResult().PerTurn(nth.MagazineSize), P(90f));

        // 4) 트리거 × 소이탄 — 물리 승산(More)은 물리만 (파이프라인 분리): 첫 탄 = 6×2 + 8 = 20
        var mix = new RunBuild(10f, 8, 4, 6);
        mix.EquipMod(new ModSpec(ModType.More, 1.0f, TriggerType.FirstShot));
        mix.Sequence.Set(0, new AmmoEntry(incendiary, 6, true), out _);
        AssertNear("첫 탄 소이 × 물리 승산 (물리 12+70 + 화염 8)", mix.GetResult().PerTurn(mix.MagazineSize), P(82f) + 8f);

        // 4b) 전체 승산(MoreAll) × 소이탄 — 물리·속성 모두 ×2: 첫 탄 = 12+16 = 28, 화상 부여도 16 (2026-06-12 의도 수정)
        var mixAll = new RunBuild(10f, 8, 4, 6);
        mixAll.EquipMod(new ModSpec(ModType.MoreAll, 1.0f, TriggerType.FirstShot));
        mixAll.Sequence.Set(0, new AmmoEntry(incendiary, 6, true), out _);
        AssertNear("첫 탄 전체 승산 × 소이 (물리 12+70 + 화염 16)", mixAll.GetResult().PerTurn(mixAll.MagazineSize), P(82f) + 16f);
        var allShot0 = mixAll.GetShotStats(0, mixAll.ToBuildStats());
        AssertNear("전체 승산 — 화상 부여도 2배 (16)", DamageCalculator.CalcElemental(allShot0, incendiary), 16f);

        // 5) 트리거 속성 강화 — 첫 탄 속성 +100%: 즉발 16 + 화상 부여 16
        var elem = new RunBuild(10f, 8, 4, 6);
        elem.EquipMod(new ModSpec(ModType.ElementalIncreased, 1.0f, TriggerType.FirstShot));
        elem.Sequence.Set(0, new AmmoEntry(incendiary, 6, true), out _);
        var shot0 = elem.GetShotStats(0, elem.ToBuildStats());
        AssertNear("첫 탄 속성 +100% — 화상 부여량 16", DamageCalculator.CalcElemental(shot0, incendiary), 16f);
        AssertNear("첫 탄 탄당 (물리 6 + 화염 16)", DamageCalculator.CalcPerShot(shot0, incendiary), P(6f) + 16f);

        // 6) 프리뷰 — 트리거 모드 장착 델타 정확
        var lo = new RunLoadout(new RunBuild(10f, 8, 4, 6)); // 크리 전 총 320
        float cur = lo.GetResult().StageTotal;
        AssertNear("프리뷰(첫 탄 2배) 델타 크리 전 +40", lo.PreviewEquipMod(new ModSpec(ModType.More, 1.0f, TriggerType.FirstShot)).StageTotal - cur, P(40f));

        // 7) 회귀 — 상시 모드(None) 경로 불변
        var plain = new RunBuild(10f, 8, 4, 6);
        plain.EquipMod(new ModSpec(ModType.Flat, 5f));
        AssertNear("회귀: 상시 모드 (크리 전 480)", plain.GetResult().StageTotal, P(480f));
    }

    // 스테이지 구성 — 일반 N + 보스 주기 골격 (Phase 3: GDD 3.1 / CONTENT_BACKLOG §2.8)
    private static void StageScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 스테이지 구성 — 일반 N + 보스 주기 (StageProgression)");
        Console.WriteLine($" 보스 주기 {GameConfig.BossStageCycle}스테 · 보스 요구치 가중 ×{GameConfig.BossRequirementMultiplier}");
        Console.WriteLine("--------------------------------------------------");

        // 런 맵 출력 (스테1~6: 일반/보스 + 요구치)
        for (int i = 1; i <= 6; i++)
        {
            var s = StageProgression.GetStage(i);
            Console.WriteLine($"   스테{i}: {(s.IsBoss ? "★보스" : " 일반")} · 요구 {s.RequiredDamage:0}");
        }

        // 1) 보스 주기 판정 (cycle 3 = 스테3·6 보스, 1·2·4·5 일반)
        AssertBool("스테1·2 일반", !StageProgression.IsBoss(1) && !StageProgression.IsBoss(2));
        AssertBool("스테3 보스 (주기 도달)", StageProgression.IsBoss(3));
        AssertBool("스테4·5 일반 (주기 재대기)", !StageProgression.IsBoss(4) && !StageProgression.IsBoss(5));
        AssertBool("스테6 보스 (다음 주기)", StageProgression.IsBoss(6));

        // 2) 보스 요구치 = 일반 곡선값 × 보스 가중 (곡선 = 선형, 2026-07-29 확정)
        float normal3 = GameConfig.StageBaseRequirement + GameConfig.StageLinearStep * 2;
        AssertNear("보스(스테3) 요구치 = 곡선 × 가중", StageProgression.GetRequired(3), normal3 * GameConfig.BossRequirementMultiplier);
        AssertBool("스테종류 노출 (GetStage)", StageProgression.GetStage(3).Type == StageType.Boss && StageProgression.GetStage(2).Type == StageType.Normal);

        // 3) 데드엔드 금지 (2026-07-29 사용자 제약) — 상점에서 아무것도 못 건져도 스테2까지는 넘어야 한다.
        //    모드는 전부 가산이라 빈 빌드가 곧 '어떤 모드 조합이든'의 최악값이다. 곡선을 만질 때 여기서 걸린다.
        var bare = new RunBuild(10f, 8, 4, 6);        // 소총 골격, 모드 0개 = 352 출력
        AssertBool("빈 빌드(모드0) 스테1 클리어 — 어떤 빌드로도 시작은 가능",
            CombatResolver.ResolveExpected(bare.ToBuildStats(), StageProgression.GetRequired(1)).Cleared);
        AssertBool("빈 빌드(모드0) 스테2 클리어 — 상점 1회를 공쳐도 막히지 않는다",
            CombatResolver.ResolveExpected(bare.ToBuildStats(), StageProgression.GetRequired(2)).Cleared);
        AssertBool("빈 빌드(모드0)는 스테3에서 멈춘다 — 무한정 공짜는 아니다",
            !CombatResolver.ResolveExpected(bare.ToBuildStats(), StageProgression.GetRequired(3)).Cleared);

        // 4) 1런 진행 — 게이트는 '보스 벽'이 아니라 곡선이다 (2026-07-29 보스 가중 폐지).
        //    멈추는 지점은 곡선 튜닝에 따라 움직이므로 스테이지 번호를 박지 않고 '완주 전에 멈춘다'만 본다.
        var weak = new RunBuild(10f, 8, 4, 6);
        weak.EquipMod(new ModSpec(ModType.Flat, 5f));
        int weakReach = MaxClearedStage(weak.ToBuildStats(), GameConfig.RunCompletionStage);
        AssertBool($"약빌드(모드1)는 곡선 게이트에서 멈춘다 (도달 스테{weakReach})",
            weakReach >= 1 && weakReach < GameConfig.RunCompletionStage);

        var strong = new RunBuild(10f, 8, 4, 6);
        strong.EquipMod(new ModSpec(ModType.Flat, 5f));
        strong.EquipMod(new ModSpec(ModType.Increased, 1.0f));
        for (int i = 0; i < 4; i++) strong.EquipMod(new ModSpec(ModType.More, 0.5f));
        AssertBool("강빌드 스테3 보스 클리어 (런 완주)", CombatResolver.ResolveExpected(strong.ToBuildStats(), StageProgression.GetRequired(3)).Cleared);
    }

    // 총기 다양성 — 리볼버 크기 잠금 / 기관총 물량·슬롯 (Phase 3: GDD 5.1 / 4.7)
    private static void GunDiversityScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 총기 다양성 — 리볼버(크기 잠금) / 소총 / 기관총");
        Console.WriteLine(" 리볼버 슬롯4·탄창6(고정) / 소총 슬롯6·탄창8 / 기관총 슬롯9·탄창12");
        Console.WriteLine("--------------------------------------------------");

        var magUp = new AccessorySpec(AccessoryType.MagazineSize, 4); // 속사장전 +4
        var countUp = new AccessorySpec(AccessoryType.MagazineCount, 2); // 확장탄창 +2

        // 1) 소총(비잠금) — 크기 증가 악세 정상 적용 (회귀): 8 → 12
        var rifle = new RunBuild(10f, 8, 4, 6);
        rifle.EquipAccessory(magUp);
        AssertBool("소총: 크기 증가 악세 적용 (8 → 12, 48발)", rifle.MagazineSize == 12 && rifle.GetResult().TotalShots == 48);

        // 2) 리볼버(잠금) — 크기 증가 악세 무효(실린더 고정), 단 탄창 수 악세는 유효
        var revolver = new RunBuild(10f, 6, 4, 4, magazineSizeLocked: true);
        revolver.EquipAccessory(magUp);
        AssertBool("리볼버: 크기 증가 무효 (6 유지)", revolver.MagazineSize == 6);
        revolver.EquipAccessory(countUp);
        AssertBool("리볼버: 탄창 수 악세는 유효 (수 6 · 36발)", revolver.MagazineCount == 6 && revolver.GetResult().TotalShots == 36);

        // 3) 리볼버 프리뷰도 잠금 전파 (크기 증가 악세 = 델타 0)
        var revLo = new RunLoadout(new RunBuild(10f, 6, 4, 4, magazineSizeLocked: true));
        float cur = revLo.GetResult().TotalShots;
        AssertBool("리볼버 프리뷰: 크기 증가 악세 델타 0", revLo.PreviewEquipAccessory(magUp).TotalShots == cur);

        // 4) 기관총 — 물량(12 × 4 = 48발) + 탄창 슬롯 6 한도(12÷2)
        // MOD_SPLIT Phase1 — Flat(상시)은 탄창 모드라 총기 슬롯9가 아닌 탄창 슬롯(12÷2=6)이 적용된다.
        var mg = new RunBuild(10f, 12, 4, 9);
        AssertBool("기관총: 탄창 12 × 수 4 = 48발", mg.GetResult().TotalShots == 48);
        for (int i = 0; i < 9; i++) mg.EquipMod(new ModSpec(ModType.Flat, 1f));
        AssertBool("기관총: 탄창 슬롯 6 장착 후 7번째 거부", mg.EquippedCount == 6 && !mg.EquipMod(new ModSpec(ModType.Flat, 1f)));
    }

    // 시퀀스 상태 조건 — 빈 칸/탄창 빔/특정 탄 N발/직전 탄 (Phase 3 C2 + ✅ 대안: GDD 4.7 / 백로그 §2.5)
    private static void ConditionScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 시퀀스 상태 조건 (ConditionType) — 빈칸/탄창빔/특정탄N/직전탄");
        Console.WriteLine(" 소총(탄창8·수4) 기본탄10 기준");
        Console.WriteLine("--------------------------------------------------");

        var fire = new AmmoSpec(10f, ElementType.Fire, 0f); // 물리10 + 화염 태그(즉발 0) — 카운트/직전 판정용

        // 1) 빈 칸 보너스(PerEmptyCell) — 증가 +12%/빈칸. 빈 칸 0이면 무효, 2칸이면 +24%(6발 발사)
        var b1 = new RunBuild(10f, 8, 4, 6);
        b1.EquipMod(new ModSpec(ModType.Increased, 0.12f, ConditionType.PerEmptyCell));
        AssertNear("빈칸당 — 빈 칸 0 → 무효 (크리 전 320)", b1.GetResult().StageTotal, P(320f));
        b1.Sequence.Set(0, AmmoEntry.CreateEmpty(), out _);
        b1.Sequence.Set(1, AmmoEntry.CreateEmpty(), out _);
        AssertNear("빈칸당 — 빈 칸 2 → +24%, 6발 (크리 전 6×12.4×4 = 297.6)", b1.GetResult().StageTotal, P(297.6f));

        // 2) 탄창 빔(MagazineHasEmpty) — 빈 칸 있으면 승산 ×1.5
        var b2 = new RunBuild(10f, 8, 4, 6);
        b2.EquipMod(new ModSpec(ModType.More, 0.5f, ConditionType.MagazineHasEmpty));
        AssertNear("탄창빔 — 빈 칸 0 → 무효 (크리 전 320)", b2.GetResult().StageTotal, P(320f));
        b2.Sequence.Set(0, AmmoEntry.CreateEmpty(), out _);
        AssertNear("탄창빔 — 빈 칸 ≥1 → ×1.5, 7발 (크리 전 7×15×4 = 420)", b2.GetResult().StageTotal, P(420f));

        // 3) 특정 탄 N발↑(CountElementAtLeast) — 화염 3발 이상이면 전탄 승산 ×1.5
        var b3 = new RunBuild(10f, 8, 4, 6);
        b3.EquipMod(new ModSpec(ModType.More, 0.5f, ConditionType.CountElementAtLeast, ElementType.Fire, 3));
        b3.Sequence.Set(0, new AmmoEntry(fire, 5, true), out _);
        b3.Sequence.Set(1, new AmmoEntry(fire, 5, true), out _);
        AssertNear("특정탄 — 화염 2발 < 3 → 무효 (크리 전 320)", b3.GetResult().StageTotal, P(320f));
        b3.Sequence.Set(2, new AmmoEntry(fire, 5, true), out _);
        AssertNear("특정탄 — 화염 3발 ≥ 3 → ×1.5 전탄 (크리 전 8×15×4 = 480)", b3.GetResult().StageTotal, P(480f));

        // 4) 직전 탄 종류(PrevShotElement, 탄별) — 직전이 화염이면 이 탄 ×2
        var b4 = new RunBuild(10f, 8, 4, 6);
        b4.EquipMod(new ModSpec(ModType.More, 1.0f, ConditionType.PrevShotElement, ElementType.Fire));
        b4.Sequence.Set(2, new AmmoEntry(fire, 5, true), out _); // 슬롯2 화염 → 슬롯3 강화
        AssertNear("직전탄 — 슬롯3(직전=화염) ×2 (크리 전 (7×10 + 20)×4 = 360)", b4.GetResult().StageTotal, P(360f));
        var bs = b4.ToBuildStats();
        AssertBool("직전탄 — 3번 탄만 강화 (크리 전 20 / 2번은 10)",
            Near(DamageCalculator.CalcPerShot(b4.GetShotStats(3, bs), b4.GetShotEntry(3).Spec), P(20f))
            && Near(DamageCalculator.CalcPerShot(b4.GetShotStats(2, bs), b4.GetShotEntry(2).Spec), P(10f)));

        // 5) 회귀 — 조건 없는 모드 경로 불변
        var plain = new RunBuild(10f, 8, 4, 6);
        plain.EquipMod(new ModSpec(ModType.Flat, 5f));
        AssertNear("회귀: 무조건 모드 (크리 전 480)", plain.GetResult().StageTotal, P(480f));
    }

    // MOD_SPLIT Phase 3 웨이브1 — 신규 트리거 4종 + 전역 조건 6종 + 탄별 조건 3종 (IMPL_PLAN §3)
    private static void Phase3ConditionScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" Phase 3 웨이브1 — 신규 트리거/조건 (MOD_SPLIT §3)");
        Console.WriteLine(" 소총(탄창8·수4) 기본탄10 기준 · 속성탄 = 물리10 + 속성태그(즉발 0)");
        Console.WriteLine("--------------------------------------------------");
        // 이 절의 탄약은 전부 즉발 0(순수 물리)이라 기대값은 P()로 기본 크리를 반영한다.
        // 라벨의 숫자는 '크리 전' 기준 — 조건 로직만 보게 두려는 의도.

        var fire = new AmmoSpec(10f, ElementType.Fire, 0f);
        var poison = new AmmoSpec(10f, ElementType.Poison, 0f);
        var shock = new AmmoSpec(10f, ElementType.Shock, 0f);

        // ── 3-A 트리거 ──
        // 1) 홀/짝 — 0-기반 짝수 인덱스 = 1·3·5…번째 탄
        var odd = new RunBuild(10f, 8, 4, 6);
        odd.EquipMod(new ModSpec(ModType.More, 1.0f, TriggerType.OddShot));
        AssertNear("홀수박자 — 홀수 번째 4발만 ×2 (4×20 + 4×10 = 120/턴 → 480)", odd.GetResult().StageTotal, P(480f));
        BuildStats bsOdd = odd.ToBuildStats();
        AssertBool("홀수박자 — 0번(1번째)=20 · 1번(2번째)=10",
            Near(DamageCalculator.CalcPerShot(odd.GetShotStats(0, bsOdd), odd.GetShotEntry(0).Spec), P(20f))
            && Near(DamageCalculator.CalcPerShot(odd.GetShotStats(1, bsOdd), odd.GetShotEntry(1).Spec), P(10f)));

        var even = new RunBuild(10f, 8, 4, 6);
        even.EquipMod(new ModSpec(ModType.More, 1.0f, TriggerType.EvenShot));
        BuildStats bsEven = even.ToBuildStats();
        AssertBool("짝수박자 — 대칭(1번=20 · 0번=10)",
            Near(DamageCalculator.CalcPerShot(even.GetShotStats(1, bsEven), even.GetShotEntry(1).Spec), P(20f))
            && Near(DamageCalculator.CalcPerShot(even.GetShotStats(0, bsEven), even.GetShotEntry(0).Spec), P(10f)));

        // 2) 막 N탄(LastNShots) — 램프업
        var lv = new RunBuild(10f, 8, 4, 6);
        lv.EquipMod(new ModSpec(ModType.More, 0.5f, TriggerType.LastNShots, 3));
        AssertNear("최후3연사 — 막 3탄만 ×1.5 (5×10 + 3×15 = 95/턴 → 380)", lv.GetResult().StageTotal, P(380f));

        // 3) 마지막 유효탄(LastLoadedShot) — 빈 칸 뒤섞임에 따라 대상이 앞으로 이동
        var ll = new RunBuild(10f, 8, 4, 6);
        ll.EquipMod(new ModSpec(ModType.More, 1.0f, TriggerType.LastLoadedShot));
        AssertNear("마지막유효탄 — 빈 칸 없음 → 7번 탄 ×2 (7×10 + 20 = 90/턴 → 360)", ll.GetResult().StageTotal, P(360f));
        ll.Sequence.Set(6, AmmoEntry.CreateEmpty(), out _);
        ll.Sequence.Set(7, AmmoEntry.CreateEmpty(), out _);
        AssertNear("마지막유효탄 — 막 2칸 빔 → 5번 탄으로 이동 (5×10 + 20 = 70/턴 → 280)", ll.GetResult().StageTotal, P(280f));
        BuildStats bsLl = ll.ToBuildStats();
        AssertBool("마지막유효탄 — 5번=20 · 4번=10",
            Near(DamageCalculator.CalcPerShot(ll.GetShotStats(5, bsLl), ll.GetShotEntry(5).Spec), P(20f))
            && Near(DamageCalculator.CalcPerShot(ll.GetShotStats(4, bsLl), ll.GetShotEntry(4).Spec), P(10f)));

        // ── 3-B 전역 조건 ──
        // 4) 덱 통일(UniformElement) — 빈 칸 무시. ⚠ 기본탄만 든 상태(전부 무속성)도 '통일'로 성립(스펙대로)
        var ud = new RunBuild(10f, 8, 4, 6);
        ud.EquipMod(new ModSpec(ModType.More, 0.6f, ConditionType.UniformElement));
        AssertNear("덱통일 — 기본탄만(전부 무속성) → 충족 ×1.6 (512)", ud.GetResult().StageTotal, P(512f));
        ud.Sequence.Set(0, new AmmoEntry(fire, 5, true), out _);
        AssertNear("덱통일 — 화염 1발 섞임 → 불충족 (320)", ud.GetResult().StageTotal, P(320f));
        for (int i = 1; i < 8; i++) ud.Sequence.Set(i, new AmmoEntry(fire, 5, true), out _);
        AssertNear("덱통일 — 전탄 화염 → 충족 ×1.6 (512)", ud.GetResult().StageTotal, P(512f));
        ud.Sequence.Set(3, AmmoEntry.CreateEmpty(), out _);
        AssertNear("덱통일 — 빈 칸은 무시 (7×16×4 = 448)", ud.GetResult().StageTotal, P(448f));

        // 5) 만재(MagazineFull)
        var fh = new RunBuild(10f, 8, 4, 6);
        fh.EquipMod(new ModSpec(ModType.More, 0.4f, ConditionType.MagazineFull));
        AssertNear("만재 — 빈 칸 0 → ×1.4 (8×14×4 = 448)", fh.GetResult().StageTotal, P(448f));
        fh.Sequence.Set(0, AmmoEntry.CreateEmpty(), out _);
        AssertNear("만재 — 빈 칸 1 → 무효 (7×10×4 = 280)", fh.GetResult().StageTotal, P(280f));

        // 6) 탄종 스택(PerElementCell) — 해당 속성 탄 수만큼 누적 적용
        var es = new RunBuild(10f, 8, 4, 6);
        es.EquipMod(new ModSpec(ModType.Increased, 0.06f, ConditionType.PerElementCell, ElementType.Fire));
        AssertNear("탄종스택 — 화염 0발 → 무효 (320)", es.GetResult().StageTotal, P(320f));
        es.Sequence.Set(0, new AmmoEntry(fire, 5, true), out _);
        es.Sequence.Set(1, new AmmoEntry(fire, 5, true), out _);
        es.Sequence.Set(2, new AmmoEntry(fire, 5, true), out _);
        AssertNear("탄종스택 — 화염 3발 → +18% 전탄 (8×11.8×4 = 377.6)", es.GetResult().StageTotal, P(377.6f));

        // 7) 무지개(DistinctElementsAtLeast) — 무속성(기본탄)은 종 수에서 제외
        var rb = new RunBuild(10f, 8, 4, 6);
        rb.EquipMod(new ModSpec(ModType.More, 0.5f, ConditionType.DistinctElementsAtLeast, ElementType.None, 3));
        rb.Sequence.Set(0, new AmmoEntry(fire, 5, true), out _);
        rb.Sequence.Set(1, new AmmoEntry(poison, 5, true), out _);
        AssertNear("무지개 — 속성 2종 < 3 → 무효 (320)", rb.GetResult().StageTotal, P(320f));
        rb.Sequence.Set(2, new AmmoEntry(shock, 5, true), out _);
        AssertNear("무지개 — 속성 3종 ≥ 3 → ×1.5 (8×15×4 = 480)", rb.GetResult().StageTotal, P(480f));

        // 8) 수미상관(FirstLastSameElement) — 빈 칸 = 불충족
        var be = new RunBuild(10f, 8, 4, 6);
        be.EquipMod(new ModSpec(ModType.More, 0.3f, ConditionType.FirstLastSameElement));
        AssertNear("수미상관 — 첫·막 둘 다 기본탄 → 충족 ×1.3 (416)", be.GetResult().StageTotal, P(416f));
        be.Sequence.Set(0, new AmmoEntry(fire, 5, true), out _);
        AssertNear("수미상관 — 첫=화염·막=기본 → 불충족 (320)", be.GetResult().StageTotal, P(320f));
        be.Sequence.Set(7, new AmmoEntry(fire, 5, true), out _);
        AssertNear("수미상관 — 첫·막 둘 다 화염 → ×1.3 (416)", be.GetResult().StageTotal, P(416f));
        be.Sequence.Set(7, AmmoEntry.CreateEmpty(), out _);
        AssertNear("수미상관 — 막 칸 빔 → 불충족 (7×10×4 = 280)", be.GetResult().StageTotal, P(280f));

        // 9) 정예 분대(AllLoadedDistinct) — 2발 이상 + 전부 다른 속성
        var eq = new RunBuild(10f, 8, 4, 6);
        eq.EquipMod(new ModSpec(ModType.More, 0.6f, ConditionType.AllLoadedDistinct));
        AssertNear("정예분대 — 기본탄 8발(무속성 중복) → 불충족 (320)", eq.GetResult().StageTotal, P(320f));
        for (int i = 2; i < 8; i++) eq.Sequence.Set(i, AmmoEntry.CreateEmpty(), out _);
        eq.Sequence.Set(0, new AmmoEntry(fire, 5, true), out _);
        eq.Sequence.Set(1, new AmmoEntry(poison, 5, true), out _);
        AssertNear("정예분대 — 화염+독 2발만 장전 → ×1.6 (2×16×4 = 128)", eq.GetResult().StageTotal, P(128f));
        eq.Sequence.Set(1, AmmoEntry.CreateEmpty(), out _);
        AssertNear("정예분대 — 1발만 장전 → 불충족 (1×10×4 = 40)", eq.GetResult().StageTotal, P(40f));
        eq.Sequence.Set(1, new AmmoEntry(fire, 5, true), out _);
        AssertNear("정예분대 — 화염 중복 → 불충족 (2×10×4 = 80)", eq.GetResult().StageTotal, P(80f));

        // ── 3-C 탄별 조건 ──
        // 10) 속성 연타(PrevSameElement) — 무속성(기본탄) 연속은 제외
        var ec = new RunBuild(10f, 8, 4, 6);
        ec.EquipMod(new ModSpec(ModType.More, 0.5f, ConditionType.PrevSameElement));
        ec.Sequence.Set(0, new AmmoEntry(fire, 5, true), out _);
        ec.Sequence.Set(1, new AmmoEntry(fire, 5, true), out _);
        ec.Sequence.Set(2, new AmmoEntry(fire, 5, true), out _);
        AssertNear("속성연타 — 화염 3연속 → 2·3번째만 ×1.5 (6×10 + 2×15 = 90/턴 → 360)", ec.GetResult().StageTotal, P(360f));
        BuildStats bsEc = ec.ToBuildStats();
        AssertBool("속성연타 — 무속성 연속(기본탄)은 제외 (4번 탄 = 10)",
            Near(DamageCalculator.CalcPerShot(ec.GetShotStats(4, bsEc), ec.GetShotEntry(4).Spec), P(10f)));

        // 11) 빈칸 반동(PrevShotEmpty)
        var er = new RunBuild(10f, 8, 4, 6);
        er.EquipMod(new ModSpec(ModType.More, 1.0f, ConditionType.PrevShotEmpty));
        er.Sequence.Set(2, AmmoEntry.CreateEmpty(), out _);
        AssertNear("빈칸반동 — 빈 칸 직후 1발만 ×2 (6×10 + 20 = 80/턴 → 320)", er.GetResult().StageTotal, P(320f));

        // 12) 교대 사격(PrevDifferentElement)
        var alt = new RunBuild(10f, 8, 4, 6);
        alt.EquipMod(new ModSpec(ModType.Increased, 0.3f, ConditionType.PrevDifferentElement));
        alt.Sequence.Set(0, new AmmoEntry(fire, 5, true), out _);
        AssertNear("교대사격 — 속성 전환 1회만 +30% (7×10 + 13 = 83/턴 → 332)", alt.GetResult().StageTotal, P(332f));

        // ── MOD_SPLIT 분류 정합 (IsGunMod) — 3-C 누락 시 전역 집계 오편입 + 슬롯 분류가 틀어진다 ──
        AssertBool("분류: 신규 트리거 4종 = 총기 모드",
            new ModSpec(ModType.More, 1f, TriggerType.OddShot).IsGunMod
            && new ModSpec(ModType.More, 1f, TriggerType.EvenShot).IsGunMod
            && new ModSpec(ModType.More, 1f, TriggerType.LastNShots, 3).IsGunMod
            && new ModSpec(ModType.More, 1f, TriggerType.LastLoadedShot).IsGunMod);
        AssertBool("분류: 신규 탄별 조건 3종 = 총기 모드 (IsPerShotCondition 등록 확인)",
            new ModSpec(ModType.More, 1f, ConditionType.PrevSameElement).IsGunMod
            && new ModSpec(ModType.More, 1f, ConditionType.PrevShotEmpty).IsGunMod
            && new ModSpec(ModType.More, 1f, ConditionType.PrevDifferentElement).IsGunMod);
        AssertBool("분류: 신규 전역 조건 6종 = 탄창 모드",
            !new ModSpec(ModType.More, 1f, ConditionType.UniformElement).IsGunMod
            && !new ModSpec(ModType.More, 1f, ConditionType.PerElementCell, ElementType.Fire).IsGunMod
            && !new ModSpec(ModType.More, 1f, ConditionType.MagazineFull).IsGunMod
            && !new ModSpec(ModType.More, 1f, ConditionType.DistinctElementsAtLeast, ElementType.None, 3).IsGunMod
            && !new ModSpec(ModType.More, 1f, ConditionType.FirstLastSameElement).IsGunMod
            && !new ModSpec(ModType.More, 1f, ConditionType.AllLoadedDistinct).IsGunMod);

        // ⭐ 계열 규칙 (2026-08-27 · MOD_REVIEW §1) — "총기 = 규칙과 상황 · 탄창 = 수치와 구성"
        // ④ 판 상황 · ⑤-액션형 · 인에이블러가 파생에 들어왔다. 빠지면 탄창으로 잘못 분류된다.
        AssertBool("분류 ④: 판 상태 조건 = 총기 (계열 = 판정 단계 등식 해제)",
            new ModSpec(ModType.More, 1f, ConditionType.BoardFirstTurn).IsGunMod
            && new ModSpec(ModType.More, 1f, ConditionType.BoardLastTurn).IsGunMod
            && new ModSpec(ModType.More, 1f, ConditionType.BoardPerTurnLeft).IsGunMod
            && new ModSpec(ModType.More, 1f, ConditionType.BoardPerFieldCap).IsGunMod
            && new ModSpec(ModType.More, 1f, ConditionType.BoardPerLastTurnKill).IsGunMod
            && new ModSpec(ModType.More, 1f, ConditionType.BoardAllShocked).IsGunMod);
        AssertBool("분류 ⑤: 액션형 = 총기 (전투 이벤트에 직접 반응)",
            new ModSpec(ModActionKind.KillDamageAdjacent, 0.2f).IsGunMod
            && new ModSpec(ModActionKind.TurnEndBurnBurst, 1.5f).IsGunMod);
        AssertBool("분류 ⑤: 인에이블러 = 총기 (시퀀스 해석 규칙 변경)",
            new ModSpec(ModType.Flat, 0f, ConditionType.EnableSkipEmptyAsPrev).IsGunMod);

        // ⚠ 파생으로는 못 잡는 자리 — 시점도 조건도 없이 ModType만 다른 규칙 변경형.
        //   Family 필드가 없으면 '구경 확장' 같은 순수 스탯과 구분되지 않는다.
        AssertBool("분류: 규칙 변경형은 파생이 못 잡는다 (Auto = 탄창으로 떨어진다)",
            !new ModSpec(ModType.BurnDecayUp, 0.6f).IsGunMod
            && !new ModSpec(ModType.ShockPersist, 1f).IsGunMod
            && !new ModSpec(ModType.CritResonance, 0.5f).IsGunMod
            && !new ModSpec(ModType.MultiShot, 1f).IsGunMod
            && !new ModSpec(ModType.CritOverflow, 2f).IsGunMod);
        AssertBool("⭐ Family = Gun 이면 파생을 덮어쓴다",
            new ModSpec(ModType.CritOverflow, 2f, TriggerType.None, 0, ConditionType.None,
                        ElementType.None, 0, ElementType.None, ModActionKind.None,
                        Rarity.Rare, ModFamily.Gun).IsGunMod);
        AssertBool("⭐ Family = Mag 이면 트리거가 있어도 탄창",
            !new ModSpec(ModType.More, 1f, TriggerType.FirstShot, 0, ConditionType.None,
                         ElementType.None, 0, ElementType.None, ModActionKind.None,
                         Rarity.Common, ModFamily.Mag).IsGunMod);
        AssertBool("Family = Auto 가 기본 — 기존 생성자는 종전 파생 그대로(회귀 0)",
            new ModSpec(ModType.More, 1f).Family == ModFamily.Auto
            && !new ModSpec(ModType.More, 1f).IsGunMod);
    }

    // 속성 축 확장 — 속성별(가산·증가·승산) vs 속성 전체. 대상 속성 = ModSpec.TargetElement
    private static void ElementalAxisScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 속성 축 확장 — 속성별 가산/증가/승산 (TargetElement)");
        Console.WriteLine(" 소이탄(물리 6 + 화염 8) 1발 기준 · 속성은 크리 미적용");
        Console.WriteLine("--------------------------------------------------");

        var fire = new AmmoSpec(6f, ElementType.Fire, 8f);   // 화염 8
        var toxic = new AmmoSpec(4f, ElementType.Poison, 3f); // 독 3
        var plain = new AmmoSpec(10f);                        // 무속성

        // 1) 속성별 가산 — 대상 속성 탄에만
        var f1 = BuildStats.Create(10f, 8, 4);
        f1.Apply(new ModSpec(ModType.ElementalFlat, 5f, ElementType.Fire));
        AssertNear("속성가산 — 화염탄 (8+5 = 13)", DamageCalculator.CalcElemental(f1, fire), 13f);
        AssertNear("속성가산 — 독탄은 무영향 (3)", DamageCalculator.CalcElemental(f1, toxic), 3f);
        AssertNear("속성가산 — 무속성탄은 0", DamageCalculator.CalcElemental(f1, plain), 0f);

        // 2) 속성별 증가 — 대상 속성 탄에만
        var f2 = BuildStats.Create(10f, 8, 4);
        f2.Apply(new ModSpec(ModType.ElementalIncreased, 0.40f, ElementType.Fire));
        AssertNear("속성증가 — 화염탄 (8 × 1.4 = 11.2)", DamageCalculator.CalcElemental(f2, fire), 11.2f);
        AssertNear("속성증가 — 독탄은 무영향 (3)", DamageCalculator.CalcElemental(f2, toxic), 3f);

        // 3) 속성별 승산
        var f3 = BuildStats.Create(10f, 8, 4);
        f3.Apply(new ModSpec(ModType.ElementalMore, 0.50f, ElementType.Fire));
        AssertNear("속성승산 — 화염탄 (8 × 1.5 = 12)", DamageCalculator.CalcElemental(f3, fire), 12f);
        AssertNear("속성승산 — 독탄은 무영향 (3)", DamageCalculator.CalcElemental(f3, toxic), 3f);

        // 4) 속성 전체(TargetElement=None) — 전 속성 적용
        var all = BuildStats.Create(10f, 8, 4);
        all.Apply(new ModSpec(ModType.ElementalIncreased, 0.25f)); // catalyst
        all.Apply(new ModSpec(ModType.ElementalMore, 0.30f));      // elemental_surge
        AssertNear("속성전체 — 화염탄 (8 × 1.25 × 1.3 = 13)", DamageCalculator.CalcElemental(all, fire), 13f);
        AssertNear("속성전체 — 독탄도 동일 배율 (3 × 1.25 × 1.3 = 4.875)", DamageCalculator.CalcElemental(all, toxic), 4.875f);

        // 5) 전체 + 속성별 합성 — 증가는 합산(Σ), 승산은 각각 곱(Π)
        var mix = BuildStats.Create(10f, 8, 4);
        mix.Apply(new ModSpec(ModType.ElementalFlat, 5f, ElementType.Fire));
        mix.Apply(new ModSpec(ModType.ElementalIncreased, 0.25f));                    // 전체 +25%
        mix.Apply(new ModSpec(ModType.ElementalIncreased, 0.40f, ElementType.Fire));   // 화염 +40%
        mix.Apply(new ModSpec(ModType.ElementalMore, 0.30f));                          // 전체 ×1.3
        mix.Apply(new ModSpec(ModType.ElementalMore, 0.50f, ElementType.Fire));        // 화염 ×1.5
        // (8+5) × (1+0.25+0.40) × 1.3 × 1.5 = 13 × 1.65 × 1.95 = 41.8275
        AssertNear("합성 — 화염탄 (13 × 1.65 × 1.95 = 41.8275)", DamageCalculator.CalcElemental(mix, fire), 41.8275f);
        // 독탄은 전체분만: 3 × 1.25 × 1.3 = 4.875
        AssertNear("합성 — 독탄은 전체분만 (4.875)", DamageCalculator.CalcElemental(mix, toxic), 4.875f);

        // 6) 기본댐 0 + 속성 가산만으로 발동 (HasElement 게이트 완화 확인)
        var tagOnly = new AmmoSpec(10f, ElementType.Fire, 0f); // 화염 태그만(즉발 0)
        var f6 = BuildStats.Create(10f, 8, 4);
        AssertNear("게이트 — 가산 없으면 종전대로 0", DamageCalculator.CalcElemental(f6, tagOnly), 0f);
        f6.Apply(new ModSpec(ModType.ElementalFlat, 5f, ElementType.Fire));
        AssertNear("게이트 — 가산만으로 발동 (0+5 = 5)", DamageCalculator.CalcElemental(f6, tagOnly), 5f);

        // 7) 전체 승산은 물리와 무관 (AllMoreMultiplier와 별개 축)
        var f7 = BuildStats.Create(10f, 8, 4);
        f7.Apply(new ModSpec(ModType.ElementalMore, 0.50f, ElementType.Fire));
        AssertNear("독립성 — 속성 승산은 물리 불변 (10)", DamageCalculator.CalcPerShotBeforeCrit(f7, fire.PhysicalBase), 6f);

        // 8) 회귀 — 속성 모드 없으면 종전 결과 (소이탄 물리6 + 화염8). 물리분만 기본 크리 적용.
        var reg = BuildStats.Create(10f, 8, 4);
        AssertNear("회귀: 무보정 소이탄 탄당 (물리 6 + 화염 8)", DamageCalculator.CalcPerShot(reg, fire), P(6f) + 8f);

        // 9) 분류 — 속성 축 모드는 조건·트리거가 없으므로 전부 탄창 모드
        AssertBool("분류: 속성 축 모드 = 탄창 모드",
            !new ModSpec(ModType.ElementalFlat, 5f, ElementType.Fire).IsGunMod
            && !new ModSpec(ModType.ElementalMore, 0.5f, ElementType.Fire).IsGunMod
            && !new ModSpec(ModType.ElementalIncreased, 0.25f).IsGunMod);
    }

    // 상태이상 부여 파이프라인 — 탄별 배율(자기 속성 한정) + 독 누적 + 감전 (max + 발당 누적)
    private static void StatusPipelineScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 상태이상 부여 — 탄별 배율 · 독 누적 · 감전(max + 발당 누적)");
        Console.WriteLine($" 감전 발당 누적 {GameConfig.ShockPerHitMagnitude:P0} · 천장 {GameConfig.ShockMaxMagnitude:P0} · 임계비율 {GameConfig.ShockThresholdRatio:P0}");
        Console.WriteLine("--------------------------------------------------");

        var fire = new AmmoSpec(6f, ElementType.Fire, 8f);                             // 배율 1(기본)
        var fireHot = new AmmoSpec(6f, ElementType.Fire, 8f, HitPattern.Single, 1, 1.5f); // 배율 1.5
        var stats = BuildStats.Create(10f, 8, 4);

        // 1) 탄별 배율 — 부여량만 1.5배, 즉발 데미지는 불변 (핵심 계약)
        AssertNear("배율 — 화상 부여량 기본 (8)", DamageCalculator.CalcBurnAmount(stats, fire), 8f);
        AssertNear("배율 — 화상 부여량 ×1.5 (12)", DamageCalculator.CalcBurnAmount(stats, fireHot), 12f);
        AssertNear("배율 — 즉발 속성 데미지는 불변 (8)", DamageCalculator.CalcElemental(stats, fireHot), 8f);
        AssertNear("배율 — 즉발 탄당도 불변 (물리 6 + 화염 8)", DamageCalculator.CalcPerShot(stats, fireHot), P(6f) + 8f);

        // 2) 상태이상 강도% 와 탄별 배율은 곱으로 합성
        var potent = BuildStats.Create(10f, 8, 4);
        potent.Apply(new ModSpec(ModType.StatusIntensity, 0.5f));
        AssertNear("배율 — 강도50% × 배율1.5 (8 × 1.5 × 1.5 = 18)", DamageCalculator.CalcBurnAmount(potent, fireHot), 18f);

        // 3) 독 — 펠릿마다 스택(×펠릿)
        var toxic = new AmmoSpec(4f, ElementType.Poison, 3f);
        AssertNear("독 — 펠릿1 부여량 (3)", DamageCalculator.CalcPoisonAmount(stats, toxic, 1), 3f);
        AssertNear("독 — 펠릿2 부여량 (6)", DamageCalculator.CalcPoisonAmount(stats, toxic, 2), 6f);

        // 4) 독 상태 — 감쇠·소멸 없이 누적(다다익선). 8발 × 3턴
        var poison = new PoisonState();
        for (int i = 0; i < 8; i++) poison.Apply(3f);
        AssertNear("독상태 — 1턴 8발 누적 (24)", poison.TickAtTurnEnd(), 24f);
        for (int i = 0; i < 8; i++) poison.Apply(3f);
        AssertNear("독상태 — 2턴차 누적 (48, 감쇠 없음)", poison.TickAtTurnEnd(), 48f);

        // 5) 감전 — 동일 전기탄 반복 시 발당 누적으로 상승(종전엔 2번째부터 기여 0)
        float req = 150f;                       // 임계 = 0.1 × 150 = 15
        var shock = new ShockState();
        shock.ApplyFromHit(8f, req);            // 단발: 0.5 × (8/15) = 26.67% + 누적 3% = 29.67%
        float after1 = shock.Magnitude;
        AssertNear("감전 — 1발 (26.67% + 3% = 29.67%)", after1, 0.2966667f);
        shock.ApplyFromHit(8f, req);            // 단발분은 갱신 없음(동일) → 누적 3%만
        AssertNear("감전 — 2발째도 누적분 상승 (32.67%)", shock.Magnitude, 0.3266667f);
        AssertBool("감전 — 2발째 기여 > 0 (종전 규칙이면 0)", shock.Magnitude > after1);

        // 6) 감전 — 천장 clamp (누적이 생겨 명시적 상한이 필요해진 지점)
        var capped = new ShockState();
        for (int i = 0; i < 20; i++) capped.ApplyFromHit(8f, req);
        AssertNear("감전 — 천장 clamp (50%)", capped.Magnitude, GameConfig.ShockMaxMagnitude);

        // 7) 감전 — 강도%는 천장까지 함께 올린다
        var strong = new ShockState();
        for (int i = 0; i < 20; i++) strong.ApplyFromHit(8f, req, 0.5f);
        AssertNear("감전 — 강도50%면 천장도 75%", strong.Magnitude, 0.75f);

        // 8) 감전 — 후반 스테이지는 단발 기여가 급감(임계가 요구치 비례) → 누적분이 주력
        var late = new ShockState();
        late.ApplyFromHit(8f, 1470f);           // 임계 147 → 0.5 × (8/147) = 2.72% + 3% = 5.72%
        AssertNear("감전 — 후반 1발 (2.72% + 3% = 5.72%)", late.Magnitude, 0.0572109f);

        // 9) 회귀 — 배율 1(기본)이면 종전 결과와 동일
        AssertNear("회귀: 배율 미지정 화상 = 속성 즉발 (8)", DamageCalculator.CalcBurnAmount(stats, fire), 8f);
        AssertNear("회귀: 감전 단발만(1회) = 종전 공식 (26.67% + 3%)", shock.Magnitude - 0.03f, 0.2966667f);
    }

    // 보스 패시브 — 배정·요구치 가중·Resolver (M2 세션3: GDD §5.4)
    private static void BossScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 보스 패시브 (BossLibrary / BossGimmickResolver)");
        Console.WriteLine($" 보스 주기 {GameConfig.BossStageCycle} · 전 기믹 경감형(무효 없음) · 폴백 배정 = 풀 정의 순서");
        Console.WriteLine("--------------------------------------------------");

        // 1) 등장 순번 배정 (결정적 폴백 — 시드 미설정 시 풀 순서 그대로)
        AssertBool("스테3 = 물리 경감", BossLibrary.GetBoss(3).Gimmick == BossGimmick.PhysicalResist);
        AssertBool("스테6 = 화염 저항", BossLibrary.GetBoss(6).Gimmick == BossGimmick.FireResist);
        AssertBool("일반 스테(4) = 패시브 없음", !BossLibrary.GetBoss(4).HasGimmick);
        AssertBool("엔들리스 순환 (스테36 = 스테3 패시브 · 풀 11종)",
            BossLibrary.GetBoss(36).Gimmick == BossLibrary.GetBoss(3).Gimmick);

        // 2) 체력 증가 — 보스 곡선에 추가 ×1.5 (실질 ×3). 폴백 배정상 스테27이 HighDemand
        AssertBool("스테27 = 체력 증가", BossLibrary.GetBoss(27).Gimmick == BossGimmick.HighDemand);
        AssertNear("체력증가(스테27) — 보스 곡선 × 추가 ×1.5",
            StageProgression.GetStage(27).RequiredDamage, StageProgression.GetRequired(27) * 1.5f);
        AssertNear("비-가중 보스(스테6)는 곡선값 그대로", StageProgression.GetStage(6).RequiredDamage, StageProgression.GetRequired(6));

        // 3) 저항 — 대상 속성만 감소, 물리·타속성은 온전
        BossSpec fireResist = BossLibrary.Make(BossGimmick.FireResist);
        AssertNear("화염저항 — 화염 즉발 ×0.25", BossGimmickResolver.ImmediateElementalFactor(fireResist, ElementType.Fire), 0.25f);
        AssertNear("화염저항 — 독 즉발 통과 ×1", BossGimmickResolver.ImmediateElementalFactor(fireResist, ElementType.Poison), 1f);
        AssertNear("화염저항 — 화상 부여 ×0.25", BossGimmickResolver.StatusFactor(fireResist, ElementType.Fire), 0.25f);
        AssertNear("화염저항 — 독 부여 통과 ×1", BossGimmickResolver.StatusFactor(fireResist, ElementType.Poison), 1f);
        AssertNear("화염저항 — 물리 영향 없음 ×1", BossGimmickResolver.PhysicalFactor(fireResist), 1f);

        // 4) 약점 — 대상 '외' 속성만 감소, 물리(무속성)는 항상 통과 = 우회로 보장
        BossSpec fireWeak = BossLibrary.Make(BossGimmick.FireWeakness);
        AssertNear("화염약점 — 화염 통과 ×1", BossGimmickResolver.ImmediateElementalFactor(fireWeak, ElementType.Fire), 1f);
        AssertNear("화염약점 — 독 ×0.4", BossGimmickResolver.ImmediateElementalFactor(fireWeak, ElementType.Poison), 0.4f);
        AssertNear("화염약점 — 전기 ×0.4", BossGimmickResolver.ImmediateElementalFactor(fireWeak, ElementType.Shock), 0.4f);
        AssertNear("화염약점 — 무속성 통과 ×1", BossGimmickResolver.ImmediateElementalFactor(fireWeak, ElementType.None), 1f);
        AssertNear("화염약점 — 물리 팩터 영향 없음 ×1", BossGimmickResolver.PhysicalFactor(fireWeak), 1f);
        AssertNear("화염약점 — 독 부여도 ×0.4", BossGimmickResolver.StatusFactor(fireWeak, ElementType.Poison), 0.4f);

        // 5) 물리 경감 / 상태이상 저항 / 치명 저항 / 장갑
        BossSpec physResist = BossLibrary.Make(BossGimmick.PhysicalResist);
        AssertNear("물리경감 — 물리 ×0.5", BossGimmickResolver.PhysicalFactor(physResist), 0.5f);
        AssertNear("물리경감 — 속성 즉발 영향 없음 ×1", BossGimmickResolver.ImmediateElementalFactor(physResist, ElementType.Fire), 1f);

        BossSpec statusResist = BossLibrary.Make(BossGimmick.StatusResist);
        AssertNear("상태저항 — 화상 부여 ×0.25", BossGimmickResolver.StatusFactor(statusResist, ElementType.Fire), 0.25f);
        AssertNear("상태저항 — 독 부여 ×0.25", BossGimmickResolver.StatusFactor(statusResist, ElementType.Poison), 0.25f);
        AssertNear("상태저항 — 감전 부여 ×0.25", BossGimmickResolver.StatusFactor(statusResist, ElementType.Shock), 0.25f);
        AssertNear("상태저항 — 속성 즉발은 통과 ×1", BossGimmickResolver.ImmediateElementalFactor(statusResist, ElementType.Fire), 1f);
        AssertNear("상태저항 — 물리 통과 ×1", BossGimmickResolver.PhysicalFactor(statusResist), 1f);

        BossSpec critResist = BossLibrary.Make(BossGimmick.CritResist);
        // ⭐ 2026-08-27 확률 → 배율 감산 전환. 확률을 깎으면 "안 터진다"(무효화)라 07-27의 '무효 금지' 원칙에
        //    혼자 어긋나 있었다. 초과분(배율−1)에만 곱하므로 배율이 1 밑으로 내려가지 않는다.
        AssertNear("치명저항 — 크리 배율 2.0 → 1.25 (초과분만 ×0.25)",
            BossGimmickResolver.CritMultiplier(critResist, 2.0f), 1.25f);
        AssertNear("치명저항 — 배율 5.0 → 2.0", BossGimmickResolver.CritMultiplier(critResist, 5.0f), 2.0f);
        AssertBool("치명저항 — 배율이 1 밑으로 내려가지 않는다(무효 금지)",
            BossGimmickResolver.CritMultiplier(critResist, 2.0f) >= 1f);
        AssertNear("None — 크리 배율 불변", BossGimmickResolver.CritMultiplier(BossSpec.None, 3.0f), 3.0f);

        BossSpec armor = BossLibrary.Make(BossGimmick.Armor);
        AssertNear("장갑 — 발당 감산 = 표적 요구치 3%", BossGimmickResolver.FlatReduction(armor, 1000f), 30f);
        AssertNear("장갑 — 요구치 비례(2배면 감산도 2배)", BossGimmickResolver.FlatReduction(armor, 2000f), 60f);
        AssertNear("None — 감산 0", BossGimmickResolver.FlatReduction(BossSpec.None, 1000f), 0f);
        AssertNear("None — 요구치 배율 ×1", BossGimmickResolver.RequiredMultiplier(BossSpec.None), 1f);

        // 6) 데드엔드 방지 불변식 — 전 기믹 × 전 속성에서 통과 배율이 0이 되면 안 된다 (2026-07-27 설계 원칙)
        bool anyZero = false;
        foreach (BossGimmick g in Enum.GetValues(typeof(BossGimmick)))
        {
            BossSpec s = BossLibrary.Make(g);
            if (BossGimmickResolver.PhysicalFactor(s) <= 0f) anyZero = true;
            foreach (ElementType e in Enum.GetValues(typeof(ElementType)))
            {
                if (BossGimmickResolver.ImmediateElementalFactor(s, e) <= 0f) anyZero = true;
                if (BossGimmickResolver.StatusFactor(s, e) <= 0f) anyZero = true;
            }
        }
        AssertBool("데드엔드 없음 — 전 기믹·전 속성 통과 배율 > 0", !anyZero);

        // 7) 로컬라이즈 키 조합 — enum 이름에서 파생(하드코딩 매핑 없음)
        AssertBool("키 조합 — FireResist → boss.fire_resist.name",
            BossGimmickKey.Name(BossGimmick.FireResist) == "boss.fire_resist.name");
        AssertBool("키 조합 — PoisonWeakness → boss.poison_weakness.desc",
            BossGimmickKey.Desc(BossGimmick.PoisonWeakness) == "boss.poison_weakness.desc");
        AssertBool("키 조합 — Armor → boss.armor.name", BossGimmickKey.Name(BossGimmick.Armor) == "boss.armor.name");
        AssertBool("키 조합 — None은 빈 문자열", BossGimmickKey.Name(BossGimmick.None).Length == 0);

        // 8) 풀 정합 — 모든 기믹이 풀에 등록돼 있어야 예고·Lab이 같은 값을 본다 (None 제외)
        bool allInPool = true;
        foreach (BossGimmick g in Enum.GetValues(typeof(BossGimmick)))
        {
            if (g == BossGimmick.None) continue;
            if (BossLibrary.Make(g).Gimmick != g) allInPool = false;
        }
        AssertBool("풀 정합 — 전 기믹이 BossLibrary 풀에 등록됨", allInPool);

        BossRampScenario();
    }

    // 보스 강도 램프 — 등장 순번별 약화 (2026-07-29: 초반 보스가 빌드업을 앞지르던 문제)
    private static void BossRampScenario()
    {
        Console.WriteLine();
        Console.WriteLine($"   [강도 램프] {GameConfig.BossPowerRampSteps}번째 보스에서 100% 도달 · Param은 중립값 방향 보간");

        // 1) 순번 → 강도 (램프 스텝 4 = 25/50/75/100, 이후 고정)
        AssertNear("강도 — 1번째 보스(스테3) 25%", BossLibrary.PowerScale(1), 0.25f);
        AssertNear("강도 — 2번째 보스(스테6) 50%", BossLibrary.PowerScale(2), 0.5f);
        AssertNear("강도 — 3번째 보스(스테9) 75%", BossLibrary.PowerScale(3), 0.75f);
        AssertNear("강도 — 4번째 보스(스테12) 100%", BossLibrary.PowerScale(4), 1f);
        AssertNear("강도 — 엔들리스(스테15, 5번째) 100% 고정", BossLibrary.PowerScale(5), 1f);
        AssertNear("강도 — GetBoss가 스펙에 강도를 실어 준다", BossLibrary.GetBoss(6).PowerScale, 0.5f);
        AssertNear("강도 — 일반 스테는 None(강도 1, 기믹 없음)", BossLibrary.GetBoss(4).PowerScale, 1f);

        // 2) 통과 배율 = 중립(1) 방향 보간 → 초반일수록 1에 가깝다. 폴백 배정상 스테3=물리경감·스테6=화염저항
        AssertNear("물리경감 25% — 통과 ×0.875 (원값 0.5)", BossGimmickResolver.PhysicalFactor(BossLibrary.GetBoss(3)), 0.875f);
        AssertNear("화염저항 50% — 화염 통과 ×0.625 (원값 0.25)",
            BossGimmickResolver.ImmediateElementalFactor(BossLibrary.GetBoss(6), ElementType.Fire), 0.625f);
        AssertNear("화염저항 50% — 화상 부여도 같은 배율",
            BossGimmickResolver.StatusFactor(BossLibrary.GetBoss(6), ElementType.Fire), 0.625f);
        AssertNear("화염저항 50% — 비대상 속성은 여전히 통과 ×1",
            BossGimmickResolver.ImmediateElementalFactor(BossLibrary.GetBoss(6), ElementType.Poison), 1f);

        // 3) 예외 규약 둘 — 체력증가(중립 1, 위로) · 장갑(중립 0, 감산 비율)
        BossSpec demand25 = BossLibrary.Make(BossGimmick.HighDemand).WithPower(0.25f);
        AssertNear("체력증가 25% — 요구 배율 ×1.125 (원값 1.5)", BossGimmickResolver.RequiredMultiplier(demand25), 1.125f);
        BossSpec armor25 = BossLibrary.Make(BossGimmick.Armor).WithPower(0.25f);
        AssertNear("장갑 25% — 발당 감산 = 요구치 0.75%", BossGimmickResolver.FlatReduction(armor25, 1000f), 7.5f);

        // 4) 경계 — 강도 0이면 패시브 없음과 동일, 1이면 원값 그대로
        BossSpec zero = BossLibrary.Make(BossGimmick.PhysicalResist).WithPower(0f);
        AssertNear("강도 0 — 물리 통과 ×1 (무패시브와 동일)", BossGimmickResolver.PhysicalFactor(zero), 1f);
        AssertNear("강도 0 — 장갑 감산 0",
            BossGimmickResolver.FlatReduction(BossLibrary.Make(BossGimmick.Armor).WithPower(0f), 1000f), 0f);
        AssertNear("강도 1 — 원값 보존 (물리 ×0.5)",
            BossGimmickResolver.PhysicalFactor(BossLibrary.Make(BossGimmick.PhysicalResist).WithPower(1f)), 0.5f);
        AssertBool("None에 램프 적용 — 여전히 기믹 없음", !BossSpec.None.WithPower(0.5f).HasGimmick);

        // 5) 데드엔드 불변식 (램프판) — 중립 방향으로만 움직이므로 어떤 강도에서도 배율 0이 될 수 없다
        bool anyZero = false;
        float[] scales = { 0f, 0.25f, 0.5f, 0.75f, 1f };
        foreach (BossGimmick g in Enum.GetValues(typeof(BossGimmick)))
        {
            foreach (float sc in scales)
            {
                BossSpec s = BossLibrary.Make(g).WithPower(sc);
                if (BossGimmickResolver.PhysicalFactor(s) <= 0f) anyZero = true;
                foreach (ElementType e in Enum.GetValues(typeof(ElementType)))
                {
                    if (BossGimmickResolver.ImmediateElementalFactor(s, e) <= 0f) anyZero = true;
                    if (BossGimmickResolver.StatusFactor(s, e) <= 0f) anyZero = true;
                }
            }
        }
        AssertBool("데드엔드 없음 — 전 기믹 × 전 강도 × 전 속성 통과 배율 > 0", !anyZero);

        // 6) 설명문 파생값 — csv에 숫자가 없으므로 램프가 걸려도 표시가 어긋나지 않는다
        AssertBool("설명값 — 화염저항 100% → \"75\"", BossEffectText.Value(BossLibrary.Make(BossGimmick.FireResist)) == "75");
        AssertBool("설명값 — 화염저항 25% → \"19\" (18.75 반올림)",
            BossEffectText.Value(BossLibrary.Make(BossGimmick.FireResist).WithPower(0.25f)) == "19");
        AssertBool("설명값 — 물리경감 25% → \"13\" (12.5는 올림)",
            BossEffectText.Value(BossLibrary.Make(BossGimmick.PhysicalResist).WithPower(0.25f)) == "13");
        AssertBool("설명값 — 체력증가 100% → \"1.5\" (보스 가중 1 = 기믹 배율 그대로)",
            BossEffectText.Value(BossLibrary.Make(BossGimmick.HighDemand)) == "1.5");
        AssertBool("설명값 — 체력증가 25% → \"1.13\"",
            BossEffectText.Value(demand25) == "1.13");
        AssertBool("설명값 — 장갑 25% → \"0.75\"", BossEffectText.Value(armor25) == "0.75");
        AssertBool("설명값 — 기믹 없음은 빈 문자열", BossEffectText.Value(BossSpec.None).Length == 0);

        // 7) 실제 요구치 — 스테3 체력증가가 걸려도 초반엔 완화된다 (램프 도입 동기)
        float req3 = StageProgression.GetRequired(3);
        AssertNear("요구치 — 스테3 체력증가면 ×1.125 (종전 ×1.5)",
            req3 * BossGimmickResolver.RequiredMultiplier(demand25), req3 * 1.125f);
    }

    // 총기 밸런스 맵 — 슬롯×탄창=48 4총기 × 빌드 투자별 클리어 도달 (M2 세션3 요구치 곡선 튜닝 근거)
    private static void GunBalanceScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 총기 밸런스 맵 — 48 불변량 4총기 (요구치 곡선 튜닝 근거)");
        Console.WriteLine($" 곡선: base {GameConfig.StageBaseRequirement:0} + step {GameConfig.StageLinearStep:0}/스테 · 보스 ×{GameConfig.BossRequirementMultiplier}(체력증가 추가 ×1.5)");
        Console.WriteLine("--------------------------------------------------");

        // 스테이지 요구 곡선 1~15 (보스·가중 반영 = GetStage)
        Console.Write("   요구치:");
        for (int i = 1; i <= 15; i++)
        {
            var s = StageProgression.GetStage(i);
            Console.Write($" {(s.IsBoss ? "★" : "")}{s.RequiredDamage:0}");
        }
        Console.WriteLine("\n   (★=보스 — 요구치는 곡선 그대로, 난이도는 패시브가 전담)\n");

        // (이름, 탄창크기, 슬롯, 크기잠금) — 48 불변량
        (string name, int mag, int slot, bool locked)[] guns =
        {
            ("리볼버 ", 6, 8, true),
            ("소총   ", 8, 6, false),
            ("기관총 ", 12, 4, false),
            ("저격총 ", 4, 12, false),
        };

        Console.WriteLine("   총기   | 빈빌드 출력·도달 | 슬롯풀강(모드만) 도달 | 물량키트(모드+악세) 출력·도달");
        foreach (var g in guns)
        {
            var empty = new RunBuild(10f, g.mag, 4, g.slot, g.locked);

            var full = new RunBuild(10f, g.mag, 4, g.slot, g.locked);
            FillStrong(full);

            var kit = new RunBuild(10f, g.mag, 4, g.slot, g.locked);
            FillStrong(kit);
            FillKit(kit); // 물량 악세: 탄창수 +2, 비잠금이면 탄창크기 +4

            Console.WriteLine($"   {g.name}| {empty.GetResult().StageTotal,6:0} / 스테{MaxClearedStage(empty.ToBuildStats(), 15),-2} "
                            + $"     | 스테{MaxClearedStage(full.ToBuildStats(), 15),-2}                | "
                            + $"{kit.GetResult().StageTotal,9:0} / 스테{MaxClearedStage(kit.ToBuildStats(), 15),-2}  ({kit.GetResult().TotalShots}발)");
        }
        Console.WriteLine("   ※ 슬롯풀강 = Flat+5·증가100%·나머지 승산50% / 물량키트 = +탄창수2·(비잠금)탄창크기4 추가");

        // 속성/DoT 아키타입 — 소이탄 시퀀스 + 속성증가 모드의 화상 누적(턴-워크). 즉발만 보는 위 컬럼이 못 보는 DoT 기여 확인.
        Console.WriteLine();
        var dot = new RunBuild(10f, 8, 4, 6); // 소총 — DoT 표본(범용 슬롯6)
        for (int i = 0; i < 4; i++) dot.Sequence.Set(i, new AmmoEntry(new AmmoSpec(6f, ElementType.Fire, 8f), 6, true), out _); // 절반 소이탄
        dot.EquipMod(new ModSpec(ModType.ElementalIncreased, 0.5f));
        dot.EquipMod(new ModSpec(ModType.ElementalIncreased, 0.5f)); // 속성증가 +100% → 화염 16
        dot.EquipMod(new ModSpec(ModType.StatusIntensity, 0.5f));    // 화상 부여량 ×1.5
        int dotReach = MaxClearedStageBurnWalk(dot, 15);
        int immediateReach = MaxClearedStage(dot.ToBuildStats(), 15); // DoT 빼고 즉발만
        Console.WriteLine($"   속성/DoT(소총·소이탄4+속성증가100%+강도50%): 도달 스테{dotReach} (즉발만이면 스테{immediateReach} → DoT가 {dotReach - immediateReach}스테 더)");

        // 기관총 물량+특수탄 — base-ammo로 스테9(가중요구치 ★6619) 벽인 기관총이 특수탄(소이탄 DoT)으로 넘는지
        var mgDot = new RunBuild(10f, 12, 4, 4, false);
        FillStrong(mgDot); FillKit(mgDot); // 모드4 + 물량 악세
        int mgSize = mgDot.MagazineSize;
        for (int i = 0; i < mgSize; i += 2) mgDot.Sequence.Set(i, new AmmoEntry(new AmmoSpec(6f, ElementType.Fire, 8f), 6, true), out _); // 절반 소이탄
        Console.WriteLine($"   기관총 물량+소이탄(절반·{mgDot.GetResult().TotalShots}발): 도달 스테{MaxClearedStageBurnWalk(mgDot, 15)} (base-ammo 물량키트는 스테8 — 가중보스 벽)");
        Console.WriteLine("   ※ 도달 = 스테1부터 연속 클리어 최대치 (보스 데미지경감 패시브는 미반영 — 실제 보스에선 면역/경감만큼 하향)");
    }

    // ── 표적 물량전 모델 (2026-08-26 — MOD_AMMO_AUDIT §17) ────────────────
    // 종전 `표적 체력 = 요구치 ÷ 표적 수 ÷ 웨이브`를 계단 테이블로 갈아엎었다. 여기서 지키는 것은
    // ① 스테이지 편성이 문서 표와 같은가 ② 여유 배수가 유지되는가 ③ 배치가 규칙·시드를 지키는가
    // ④ 소환 예산·필드 상한을 넘지 않는가 ⑤ 데드엔드가 안 나는가.
    private static void TargetModelScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 표적 물량전 모델 — 편성 · 배치 · 리필");
        Console.WriteLine($" 여유 ×{GameConfig.TargetHeadroomMultiplier} · 필드 상한 {GameConfig.FieldCapMin}~{GameConfig.FieldCapMax}"
                        + $" · 클러스터 {GameConfig.ClusterPlacementMaxTargets}기 이하 · 격자 {BattleGrid.Rows}×{BattleGrid.Cols}");
        Console.WriteLine("--------------------------------------------------");

        // ① 일반 스테이지 편성 = 문서 §17.3 표. 보스 스테이지는 공식이 달라(보스가 포인트 60%) 여기서 뺀다.
        (int stage, float hp, int total, int cap)[] table =
        {
            (1, 75f, 3, 2), (2, 200f, 4, 2), (4, 320f, 6, 4), (5, 320f, 8, 5),
            (7, 410f, 9, 5), (8, 410f, 10, 6), (10, 470f, 12, 7), (11, 470f, 13, 8),
        };
        bool tableOk = true;
        foreach ((int stage, float hp, int total, int cap) e in table)
        {
            StageSpec s = StageProgression.GetStage(e.stage);
            if (!Near(s.TargetHealth, e.hp) || s.TotalSpawns != e.total || s.FieldCap != e.cap)
            {
                tableOk = false;
                Console.WriteLine($"   ⚠ 스{e.stage}: 체력 {s.TargetHealth}(기대 {e.hp}) · 총 {s.TotalSpawns}({e.total}) · 필드 {s.FieldCap}({e.cap})");
            }
        }
        AssertBool("일반 스테이지 편성 = §17.3 표 (8스테 전건)", tableOk);

        // ② 여유 배수 — 총 포인트가 요구치를 크게 웃돌아야 "40%만 잡으면 클리어"가 성립한다.
        //    ⚠ 하한 2.2로 잰다: 총 소환이 정수 반올림이라 스1(100÷75=3.33→3기=2.25배)처럼
        //      체력 대비 요구치가 작은 초반에서 오차가 가장 크다. 튜토리얼 구간이라 수용한다.
        bool headroomOk = true, growthOk = true;
        float prevHp = 0f;
        for (int n = 1; n <= GameConfig.RunCompletionStage; n++)
        {
            StageSpec s = StageProgression.GetStage(n);
            float points = s.TotalSpawns * s.TargetHealth + s.BossHealth;
            if (points < s.RequiredDamage * 2.2f)
            {
                headroomOk = false;
                Console.WriteLine($"   ⚠ 스{n} 여유 부족: 총 포인트 {points:0} / 요구 {s.RequiredDamage:0} = {points / s.RequiredDamage:0.00}배");
            }
            // ⚠ 단조 증가는 <b>일반 계단</b>으로 잰다 — spec.TargetHealth는 보스전에서 호위값(일반×0.6)이라
            //   스2(200) → 스3 호위(120)로 내려간다. 그건 정상이고 계단이 깨진 것이 아니다.
            float stepHp = StageProgression.GetTargetHealth(n);
            if (stepHp < prevHp) growthOk = false;
            prevHp = stepHp;
        }
        AssertBool("여유 배수 유지 — 총 포인트 ≥ 요구치 × 2.2 (12스테 전건)", headroomOk);
        AssertBool("체력 계단 단조 증가", growthOk);
        AssertBool("체력 증가율이 6.5배 이하 (종전 26배 — 성장 체감의 근거)",
            StageProgression.GetTargetHealth(12) / StageProgression.GetTargetHealth(1) <= 6.5f);

        // ③ 배치 — 클러스터 규칙·시드 결정성·보스전 금지 칸
        bool clusterOk = true;
        for (int seed = 0; seed < 300; seed++)
        {
            int mask = new TargetPlacer(seed).PlaceInitial(GameConfig.ClusterPlacementMaxTargets, false);
            if (!AllAdjacent(mask)) { clusterOk = false; break; }
        }
        AssertBool($"클러스터 배치 — {GameConfig.ClusterPlacementMaxTargets}기 전원이 최소 1기와 인접 (시드 300개)", clusterOk);

        AssertBool("시드 결정적 — 같은 시드면 같은 배치",
            new TargetPlacer(77).PlaceInitial(5, false) == new TargetPlacer(77).PlaceInitial(5, false));
        AssertBool("시드가 다르면 배치도 갈린다",
            new TargetPlacer(1).PlaceInitial(5, false) != new TargetPlacer(2).PlaceInitial(5, false));

        int bossMask = new TargetPlacer(9).PlaceInitial(13, bossStage: true);
        bool blockedClear = true;
        for (int cell = 0; cell < BattleGrid.CellCount; cell++)
        {
            if ((bossMask & (1 << cell)) == 0) continue;
            if (!BattleGrid.CanPlace(cell, true)) { blockedClear = false; break; }
        }
        AssertBool("보스전 — 금지 칸·보스 자리에 배치되지 않는다", blockedClear);
        AssertBool("보스전 배치 가능 칸 = 13 / 일반 = 18",
            BattleGrid.PlaceableCount(true) == 13 && BattleGrid.PlaceableCount(false) == 18);

        // ④ 소환 예산·필드 상한 — 전 스테이지를 끝까지 밀어 실제 소환 수를 센다
        bool budgetOk = true, capOk = true;
        for (int n = 1; n <= GameConfig.RunCompletionStage; n++)
        {
            StageSpec spec = StageProgression.GetStage(n);
            var group = new TargetGroup();
            int filled = group.Initialize(spec, runSeed: 12345);
            int spawned = BitCount(filled) - (spec.IsBoss ? 1 : 0); // 보스는 예산 밖
            if (group.AliveNormalCount > spec.FieldCap) capOk = false;

            // 전멸 → 리필을 반복해 예산이 바닥날 때까지.
            // ⚠ 처치할 때 포인트를 반드시 적립한다 — 안 하면 "포인트 미달"이 계속 참이라
            //   데드엔드 방어가 매 턴 발동해 예산 밖 소환이 섞인다(실제 전투에서는 처치 = 적립).
            for (int guard = 0; guard < 100; guard++)
            {
                for (int i = 0; i < group.Count; i++)
                {
                    TargetState t = group[i];
                    if (t == null || t.IsDead || t.IsBoss) continue;
                    t.ApplyDamage(t.MaxHealth);
                    group.RegisterKill(i);
                }
                int more = group.Refill();
                if (more == 0) break;
                spawned += BitCount(more);
                if (group.AliveNormalCount > spec.FieldCap) capOk = false;
            }
            if (spawned != spec.TotalSpawns)
            {
                budgetOk = false;
                Console.WriteLine($"   ⚠ 스{n} 소환 수 {spawned} ≠ 예산 {spec.TotalSpawns}");
            }
        }
        AssertBool("소환 총량 = 예산 (12스테 전건)", budgetOk);
        AssertBool("필드 상한을 넘지 않는다", capOk);

        // ⑤ 데드엔드 방어 — 예산을 다 쓰고 필드가 비었는데 포인트가 모자라면 강제로 1기 낸다.
        //    포인트를 적립하지 않고 죽이는 것 = "전부 오버킬로 샜다"는 최악 가정.
        {
            StageSpec spec = StageProgression.GetStage(6);
            var group = new TargetGroup();
            group.Initialize(spec, runSeed: 1);
            int emergency = 0;
            for (int guard = 0; guard < 200; guard++)
            {
                for (int i = 0; i < group.Count; i++)
                {
                    TargetState t = group[i];
                    if (t != null && !t.IsDead && !t.IsBoss) t.ApplyDamage(t.MaxHealth);
                }
                int more = group.Refill();
                if (more == 0) break;
                if (group.RemainingSpawns == 0) emergency++; // 예산이 바닥난 뒤의 소환 = 방어 발동
            }
            AssertBool("데드엔드 방어가 발동한다 (쏠 대상 없이 턴만 소모되는 것을 막는다)", emergency > 0);
            AssertBool($"⚠ 방어에 상한이 있다 — 무한 소환 금지 (≤ 필드 상한 {spec.FieldCap})", emergency <= spec.FieldCap);
        }

        // ⑥ 처치 포인트 — 최대 체력만큼만 들어가고 오버킬은 버려진다
        {
            var group = new TargetGroup();
            StageSpec spec = StageProgression.GetStage(2);
            group.Initialize(spec, runSeed: 5);
            int cell = group.FindWeakestAlive();
            TargetState t = group[cell];
            float max = t.MaxHealth;
            AssertBool("과잉 피해로 처치", t.ApplyDamage(max * 10f));
            group.RegisterKill(cell);
            AssertNear("처치 포인트 = 최대 체력 (오버킬 버려짐)", group.EarnedPoints, max);
            AssertBool("처치된 칸은 IsDead — 빈 칸과 같은 취급", group[cell].IsDead);
        }

        // ⑦ 한 발 처치 판정 — UnlockStat.OneShotKills · M30 일격필살의 근거 (2026-08-26 배선)
        {
            var group = new TargetGroup();
            group.Initialize(StageProgression.GetStage(2), runSeed: 11);
            int cell = group.FindWeakestAlive();
            TargetState t = group[cell];

            AssertBool("갓 세운 표적 = 만피", t.IsFullHealth);
            t.ApplyDamage(t.MaxHealth * 0.1f);
            AssertBool("한 대 맞으면 만피가 아니다 (= 한 발 처치로 안 센다)", !t.IsFullHealth);

            // 빈 칸은 만피일 수 없다 — 세워지지 않은 칸이 "한 발 처치" 후보가 되면 안 된다
            int empty = -1;
            for (int i = 0; i < group.Count; i++)
                if (group[i] != null && group[i].IsDead) { empty = i; break; }
            AssertBool("빈 칸은 IsFullHealth = false", empty >= 0 && !group[empty].IsFullHealth);
        }

        Console.WriteLine($"   스1 체력 {StageProgression.GetTargetHealth(1):0} → 스12 {StageProgression.GetTargetHealth(12):0}"
                        + $" ({StageProgression.GetTargetHealth(12) / StageProgression.GetTargetHealth(1):0.0}배 — 종전 26배)");
    }

    // ── L2-b 표적 조건 (2026-08-26 — MOD_AMMO_AUDIT §9.1) ──────────────────
    // int mask → TargetContext로 넓힌 배관. 조건 11종이 이 구조체 하나에서 판정된다.
    // 여기서 지키는 것 = ① 조준 대상이 없으면 전부 불충족 ② 각 조건이 제 재료를 본다
    // ③ 표적 조건이 전역 집계로 새지 않는다(IsTargetCondition 누락 검출).
    private static void TargetConditionScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 표적 조건 (L2-b) — TargetContext 판정");
        Console.WriteLine("--------------------------------------------------");

        // 기준 빌드 — 탄당 10, 조건 모드가 붙으면 승산 +100%로 두 배가 된다
        static RunBuild Fresh() => new RunBuild(10f, 4, 1, 6);
        const float Base = 10f;

        // 조건별 검증: (이름, 조건, CondParam, CondElement, 충족 컨텍스트, 불충족 컨텍스트)
        var full   = new TargetContext(0, 1.0f, 100f, 0f, 0f, 0, 1, false);
        var hurt   = new TargetContext(0, 0.25f, 25f, 0f, 0f, 0, 1, false);
        var burning= new TargetContext(1 << (int)ElementType.Fire, 1f, 100f, 20f, 0f, 0, 1, false);
        var allSt  = new TargetContext(TargetContext.AllStatusMask, 1f, 100f, 5f, 3f, 0, 1, false);
        var packed = new TargetContext(0, 1f, 100f, 0f, 0f, 3, 3, false);
        var boss   = new TargetContext(0, 1f, 100f, 0f, 0f, 0, 1, true);
        var burst  = new TargetContext(1 << (int)ElementType.Fire, 0.2f, 20f, 50f, 0f, 0, 1, false);
        var toxic  = new TargetContext(1 << (int)ElementType.Poison, 1f, 100f, 0f, 6f, 0, 1, false);

        (string name, ConditionType cond, int param, ElementType elem, TargetContext on, TargetContext off)[] cases =
        {
            ("체력 30% 이하 (M09 처형자)",      ConditionType.TargetHealthBelow,     30, ElementType.None,   hurt,    full),
            ("만피 (M10 선제타격)",             ConditionType.TargetFullHealth,       0, ElementType.None,   full,    hurt),
            ("무상태 (M22·M45)",                ConditionType.TargetNoStatus,         0, ElementType.None,   full,    burning),
            ("상태 3종 전부 (M23 삼중 중독)",   ConditionType.TargetAllStatus,        0, ElementType.None,   allSt,   burning),
            ("인접 있음 (M21 밀집 사격)",       ConditionType.TargetHasAdjacent,      0, ElementType.None,   packed,  full),
            ("같은 행 3기↑ (M31 행렬 강타)",    ConditionType.TargetRowCountAtLeast,  3, ElementType.None,   packed,  full),
            ("독 스택 5↑ (M34 독성 관통)",      ConditionType.TargetPoisonAtLeast,    5, ElementType.None,   toxic,   full),
            ("보스 (M35 보스 특효)",            ConditionType.TargetIsBoss,           0, ElementType.None,   boss,    full),
            ("화상 ≥ 체력 (M41 과열 소각)",     ConditionType.TargetBurnOverHealth,   0, ElementType.None,   burst,   burning),
            ("화상 상태 (기존 TargetHasStatus)", ConditionType.TargetHasStatus,       0, ElementType.Fire,   burning, full),
        };

        bool allOk = true;
        foreach ((string name, ConditionType cond, int param, ElementType elem, TargetContext on, TargetContext off) c in cases)
        {
            RunBuild b = Fresh();
            b.EquipMod(new ModSpec(ModType.More, 1.0f, c.cond, c.elem, c.param));
            BuildStats bs = b.ToBuildStats();

            float hit  = DamageCalculator.CalcPerShotBeforeCrit(b.ApplyTargetStatus(bs, c.on), Base);
            float miss = DamageCalculator.CalcPerShotBeforeCrit(b.ApplyTargetStatus(bs, c.off), Base);
            float none = DamageCalculator.CalcPerShotBeforeCrit(b.ApplyTargetStatus(bs, TargetContext.None), Base);

            bool ok = Near(hit, Base * 2f) && Near(miss, Base) && Near(none, Base);
            if (!ok) { allOk = false; Console.WriteLine($"   ⚠ {c.name}: 충족 {hit:0.0}(기대 20) · 미충족 {miss:0.0}(10) · 대상없음 {none:0.0}(10)"); }
        }
        AssertBool($"표적 조건 {cases.Length}종 — 충족 시만 적용 · 조준 대상 없으면 전부 불충족", allOk);

        // ⚠ 이번 탄 속성 조건(M13)은 탄 속성을 인자로 받는다 — 무속성 탄은 "그 속성의 상태"가 정의되지 않아 불충족
        {
            RunBuild b = Fresh();
            b.EquipMod(new ModSpec(ModType.More, 1.0f, ConditionType.TargetShotElementStatus));
            BuildStats bs = b.ToBuildStats();
            AssertNear("M13 — 화염탄 + 화상 표적 = 발동",
                DamageCalculator.CalcPerShotBeforeCrit(b.ApplyTargetStatus(bs, burning, ElementType.Fire), Base), Base * 2f);
            AssertNear("M13 — 독탄 + 화상 표적 = 불발 (속성 불일치)",
                DamageCalculator.CalcPerShotBeforeCrit(b.ApplyTargetStatus(bs, burning, ElementType.Poison), Base), Base);
            AssertNear("M13 — 무속성 탄 = 불발 (걸 상태가 없다)",
                DamageCalculator.CalcPerShotBeforeCrit(b.ApplyTargetStatus(bs, burning, ElementType.None), Base), Base);
        }

        // 신규 표적 조건 3종 (2026-08-27 크리 축 보강) — 단독 사격 · 눈앞의 적 · 후열 저격
        {
            var alone = new TargetContext(0, 1f, 100f, 0f, 0f, 1, 1, false);  // 인접 1
            var crowd = new TargetContext(0, 1f, 100f, 0f, 0f, 3, 1, false);  // 인접 3
            var front = new TargetContext(0, 1f, 100f, 0f, 0f, 0, 1, false, isFrontMost: true);
            var rear  = new TargetContext(0, 1f, 100f, 0f, 0f, 0, 1, false, isRearMost: true);
            var mid   = new TargetContext(0, 1f, 100f, 0f, 0f, 0, 1, false);

            RunBuild lone = Fresh();
            lone.EquipMod(new ModSpec(ModType.CritChance, 0.15f, ConditionType.TargetAdjacentAtMost, ElementType.None, 1));
            BuildStats ls = lone.ToBuildStats();
            AssertNear("단독 사격 — 인접 1기 = 발동", lone.ApplyTargetStatus(ls, alone).CritChance, ls.CritChance + 0.15f);
            AssertNear("단독 사격 — 인접 3기 = 불발", lone.ApplyTargetStatus(ls, crowd).CritChance, ls.CritChance);

            RunBuild pb = Fresh();
            pb.EquipMod(new ModSpec(ModType.CritChance, 0.15f, ConditionType.TargetFrontMost));
            BuildStats ps = pb.ToBuildStats();
            AssertNear("눈앞의 적 — 앞줄 = 발동", pb.ApplyTargetStatus(ps, front).CritChance, ps.CritChance + 0.15f);
            AssertNear("눈앞의 적 — 중간 = 불발", pb.ApplyTargetStatus(ps, mid).CritChance, ps.CritChance);

            RunBuild rs = Fresh();
            rs.EquipMod(new ModSpec(ModType.CritMultiplier, 0.5f, ConditionType.TargetRearMost));
            BuildStats rss = rs.ToBuildStats();
            AssertNear("후열 저격 — 뒷줄 = 발동", rs.ApplyTargetStatus(rss, rear).CritMultiplier, rss.CritMultiplier + 0.5f);
            AssertNear("후열 저격 — 중간 = 불발", rs.ApplyTargetStatus(rss, mid).CritMultiplier, rss.CritMultiplier);
            AssertBool("⚠ 앞줄·뒷줄은 배타가 아니다 — 표적이 1기뿐이면 둘 다 참",
                front.IsFrontMost && rear.IsRearMost);
        }

        // ⭐ 정밀 초과 — 크리율 100% 초과분을 배율로 전환 (2026-08-27)
        {
            var s = BuildStats.Create(10f, 8, 4);
            s.CritChance = 1.25f;                 // 125% — 25%p 초과
            s.CritMultiplier = 2f;

            DamageCalculator.ResolveCrit(s, out float c0, out float m0);
            AssertNear("전환 모드 없음 — 확률은 100%로 잘리고", c0, 1f);
            AssertNear("전환 모드 없음 — 초과분은 그대로 버려진다(회귀 없음)", m0, 2f);

            s.CritOverflowRate = 2f;              // 초과 1.0당 배율 +2 → 0.25 × 2 = +0.5
            DamageCalculator.ResolveCrit(s, out float c1, out float m1);
            AssertNear("정밀 초과 — 확률 100%", c1, 1f);
            AssertNear("정밀 초과 — 초과 25%p → 배율 +0.5", m1, 2.5f);
            AssertNear("정밀 초과 — 기댓값이 실전투와 일치 (100% × 2.5)",
                DamageCalculator.CritExpectedMultiplier(c1, m1), 2.5f);

            var under = BuildStats.Create(10f, 8, 4);
            under.CritChance = 0.6f; under.CritMultiplier = 3f; under.CritOverflowRate = 2f;
            DamageCalculator.ResolveCrit(under, out float c2, out float m2);
            AssertBool("초과가 없으면 전환도 없다", Near(c2, 0.6f) && Near(m2, 3f));
        }

        // ⭐⭐ 원소 치명 — 속성에 크리 활로를 연다 (2026-08-27). 치명 공명(배율 환전)과 다른 축이다.
        {
            var fireAmmo = new AmmoSpec(6f, ElementType.Fire, 8f); // 물리 6 · 화염 8
            var plain = BuildStats.Create(10f, 8, 4);
            plain.CritChance = 0.5f; plain.CritMultiplier = 2f;

            AssertNear("회귀 — 원소 치명 없으면 속성에 크리 미적용", DamageCalculator.ElementalCritExpected(plain), 1f);
            AssertNear("회귀 — 속성 즉발 그대로 (8)", DamageCalculator.CalcElemental(plain, fireAmmo), 8f);

            var ec = plain;
            ec.ElementalCritBonus = 1f;  // 크리 시 속성 ×2
            AssertNear("원소 치명 — 기댓값 = 1 + 0.5×1 = 1.5", DamageCalculator.ElementalCritExpected(ec), 1.5f);
            AssertNear("원소 치명 — 탄당 속성 8 → 12 (물리 6×1.5 = 9 별도)",
                DamageCalculator.CalcPerShot(ec, fireAmmo), 6f * 1.5f + 8f * 1.5f);
            AssertNear("원소 치명 — 크리 롤 시 ×2", DamageCalculator.ElementalCritRolled(ec, crit: true), 2f);
            AssertNear("원소 치명 — 비크리는 ×1", DamageCalculator.ElementalCritRolled(ec, crit: false), 1f);

            // ⚠⚠ 더블딥 방지 — 상태이상 부여량은 크리 이전 값을 쓴다
            AssertNear("⚠ 상태이상 부여량은 크리를 안 받는다 (화상 기준 8 유지)",
                DamageCalculator.CalcStatusSource(ec, fireAmmo), DamageCalculator.CalcStatusSource(plain, fireAmmo));

            // 확률 공유 — 정밀 초과로 확률이 잘린 뒤의 값을 쓴다
            var over = plain;
            over.ElementalCritBonus = 1f; over.CritChance = 1.4f;
            AssertNear("확률 공유 — 100%로 잘린 뒤 적용 (1 + 1.0×1 = 2.0)",
                DamageCalculator.ElementalCritExpected(over), 2f);

            // 치명 공명과는 다른 축 — 저쪽은 속성 '증가%'를 올리고 크리를 열지 않는다
            RunBuild reson = new RunBuild(10f, 8, 4, 6);
            reson.EquipMod(new ModSpec(ModType.CritResonance, 0.5f));
            BuildStats rs2 = reson.ToBuildStats();
            AssertBool("⭐ 치명 공명은 속성 크리를 열지 않는다 (다른 모드)",
                rs2.ElementalCritBonus == 0f && rs2.ElementalIncreased > 0f);
        }

        // ⚠⚠ 액션형이 스탯 집계에 새지 않는가 — ModType.Action은 BuildStats.Apply의 switch에 케이스가 없어
        //    자동으로 무시된다. 그 "자동"이 깨지면(누군가 케이스를 추가하면) 액션 모드가 수치로도 먹는다.
        {
            RunBuild b = Fresh();
            b.EquipMod(new ModSpec(ModActionKind.KillDamageAdjacent, 0.15f));
            b.EquipMod(new ModSpec(ModActionKind.KillPointBonus, 0.20f));
            AssertNear("⚠ 액션형은 스탯 집계에 영향 없음 (탄당 불변)",
                DamageCalculator.CalcPerShotBeforeCrit(b.ToBuildStats(), Base), Base);
            AssertNear("처치 포인트 배율 = 1 + 합산 (M08 수확)", b.KillPointMultiplier, 1.20f);
            AssertNear("액션 값 합계 — 같은 종류 누적", b.ActionValueSum(ModActionKind.KillDamageAdjacent), 0.15f);
            AssertNear("없는 액션 = 0", b.ActionValueSum(ModActionKind.KillCurrency), 0f);

            RunBuild chain = Fresh();
            chain.EquipMod(new ModSpec(ModActionKind.KillPointBonus, 0.15f));
            chain.EquipMod(new ModSpec(ModActionKind.KillPointBonus, 0.20f));
            AssertNear("사슬 Ⅰ+Ⅱ 동시 장착 = 합산 (1.35)", chain.KillPointMultiplier, 1.35f);
        }

        // ⚠⚠ 조건형 액션 — 2026-08-27 버그 회귀 방어.
        //    액션형은 ToBuildStats·ApplyTargetStatus를 안 거치므로 조건이 저절로 걸리지 않는다.
        //    실행 루프가 IsActionActive를 통과시키지 않아 4종(행렬 강타 Ⅰ·Ⅱ·단일 특화·전면 감염)이
        //    설명문의 "~이면"을 무시하고 무조건 발동하고 있었다. 그 술어 자체를 여기서 못 박는다.
        {
            RunBuild b = Fresh();
            var noBoard = BoardContext.None;
            var noTarget = TargetContext.None;

            // ① 조건 없는 액션 = 항상 발동
            var plain = new ModSpec(ModActionKind.KillDamageAdjacent, 0.15f);
            AssertBool("조건 없는 액션 = 항상 발동", b.IsActionActive(plain, noBoard, noTarget));

            // ② 표적 조건 — 같은 행 3기 이상이어야 발동 (M31 행렬 강타)
            var rowStrike = new ModSpec(ModType.Action, 0.5f, TriggerType.None, 0,
                                        ConditionType.TargetRowCountAtLeast, ElementType.None, 3,
                                        ElementType.None, ModActionKind.ShotRowDamage);
            var row2 = new TargetContext(0, 1f, 100f, 0f, 0f, 0, 2, false);
            var row3 = new TargetContext(0, 1f, 100f, 0f, 0f, 0, 3, false);
            AssertBool("표적 조건 미충족(행 2기) = 불발", !b.IsActionActive(rowStrike, noBoard, row2));
            AssertBool("표적 조건 충족(행 3기) = 발동", b.IsActionActive(rowStrike, noBoard, row3));
            AssertBool("조준 대상 없음 = 표적 조건 불발", !b.IsActionActive(rowStrike, noBoard, noTarget));

            // ③ 판 조건 — 전 표적 감전일 때만 (M19 전면 감염)
            var infect = new ModSpec(ModType.Action, 1f, TriggerType.None, 0,
                                     ConditionType.BoardAllShocked, ElementType.None, 0,
                                     ElementType.None, ModActionKind.ShotChainStatus);
            var shockedBoard = new BoardContext(1, 3, 4, 4, allShocked: true, lastTurnKills: 0);
            var plainBoard = new BoardContext(1, 3, 4, 4, allShocked: false, lastTurnKills: 0);
            AssertBool("판 조건 미충족(일부만 감전) = 불발", !b.IsActionActive(infect, plainBoard, noTarget));
            AssertBool("판 조건 충족(전원 감전) = 발동", b.IsActionActive(infect, shockedBoard, noTarget));
            AssertBool("전투 밖(판 스냅샷 없음) = 불발", !b.IsActionActive(infect, noBoard, noTarget));

            // ④ 덱 조건 — 장전이 전부 같은 속성일 때만 (M33 단일 특화). 빌드 상태를 본다
            var soloFocus = new ModSpec(ModType.Action, 0.3f, TriggerType.None, 0,
                                        ConditionType.UniformElement, ElementType.None, 0,
                                        ElementType.None, ModActionKind.ShotSpreadStatus);
            var fireAmmo = new AmmoSpec(6f, ElementType.Fire, 8f);
            var toxicAmmo = new AmmoSpec(4f, ElementType.Poison, 3f);
            RunBuild mixed = Fresh();
            mixed.Sequence.Set(0, new AmmoEntry(fireAmmo, 5, true), out _);
            mixed.Sequence.Set(1, new AmmoEntry(toxicAmmo, 5, true), out _);
            AssertBool("덱 조건 미충족(혼합 장전) = 불발", !mixed.IsActionActive(soloFocus, noBoard, noTarget));
            RunBuild mono = Fresh();
            for (int i = 0; i < mono.MagazineSize; i++)
                mono.Sequence.Set(i, new AmmoEntry(fireAmmo, 5, true), out _);
            AssertBool("덱 조건 충족(전탄 화염) = 발동", mono.IsActionActive(soloFocus, noBoard, noTarget));

            // ⑤ 탄별 조건은 액션에 배관이 없다 — 조용히 통과시키면 같은 버그가 재발한다
            var perShot = new ModSpec(ModType.Action, 1f, TriggerType.None, 0,
                                      ConditionType.PrevShotEmpty, ElementType.None, 0,
                                      ElementType.None, ModActionKind.ShotRowDamage);
            AssertBool("⚠ 탄별 조건 액션 = 미지원(불발로 막는다)", !b.IsActionActive(perShot, noBoard, noTarget));
        }

        // 처치 포인트 배율이 실제 적립에 반영되는가 (TargetGroup)
        {
            var group = new TargetGroup();
            group.Initialize(StageProgression.GetStage(2), runSeed: 3);
            int cell = group.FindWeakestAlive();
            float max = group[cell].MaxHealth;
            group[cell].ApplyDamage(max);
            group.RegisterKill(cell, 1.20f);
            AssertNear("M08 — 포인트가 배율만큼 더 들어간다", group.EarnedPoints, max * 1.20f);
        }

        // ── L0-d 판 상태 조건 (BoardContext) ──
        // ⭐ ToBuildStats가 턴 시작마다 다시 돌기 때문에 판 조건이 기존 L0 안에 들어간다.
        //   전투 밖(default)에서는 전부 불충족 — 상점 통계가 조건부 모드에 흔들리지 않아야 한다(§9.1 결정 1).
        {
            var turn1 = new BoardContext(1, 4, 6, 5, false, 0);   // 첫 턴 · 남은 4 · 정원 6
            var last  = new BoardContext(4, 1, 6, 2, false, 3);   // 막 턴 · 남은 1 · 직전 턴 3처치
            var shock = new BoardContext(2, 3, 6, 4, true, 0);    // 전원 감전

            (string name, ConditionType cond, BoardContext on, BoardContext off)[] board =
            {
                ("첫 턴 (M02 선제 화력)",      ConditionType.BoardFirstTurn,  turn1, last),
                ("마지막 턴 (M17 잔재 회수)",  ConditionType.BoardLastTurn,   last,  turn1),
                ("전원 감전 (M19 전면 감염)",  ConditionType.BoardAllShocked, shock, turn1),
            };
            bool ok = true;
            foreach ((string name, ConditionType cond, BoardContext on, BoardContext off) c in board)
            {
                RunBuild b = Fresh();
                b.EquipMod(new ModSpec(ModType.More, 1.0f, c.cond));
                float hit  = DamageCalculator.CalcPerShotBeforeCrit(b.ToBuildStats(c.on), Base);
                float miss = DamageCalculator.CalcPerShotBeforeCrit(b.ToBuildStats(c.off), Base);
                float none = DamageCalculator.CalcPerShotBeforeCrit(b.ToBuildStats(), Base); // 전투 밖
                if (!Near(hit, Base * 2f) || !Near(miss, Base) || !Near(none, Base))
                { ok = false; Console.WriteLine($"   ⚠ {c.name}: {hit:0.0}/{miss:0.0}/{none:0.0}"); }
            }
            AssertBool("판 상태 조건 3종 — 충족 시만 · 전투 밖에서는 전부 불충족", ok);

            // 누적형 — 자원 수만큼 반복 적용된다(승산 1.2를 n번 곱)
            RunBuild acc = Fresh();
            acc.EquipMod(new ModSpec(ModType.More, 0.2f, ConditionType.BoardPerTurnLeft));
            AssertNear("남은 턴 4개 = ×1.2^4 (M03 탄약 절약)",
                DamageCalculator.CalcPerShotBeforeCrit(acc.ToBuildStats(turn1), Base), Base * MathF.Pow(1.2f, 4));

            RunBuild cap = Fresh();
            cap.EquipMod(new ModSpec(ModType.More, 0.1f, ConditionType.BoardPerFieldCap));
            AssertNear("표적 정원 6기 = ×1.1^6 (M04 포위 사격)",
                DamageCalculator.CalcPerShotBeforeCrit(cap.ToBuildStats(turn1), Base), Base * MathF.Pow(1.1f, 6));

            RunBuild loot = Fresh();
            loot.EquipMod(new ModSpec(ModType.Increased, 0.3f, ConditionType.BoardPerLastTurnKill));
            AssertNear("직전 턴 3처치 = 증가 +90% (M07 전리품)",
                DamageCalculator.CalcPerShotBeforeCrit(loot.ToBuildStats(last), Base), Base * 1.9f);
            AssertNear("직전 턴 0처치 = 미적용",
                DamageCalculator.CalcPerShotBeforeCrit(loot.ToBuildStats(turn1), Base), Base);
        }

        // ── L0-c 빌드 자기참조 + 스탯 후처리 ──
        {
            // M01 경량 편성 — 빈 모드 슬롯 1칸당. 총기 6 + 탄창 2(크기 4 ÷ 2) = 8칸 중 1개 장착 → 7칸
            RunBuild slots = new RunBuild(10f, 4, 1, 6);
            slots.EquipMod(new ModSpec(ModType.More, 0.1f, ConditionType.PerEmptyModSlot));
            AssertNear("M01 — 빈 모드 슬롯 7칸 = ×1.1^7 (자기 자신도 슬롯을 먹는다)",
                DamageCalculator.CalcPerShotBeforeCrit(slots.ToBuildStats(), 10f), 10f * MathF.Pow(1.1f, 7));

            // M05 정예 탄약 — 특수탄 1발당. 4칸 중 2칸을 특수탄으로
            RunBuild spec = new RunBuild(10f, 4, 1, 6);
            var special = new AmmoEntry(new AmmoSpec(10f), 5, true);
            spec.Sequence.Set(0, special, out _);
            spec.Sequence.Set(2, special, out _);
            spec.EquipMod(new ModSpec(ModType.More, 0.1f, ConditionType.PerSpecialAmmo));
            AssertNear("M05 — 특수탄 2발 = ×1.1^2",
                DamageCalculator.CalcPerShotBeforeCrit(spec.ToBuildStats(), 10f), 10f * 1.21f);

            // M14 균형 배합 — 가장 적은 속성의 탄 1발당. 화염 3 · 독 1 → 최소 1
            RunBuild bal = new RunBuild(10f, 4, 1, 6);
            for (int i = 0; i < 3; i++)
                bal.Sequence.Set(i, new AmmoEntry(new AmmoSpec(6f, ElementType.Fire, 8f), 5, true), out _);
            bal.Sequence.Set(3, new AmmoEntry(new AmmoSpec(6f, ElementType.Poison, 8f), 5, true), out _);
            bal.EquipMod(new ModSpec(ModType.More, 0.5f, ConditionType.PerMinElementAmmo));
            AssertNear("M14 — 최소 속성(독 1발) = ×1.5 한 번",
                DamageCalculator.CalcPerShotBeforeCrit(bal.ToBuildStats(), 10f), 15f);

            RunBuild phys = new RunBuild(10f, 4, 1, 6); // 전부 기본탄(무속성) = 속성 0종
            phys.EquipMod(new ModSpec(ModType.More, 0.5f, ConditionType.PerMinElementAmmo));
            AssertNear("M14 — 속성탄이 없으면 미적용 (무속성은 세지 않는다)",
                DamageCalculator.CalcPerShotBeforeCrit(phys.ToBuildStats(), 10f), 10f);

            // M28 순수주의 — 물리 승산 ×max(1, 3 − 0.4×속성탄). 순정 물리(0발) = ×3.0
            RunBuild pure = new RunBuild(10f, 4, 1, 6);
            pure.EquipMod(new ModSpec(ModType.PurityPhysicalMore, 3.0f));
            AssertNear("M28 — 순정 물리 = ×3.0", DamageCalculator.CalcPerShotBeforeCrit(pure.ToBuildStats(), 10f), 30f);
            pure.Sequence.Set(0, new AmmoEntry(new AmmoSpec(6f, ElementType.Fire, 8f), 5, true), out _);
            AssertNear("M28 — 속성탄 1발 = ×2.6", DamageCalculator.CalcPerShotBeforeCrit(pure.ToBuildStats(), 10f), 26f);
            for (int i = 1; i < 4; i++)
                pure.Sequence.Set(i, new AmmoEntry(new AmmoSpec(6f, ElementType.Fire, 8f), 5, true), out _);
            AssertNear("M28 — 속성탄 4발이면 계수 1.4 (하한 1 미도달)",
                DamageCalculator.CalcPerShotBeforeCrit(pure.ToBuildStats(), 10f), 14f);

            // M39 치명 공명 — 크리배율의 절반만큼 속성 증가. 순서 무관이어야 한다
            RunBuild reso = new RunBuild(10f, 4, 1, 6);
            reso.EquipMod(new ModSpec(ModType.CritMultiplier, 2.0f)); // 기본 2.0 + 2.0 = 4.0
            reso.EquipMod(new ModSpec(ModType.CritResonance, 0.5f));
            BuildStats rs = reso.ToBuildStats();
            AssertNear("M39 — 크리배율 4.0의 절반 = 속성 증가 +200%", rs.ElementalIncreased, 2.0f);

            RunBuild resoRev = new RunBuild(10f, 4, 1, 6); // 장착 순서를 뒤집어도 같아야 한다
            resoRev.EquipMod(new ModSpec(ModType.CritResonance, 0.5f));
            resoRev.EquipMod(new ModSpec(ModType.CritMultiplier, 2.0f));
            AssertNear("⭐ M39 — 장착 순서와 무관 (후처리라 집계가 끝난 뒤 읽는다)",
                resoRev.ToBuildStats().ElementalIncreased, 2.0f);
        }

        // ── ⑦ 상태이상 규칙 완화 (M11 불씨 유지 · M12 잔류 전하) ──
        {
            RunBuild plain = Fresh();
            AssertNear("기본 화상 감쇠 = GameConfig", plain.BurnDecay, GameConfig.BurnDecay);
            AssertBool("기본은 감전이 턴 종료에 사라진다", !plain.ShockPersists);

            RunBuild ember = Fresh();
            ember.EquipMod(new ModSpec(ModType.BurnDecayUp, 0.7f));
            AssertNear("M11 — 감쇠 0.7로 대체", ember.BurnDecay, 0.7f);
            ember.EquipMod(new ModSpec(ModType.BurnDecayUp, 0.6f)); // 사슬 하위가 같이 있어도
            AssertNear("⭐ M11 — 가장 큰 값 하나만 (대체지 누적이 아니다)", ember.BurnDecay, 0.7f);

            RunBuild over = Fresh();
            over.EquipMod(new ModSpec(ModType.BurnDecayUp, 1.0f));
            AssertNear($"⚠ 감쇠 상한 {GameConfig.BurnDecayMax} — 1.0이면 독과 같은 O(t²)로 폭주한다",
                over.BurnDecay, GameConfig.BurnDecayMax);

            RunBuild charge = Fresh();
            charge.EquipMod(new ModSpec(ModType.ShockPersist, 0f));
            AssertBool("M12 — 감전 지속", charge.ShockPersists);

            // 감쇠가 실제 틱 곡선을 바꾸는가 — 정상상태는 A/(1−d)로 수렴한다
            var b05 = new BurnState();
            var b08 = new BurnState();
            float last05 = 0f, last08 = 0f;
            for (int turn = 0; turn < 40; turn++)
            {
                b05.Apply(10f); last05 = b05.TickAtTurnEnd(0.5f);
                b08.Apply(10f); last08 = b08.TickAtTurnEnd(0.8f);
            }
            AssertNear("감쇠 0.5 정상상태 = 10/(1−0.5) = 20", last05, 20f);
            AssertNear("감쇠 0.8 정상상태 = 10/(1−0.8) = 50", last08, 50f);

            // M27 화염 환전 — 남은 화상을 전량 소진해 즉발로
            var burn = new BurnState();
            burn.Apply(30f);
            AssertNear("M27 — Consume이 전량을 돌려준다", burn.Consume(), 30f);
            AssertBool("M27 — 소진 후 화상이 남지 않는다", !burn.IsActive && burn.ProjectedTick <= 0f);
        }

        // ── ② ModSpec 등급 필드 (M42) · 최빈 속성 (M43) · ⑩ 2패스 인에이블러 (M25) ──
        {
            // M42 정예 편성 — 특정 등급 모드 1개당. 희귀 2개 + 자기 자신(희귀) = 3개
            RunBuild elite = new RunBuild(10f, 12, 1, 6); // 탄창 슬롯 6칸 확보(크기÷2) — 한도에 걸리면 장착이 조용히 거부된다
            elite.EquipMod(new ModSpec(ModType.Flat, 0f, TriggerType.None, 0, ConditionType.None,
                                       ElementType.None, 0, ElementType.None, ModActionKind.None, Rarity.Rare));
            elite.EquipMod(new ModSpec(ModType.Flat, 0f, TriggerType.None, 0, ConditionType.None,
                                       ElementType.None, 0, ElementType.None, ModActionKind.None, Rarity.Rare));
            elite.EquipMod(new ModSpec(ModType.More, 0.1f, TriggerType.None, 0, ConditionType.PerModOfRarity,
                                       ElementType.None, (int)Rarity.Rare, ElementType.None, ModActionKind.None, Rarity.Rare));
            AssertNear("M42 — 희귀 모드 3개(자기 포함) = ×1.1^3",
                DamageCalculator.CalcPerShotBeforeCrit(elite.ToBuildStats(), 10f), 10f * MathF.Pow(1.1f, 3));

            // M43 편향 화력 — 모드 TargetElement 최빈값. 화염 2 · 독 1 → 화염
            RunBuild bias = new RunBuild(10f, 12, 1, 6);
            bias.EquipMod(new ModSpec(ModType.ElementalIncreased, 0f, ElementType.Fire));
            bias.EquipMod(new ModSpec(ModType.ElementalIncreased, 0f, ElementType.Fire));
            bias.EquipMod(new ModSpec(ModType.ElementalIncreased, 0f, ElementType.Poison));
            bias.EquipMod(new ModSpec(ModType.DominantElementIncreased, 0.28f));
            BuildStats bstats = bias.ToBuildStats();
            AssertNear("M43 — 최빈 속성(화염)에만 +28%", bstats.Fire.IncreasedPercent, 0.28f);
            AssertNear("M43 — 다른 속성은 안 받는다", bstats.Poison.IncreasedPercent, 0f);

            RunBuild tie = new RunBuild(10f, 12, 1, 6); // 화염 1 · 독 1 = 동수
            tie.EquipMod(new ModSpec(ModType.ElementalIncreased, 0f, ElementType.Fire));
            tie.EquipMod(new ModSpec(ModType.ElementalIncreased, 0f, ElementType.Poison));
            tie.EquipMod(new ModSpec(ModType.DominantElementIncreased, 0.28f));
            BuildStats tstats = tie.ToBuildStats();
            AssertBool("⚠ M43 — 동수면 미적용 (§13.8 확정)",
                Near(tstats.Fire.IncreasedPercent, 0f) && Near(tstats.Poison.IncreasedPercent, 0f));

            // M25 여백의 미 — 빈 칸을 건너뛰어 '직전 탄'을 잇는다. [화염][빈][화염] 배치
            var fire = new AmmoEntry(new AmmoSpec(6f, ElementType.Fire, 8f), 5, true);
            RunBuild gap = new RunBuild(10f, 4, 1, 6);
            gap.Sequence.Set(0, fire, out _);
            gap.Sequence.Set(1, AmmoEntry.CreateEmpty(), out _);
            gap.Sequence.Set(2, fire, out _);
            gap.EquipMod(new ModSpec(ModType.More, 1.0f, ConditionType.PrevSameElement));
            AssertBool("빈 칸이 끼면 '직전 같은 속성'이 끊긴다 (기본)", !gap.IsModTriggeredAt(2, 0));

            gap.EquipMod(new ModSpec(ModType.Flat, 0f, ConditionType.EnableSkipEmptyAsPrev));
            AssertBool("⭐ M25 — 인에이블러가 켜지면 빈 칸을 건너뛰어 이어진다", gap.IsModTriggeredAt(2, 0));
            AssertBool("M25 — 인에이블러 자신은 스탯을 주지 않는다 (판정 결과 없음)",
                Near(DamageCalculator.CalcPerShotBeforeCrit(gap.ToBuildStats(), 10f), 10f));
        }

        // ── ⑨ 패턴 합성 (M34 독성 관통) ──
        {
            RunBuild pen = Fresh();
            pen.EquipMod(new ModSpec(ModType.GrantPattern, (float)HitPattern.Penetrate));
            BuildStats ps = pen.ToBuildStats();
            AssertBool("M34 — 관통 패턴이 마스크에 실린다",
                (ps.GrantedPatternMask & (1 << (int)HitPattern.Penetrate)) != 0);
            AssertBool("부여하지 않은 패턴은 꺼져 있다",
                (ps.GrantedPatternMask & (1 << (int)HitPattern.Explode)) == 0);

            RunBuild both = Fresh();
            both.EquipMod(new ModSpec(ModType.GrantPattern, (float)HitPattern.Penetrate));
            both.EquipMod(new ModSpec(ModType.GrantPattern, (float)HitPattern.Explode));
            int mask = both.ToBuildStats().GrantedPatternMask;
            AssertBool("⭐ 합성 = 합집합 (관통 ∪ 폭발)",
                (mask & (1 << (int)HitPattern.Penetrate)) != 0 && (mask & (1 << (int)HitPattern.Explode)) != 0);
            AssertBool("패턴 부여는 스탯 수치를 건드리지 않는다",
                Near(DamageCalculator.CalcPerShotBeforeCrit(both.ToBuildStats(), Base), Base));

            // 연쇄 감쇠 — 3홉 누적이 실효 2.73기(§17.4)에 맞는가
            float sum = 1f, scale = 1f;
            for (int hop = 0; hop < GameConfig.ChainHops; hop++) { scale *= GameConfig.ChainHopDecay; sum += scale; }
            AssertBool($"연쇄 3홉 누적 감쇠 실효 = {sum:0.00}기 (§17.4 기준 2.73)", Math.Abs(sum - 2.73f) < 0.02f);
        }

        // ── 악세 5개 레인 확장 (§16.2) ──
        {
            RunBuild kit = new RunBuild(10f, 8, 4, 6); // 총기 6 · 탄창 8÷2 = 4
            AssertBool("기본 슬롯 (총기 6 · 탄창 4)", kit.SlotCount == 6 && kit.MagSlotCount == 4);
            kit.EquipAccessory(new AccessorySpec(AccessoryType.GunModSlot, 1));
            kit.EquipAccessory(new AccessorySpec(AccessoryType.MagModSlot, 1));
            AssertBool("⭐ 확장 키트 — 모드 슬롯 +1씩 (§16.5 '슬롯이 차면 재화가 남는다'의 답)",
                kit.SlotCount == 7 && kit.MagSlotCount == 5);
            AssertBool("늘어난 슬롯에 실제로 장착된다",
                kit.EquipMod(new ModSpec(ModType.Flat, 1f, TriggerType.FirstShot)) );

            // 경제 레인
            RunBuild eco = new RunBuild(10f, 8, 4, 6);
            eco.EquipAccessory(new AccessorySpec(AccessoryType.ShopDiscount, 25));
            AssertNear("단골 Ⅰ — 할인 25%", eco.ShopDiscountRate, 0.25f);
            AssertBool("할인가 = 8원 → 6원", ShopRules.GetPrice(8, 0.25f) == 6);
            AssertBool("⚠ 최소 1원은 받는다 (0원이면 사는 판단이 사라진다)", ShopRules.GetPrice(2, 0.9f) == 1);
            AssertBool("흥정 — 리롤비 감소, 0원 미만 없음",
                ShopRules.GetRerollCost(0, 1) == GameConfig.RerollCostStart - 1 && ShopRules.GetRerollCost(0, 99) == 0);

            // ⚠ 사슬 승계 불변량 — "Ⅰ을 사고 Ⅱ로 올린 총액 = Ⅱ를 직접 산 값".
            //   할인을 정가 차액에 걸면 이게 깨진다(Ⅰ 할인가 + 정가차액 ≠ Ⅱ 할인가).
            //   ShopManager.ResolvePrice가 할인가끼리 빼는 근거다. 최소 1원 보장 구간까지 훑는다.
            bool tierOk = true;
            foreach (float rate in new[] { 0f, 0.25f, 0.5f, 0.9f })
                for (int p1 = 1; p1 <= 12; p1++)
                    for (int p2 = p1; p2 <= 14; p2++)
                    {
                        int lower = ShopRules.GetPrice(p1, rate);
                        int upper = ShopRules.GetPrice(p2, rate);
                        int diff = upper - lower; if (diff < 0) diff = 0;
                        if (lower + diff != upper) tierOk = false;
                    }
            AssertBool("⚠ 사슬 승계 불변량 — 단계를 밟는 쪽이 손해 보지 않는다 (할인 0~90% 전 구간)", tierOk);

            var run = new RunState();
            int plain = run.AwardStageClear(3);
            var run2 = new RunState();
            int looted = run2.AwardStageClear(3, unusedTurnBonus: 1);
            AssertBool("노획 — 잔여 턴당 +1원이 실제로 붙는다 (3턴 = +3)", looted == plain + 3);

            // 증원 요청 — 체력은 그대로 두고 수만 늘어야 총 포인트가 는다
            StageSpec baseSpec = StageProgression.GetStage(5);
            StageSpec more = baseSpec.WithExtraTargets(1);
            AssertBool("증원 — 필드 상한·총 소환 +1", more.FieldCap == baseSpec.FieldCap + 1 && more.TotalSpawns == baseSpec.TotalSpawns + 1);
            AssertNear("⭐ 증원 — 표적당 체력은 유지된다", more.TargetHealth, baseSpec.TargetHealth);
            AssertBool("증원 — 총 포인트가 실제로 늘어난다",
                more.TotalSpawns * more.TargetHealth > baseSpec.TotalSpawns * baseSpec.TargetHealth);
        }

        // ⚠⚠ 전역 집계 오편입 검출 — IsTargetCondition에 등록을 빠뜨리면 조건 없이 상시 적용된다
        {
            bool leaked = false;
            foreach (ConditionType cond in Enum.GetValues(typeof(ConditionType)))
            {
                if (!cond.ToString().StartsWith("Target")) continue;
                if (!ModSpec.IsTargetCondition(cond)) { leaked = true; Console.WriteLine($"   ⚠ {cond} 미등록"); continue; }
                RunBuild b = Fresh();
                b.EquipMod(new ModSpec(ModType.More, 1.0f, cond));
                // 표적 조건은 ToBuildStats(덱 집계)에 절대 들어가면 안 된다
                if (!Near(DamageCalculator.CalcPerShotBeforeCrit(b.ToBuildStats(), Base), Base)) leaked = true;
            }
            AssertBool("⚠ Target* 조건이 전역 집계로 새지 않는다 (IsTargetCondition 누락 검출)", !leaked);
        }
    }

    /// <summary>마스크의 모든 점유 칸이 다른 점유 칸과 인접한가 (클러스터 검증).</summary>
    private static bool AllAdjacent(int mask)
    {
        for (int a = 0; a < BattleGrid.CellCount; a++)
        {
            if ((mask & (1 << a)) == 0) continue;
            bool found = false;
            for (int b = 0; b < BattleGrid.CellCount; b++)
            {
                if (a == b || (mask & (1 << b)) == 0) continue;
                if (BattleGrid.IsAdjacent(a, b)) { found = true; break; }
            }
            if (!found) return false;
        }
        return true;
    }

    private static int BitCount(int mask)
    {
        int n = 0;
        while (mask != 0) { n += mask & 1; mask >>= 1; }
        return n;
    }

    private static void FillStrong(RunBuild b)
    {
        int slots = b.SlotCount;
        b.EquipMod(new ModSpec(ModType.Flat, 5f));
        if (slots >= 2) b.EquipMod(new ModSpec(ModType.Increased, 1.0f));
        for (int i = 2; i < slots; i++) b.EquipMod(new ModSpec(ModType.More, 0.5f));
    }

    // 물량 악세 키트 — 탄창 수 +2, 크기 비잠금이면 +4 (리볼버는 실린더 고정이라 크기 악세 무효)
    private static void FillKit(RunBuild b)
    {
        b.EquipAccessory(new AccessorySpec(AccessoryType.MagazineCount, 2));
        b.EquipAccessory(new AccessorySpec(AccessoryType.MagazineSize, 4));
    }

    // 화상 DoT 포함 턴-워크 도달 — LabEngine.Walk 축약판(즉발+화상 틱). 기본탄 시퀀스면 MaxClearedStage와 동일.
    private static int MaxClearedStageBurnWalk(RunBuild build, int upTo)
    {
        BuildStats baseStats = build.ToBuildStats();
        int magSize = build.MagazineSize;
        int reached = 0;
        for (int stage = 1; stage <= upTo; stage++)
        {
            float required = StageProgression.GetStage(stage).RequiredDamage;
            var burn = new BurnState();
            float cum = 0f;
            bool cleared = false;
            for (int turn = 1; turn <= build.MagazineCount && !cleared; turn++)
            {
                for (int i = 0; i < magSize; i++)
                {
                    AmmoEntry entry = build.GetShotEntry(i);
                    if (entry.IsEmpty) continue;
                    BuildStats shot = build.GetShotStats(i, baseStats);
                    int pellets = DamageCalculator.EffectivePellets(shot.MultiShot, entry.Spec.MultiShot);
                    float phys = DamageCalculator.CalcPerShotBeforeCrit(shot, entry.Spec.PhysicalBase)
                                 * DamageCalculator.PhysicalCritExpected(shot);
                    float elemRaw = DamageCalculator.CalcElemental(shot, entry.Spec);   // 부여량 기준(크리 이전)
                    float elem = elemRaw * DamageCalculator.ElementalCritExpected(shot); // 즉발(원소 치명 반영)
                    cum += (phys + elem) * pellets;
                    if (entry.Spec.Element == ElementType.Fire && entry.Spec.HasElement)
                        burn.Apply(elemRaw * (1f + shot.StatusIntensity));
                }
                cum += burn.TickAtTurnEnd();
                if (cum >= required) cleared = true;
            }
            if (cleared) reached = stage; else break;
        }
        return reached;
    }

    private static int MaxClearedStage(in BuildStats stats, int upTo)
    {
        int reached = 0;
        for (int i = 1; i <= upTo; i++)
        {
            if (CombatResolver.ResolveExpected(stats, StageProgression.GetStage(i).RequiredDamage).Cleared) reached = i;
            else break;
        }
        return reached;
    }

    // 시드 셔플 + 보스 패시브 반영 — 단일표적 슬라이스 12완주 도달성 (곡선 완화 후보 탐색)
    // 곡선을 인자로 받아 같은 실행에서 여러 후보를 비교(GameConfig는 const라 런타임 변경 불가).
    private static void BossSeedBalanceScenario()
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(" 시드 셔플 12완주 도달성 — 보스 패시브 반영 (단일표적)");
        Console.WriteLine(" 소총 풀빌드(모드+물량악세) 천장으로 시드 200개 완주율 측정");
        Console.WriteLine(" ※ 강패시브(가중요구·물리경감)가 셔플로 후반 보스에 겹칠 때 완주 가능?");
        Console.WriteLine("--------------------------------------------------");

        const int seeds = 200;
        const int target = GameConfig.RunCompletionStage; // 12

        // 곡선 후보 (base · 스테이지당 증가폭). 보스 가중은 1로 폐지됐으므로 두 knob만 흔든다.
        //   제약① 스테1·2는 모드 0개로 통과 — 상점 1회에서 모드를 못 챙겨도 막히면 안 된다(데드엔드 금지).
        //         → base + step ≤ 352 (소총 빈 빌드 출력 = 모드 조합 최악값). base를 낮추고 step을 세우는 모양이 된다.
        //   제약② 스테6 돌파율 — 스테6 요구치 = base + 5×step.
        (float baseReq, float step)[] curves =
        {
            (GameConfig.StageBaseRequirement, GameConfig.StageLinearStep), // 현재
            (150f, 180f),
            (120f, 210f),
            (100f, 230f),
            (100f, 250f),
            (80f,  270f),
        };

        // 소총 풀빌드 천장 출력(보스 패시브 미반영) — 마진 기준값
        var rifleRef = new RunBuild(10f, 8, 4, 6); FillStrong(rifleRef); FillKit(rifleRef);
        float riflePower = rifleRef.GetResult().StageTotal;
        Console.WriteLine($"   소총 풀빌드 천장 출력 = {riflePower:0} ({rifleRef.GetResult().TotalShots}발)\n");

        // 스테6 요구치를 '소총 모드 N개' 환산으로 읽기 위한 사다리 (32발 기준, 하니스 실측)
        (string label, float output)[] ladder =
        {
            ("모드0(빈빌드)", 352f), ("모드1", 528f), ("모드2", 792f), ("모드3", 1188f), ("모드4", 1728f),
        };

        Console.WriteLine("   곡선(base+step)  | 스2요구 | 스2통과 | 스3요구 | 스3통과 | 스6요구 | 스6통과 | 스12최악 | 마진 | 완주율");
        foreach (var c in curves)
        {
            int clears = 0;
            var firstFailHist = new int[target + 1];
            for (int seed = 0; seed < seeds; seed++)
            {
                BossLibrary.SetRunSeed(seed);
                var rifle = new RunBuild(10f, 8, 4, 6);
                FillStrong(rifle); FillKit(rifle);
                int fail = FirstFailStageWithBoss(rifle, target, c.baseReq, c.step, GameConfig.BossRequirementMultiplier);
                if (fail == 0) clears++; else firstFailHist[fail]++;
            }

            float req2 = c.baseReq + c.step;
            float req3 = c.baseReq + c.step * 2f;
            float req6 = c.baseReq + c.step * 5f;
            float req12 = c.baseReq + c.step * (target - 1);
            float worst12 = req12 * 1.5f;     // HighDemand(풀 강도)가 스12에 겹친 최악
            float margin = riflePower / worst12;

            int worstStage = 0, worstCnt = 0;
            for (int s = 1; s <= target; s++) if (firstFailHist[s] > worstCnt) { worstCnt = firstFailHist[s]; worstStage = s; }
            string tag = c.baseReq == GameConfig.StageBaseRequirement && c.step == GameConfig.StageLinearStep ? "현재" : "";

            Console.WriteLine($"   {c.baseReq,3:0}+{c.step:0}/스테 {tag,-4}| {req2,6:0} | {NeedBuild(req2, ladder),-7} | {req3,6:0} | {NeedBuild(req3, ladder),-7}" +
                              $" | {req6,6:0} | {NeedBuild(req6, ladder),-7} | {worst12,7:0} | {margin,5:P0} | {(float)clears / seeds,5:P0}");
        }
        Console.WriteLine("   ※ 통과 = 그 요구치를 넘기는 데 필요한 최소 소총 빌드 (보스 패시브 미반영)");
        Console.WriteLine("   ※ 제약① 스2까지는 '모드0'이어야 데드엔드 없음 · 제약② 스6 = 80% 목표 → LabAuto로 실측");
        Console.WriteLine("   ※ 완주율·마진 = 소총 풀빌드 천장(5346) 기준 — 경제·빌드업 속도 미반영");
    }

    // 요구치를 넘기는 데 필요한 최소 소총 빌드 라벨 — 사다리 최상단으로도 모자라면 "그이상".
    private static string NeedBuild(float required, (string label, float output)[] ladder)
    {
        foreach (var l in ladder) if (l.output >= required) return l.label;
        return "그이상";
    }

    // 보스 패시브 반영 walk — 1부터 연속 클리어, 처음 막힌 스테 반환(0 = 완주). 곡선(선형 step) 파라미터화.
    private static int FirstFailStageWithBoss(RunBuild build, int upTo, float baseReq, float step, float bossMult)
    {
        BuildStats baseStats = build.ToBuildStats();
        int magSize = build.MagazineSize;
        for (int stage = 1; stage <= upTo; stage++)
        {
            BossSpec boss = BossLibrary.GetBoss(stage); // SetRunSeed 후 셔플 + 강도 램프 반영
            float required = baseReq + step * (stage - 1);
            if (StageProgression.IsBoss(stage)) required *= bossMult;
            required *= BossGimmickResolver.RequiredMultiplier(boss);

            var burn = new BurnState();
            float cum = 0f;
            bool cleared = false;
            for (int turn = 1; turn <= build.MagazineCount && !cleared; turn++)
            {
                for (int i = 0; i < magSize; i++)
                {
                    AmmoEntry entry = build.GetShotEntry(i);
                    if (entry.IsEmpty) continue;
                    BuildStats shot = build.GetShotStats(i, baseStats);
                    int pellets = DamageCalculator.EffectivePellets(shot.MultiShot, entry.Spec.MultiShot);
                    float phys = DamageCalculator.CalcPerShotBeforeCrit(shot, entry.Spec.PhysicalBase)
                                 * DamageCalculator.CritExpectedMultiplier(shot.CritChance, shot.CritMultiplier)
                                 * BossGimmickResolver.PhysicalFactor(boss);
                    float elemRaw = DamageCalculator.CalcElemental(shot, entry.Spec); // 보스 배율 전 — 부여량 기준값
                    float elem = elemRaw * BossGimmickResolver.ImmediateElementalFactor(boss, entry.Spec.Element);
                    cum += (phys + elem) * pellets;
                    if (entry.Spec.Element == ElementType.Fire && entry.Spec.HasElement)
                        burn.Apply(elemRaw * (1f + shot.StatusIntensity)
                                   * BossGimmickResolver.StatusFactor(boss, entry.Spec.Element));
                }
                cum += burn.TickAtTurnEnd();
                if (cum >= required) cleared = true;
            }
            if (!cleared) return stage;
        }
        return 0;
    }
}
