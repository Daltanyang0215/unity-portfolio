using System;

namespace NightView.Gameplay
{
    /// <summary>
    /// 데미지 공식 엔진 (순수 로직, UnityEngine 비의존 → 콘솔/테스트 검증 가능).
    ///
    /// 층1 탄당_물리 = (기본 + 가산) × (1+Σ증가%) × Π(1+승산%) × 크리보정
    /// 층2 총 데미지 = 탄당 × 총 탄 수 × 다중표적 보정(단일 = 1)
    ///
    /// 순서 의존: 가산 합산 → 증가% 1회 곱 → 승산% 누적 곱 → 크리. (DAMAGE_SYSTEM.md 2장)
    /// </summary>
    public static class DamageCalculator
    {
        /// <summary>
        /// 크리 기댓값 보정 = 1 + min(크리율, 1) × (크리배율 - 1).
        ///
        /// ⚠ 2026-08-26 clamp 추가 (MOD_AMMO_AUDIT §13.8) — 실전투는 <c>rng &lt; CritChance</c> 확률 롤이라
        ///   <b>100% 초과분이 그대로 버려진다.</b> clamp가 없으면 통계·DamageLab·회귀 하니스가 함께 과대평가했다:
        ///   크리율 140%(기계식 조준기 +50% + 조준경 +90% — 실제 가능한 조합) · 배율 3.0에서
        ///   통계 3.80배 vs 실전투 3.00배로 <b>27% 과대</b>. 세 도구가 이 함수 하나를 공유해 함께 틀렸다.
        /// ✅ 2026-08-27 — 예고했던 "초과 크리율 → 크리 피해 전환"이 <c>ResolveCrit</c>으로 들어왔다.
        ///   이 함수는 <b>이미 해석된</b> 확률·배율을 받는 순수 계산으로 남는다(clamp는 방어용으로 유지).
        /// </summary>
        public static float CritExpectedMultiplier(float critChance, float critMultiplier)
        {
            if (critChance <= 0f) return 1f;
            if (critChance > 1f) critChance = 1f;
            return 1f + critChance * (critMultiplier - 1f);
        }

        /// <summary>
        /// 크리 최종 확률·배율 — <b>100% 초과분을 배율로 전환</b>한 뒤 확률을 1로 자른다 (M-정밀 초과, 2026-08-27).
        ///
        /// ⭐ 전환 모드가 없으면(<c>CritOverflowRate</c> 0) 동작이 종전과 같다 — 초과분은 그대로 버려진다.
        ///   그래서 이 함수 도입만으로는 <b>회귀가 없다</b>.
        /// ⭐ 통계·Lab·실전투가 모두 여기를 지나야 세 도구가 같은 값을 본다(08-24에 27% 과대가 났던 자리).
        /// ⚠ 크리율은 탄별로도 붙는다(순번 트리거·표적 조건·탄약 손잡이). 그래서 전환을 스탯 합산 단계가 아니라
        ///   <b>크리 해석 단계</b>에 둔다 — 합산 단계에 두면 발사 시점에 더해진 확률의 초과분을 놓친다.
        /// </summary>
        public static void ResolveCrit(in BuildStats s, out float chance, out float multiplier)
        {
            chance = s.CritChance;
            multiplier = s.CritMultiplier;
            if (chance <= 1f) return;
            if (s.CritOverflowRate > 0f) multiplier += (chance - 1f) * s.CritOverflowRate;
            chance = 1f;
        }

        /// <summary>물리에 걸리는 크리 기댓값 배율 — 초과 전환까지 반영한 최종값. 시뮬·Lab은 이걸 써야 실전투와 맞는다.</summary>
        public static float PhysicalCritExpected(in BuildStats s)
        {
            ResolveCrit(s, out float c, out float m);
            return CritExpectedMultiplier(c, m);
        }

        /// <summary>
        /// 속성 즉발에 걸리는 크리 기댓값 배율 — <b>원소 치명이 없으면 1</b>(종전 동작).
        /// ⭐ 확률은 물리와 공유하고 배율만 따로다. ⚠ 상태이상 부여량에는 쓰지 말 것(CalcStatusSource는 크리 이전 값).
        /// </summary>
        public static float ElementalCritExpected(in BuildStats s)
        {
            if (s.ElementalCritBonus <= 0f) return 1f;
            ResolveCrit(s, out float c, out float _);
            return 1f + c * s.ElementalCritBonus;
        }

        /// <summary>속성 즉발에 걸리는 크리 실연산 배율 — 물리와 <b>같은 롤</b>을 쓴다.</summary>
        public static float ElementalCritRolled(in BuildStats s, bool crit)
            => crit && s.ElementalCritBonus > 0f ? 1f + s.ElementalCritBonus : 1f;

        /// <summary>크리 적용 전 탄당 (가산 → 증가% → 승산% → 전체 승산까지). 통계·실연산 공통 베이스.</summary>
        public static float CalcPerShotBeforeCrit(in BuildStats s)
            => (s.BaseDamage + s.FlatAdded) * (1f + s.IncreasedPercent) * s.MoreMultiplier * s.AllMoreMultiplier;

        /// <summary>탄당 데미지 (크리 기댓값 반영) — 통계/검증용</summary>
        public static float CalcPerShot(in BuildStats s)
        {
            ResolveCrit(s, out float c, out float m);
            return CalcPerShotBeforeCrit(s) * CritExpectedMultiplier(c, m);
        }

        /// <summary>탄당 + 층2 총 데미지 + 단계별 중간값까지 계산</summary>
        public static DamageResult Calc(in BuildStats s, float multiTargetMult = 1f)
        {
            float afterFlat = s.BaseDamage + s.FlatAdded;
            float afterInc  = afterFlat * (1f + s.IncreasedPercent);
            float afterMore = afterInc * s.MoreMultiplier * s.AllMoreMultiplier;
            ResolveCrit(s, out float critChance, out float critMultiplier);
            float critMult  = CritExpectedMultiplier(critChance, critMultiplier);
            float perShot   = afterMore * critMult;

            return new DamageResult
            {
                AfterFlat      = afterFlat,
                AfterIncreased = afterInc,
                AfterMore      = afterMore,
                CritMult       = critMult,
                PerShot        = perShot,
                TotalShots     = s.TotalShots,
                StageTotal     = perShot * s.TotalShots * multiTargetMult,
            };
        }

        /// <summary>
        /// 전투용 실연산 탄당 (탄별 크리 롤). 통계는 기댓값, 실제 발사는 이 확률 롤로 분산을 만든다.
        /// crit으로 크리 발생 여부를 반환해 연출(색·크기)에 활용.
        /// </summary>
        public static float CalcPerShotRolled(in BuildStats s, Random rng, out bool crit)
        {
            ResolveCrit(s, out float c, out float m);
            crit = rng.NextDouble() < c;
            return CalcPerShotBeforeCrit(s) * (crit ? m : 1f);
        }

        // ── 시퀀스 덱 — 탄별 계산 (Phase 2) ──
        // 모드 집계(stats)는 탄창 공통, 기본댐(물리·속성)만 해당 탄 값으로 바꿔 계산한다.
        // 탄당 = 물리(크리 적용) + 속성 즉발(기본은 크리 미적용 — <b>원소 치명</b>이 있으면 걸린다, 2026-08-27).
        // DoT는 별도 누적(통계 미반영)이고 부여량 기준은 크리 <b>이전</b> 값이다(더블딥 방지).

        public static float CalcPerShotBeforeCrit(in BuildStats s, float ammoBaseDamage)
            => (ammoBaseDamage + s.FlatAdded) * (1f + s.IncreasedPercent) * s.MoreMultiplier * s.AllMoreMultiplier;

        /// <summary>
        /// 탄의 속성 최종 데미지 = (속성 기본댐 + 속성별 가산) × (1 + Σ속성 증가%[전체+속성별])
        ///                        × 속성 승산[전체] × 속성 승산[속성별] × 전체 승산.
        /// 즉발분이자 화상·독 부여량 기준 (§4.1) — 상태이상은 이 값에서 파생되므로 별도 처리가 필요 없다.
        /// HasElement(기본댐 &gt; 0 요구) 대신 속성 유무로 게이트해, 기본댐 0인 탄도 속성 가산만으로 발동할 수 있다.
        /// 가산이 없으면 결과가 종전과 동일(기본댐 0 → 0)이라 회귀 없음.
        /// </summary>
        public static float CalcElemental(in BuildStats s, in AmmoSpec ammo)
        {
            if (ammo.Element == ElementType.None) return 0f;
            ElementalStats es = s.ElementalFor(ammo.Element);
            float baseAmount = ammo.ElementalBase + es.Flat;
            if (baseAmount <= 0f) return 0f;
            return baseAmount
                 * (1f + s.ElementalIncreased + es.IncreasedPercent)
                 * s.ElementalMoreMultiplier * es.MoreMultiplier
                 * s.AllMoreMultiplier;
        }

        /// <summary>탄별 탄당 기댓값 = 물리(크리 기댓값) + 속성 즉발 — 시퀀스 턴당 합산·통계용.</summary>
        public static float CalcPerShot(in BuildStats s, in AmmoSpec ammo)
        {
            ResolveCrit(s, out float c, out float m);
            return CalcPerShotBeforeCrit(s, ammo.PhysicalBase) * CritExpectedMultiplier(c, m)
                 + CalcElemental(s, ammo) * ElementalCritExpected(s);
        }

        /// <summary>탄별 실연산 = 물리(크리 롤) + 속성 즉발 (전투 발사 루프 — BattleController).</summary>
        public static float CalcPerShotRolled(in BuildStats s, in AmmoSpec ammo, Random rng, out bool crit)
        {
            ResolveCrit(s, out float c, out float m);
            crit = rng.NextDouble() < c;
            return CalcPerShotBeforeCrit(s, ammo.PhysicalBase) * (crit ? m : 1f)
                 + CalcElemental(s, ammo) * ElementalCritRolled(s, crit);
        }

        /// <summary>탄별 실연산 — 물리/속성 즉발을 분리 반환 (팝업 색상 분리용). 감전 증폭은 호출자가 적용.</summary>
        public static void CalcPerShotRolled(in BuildStats s, in AmmoSpec ammo, Random rng, out bool crit, out float physical, out float elemental)
        {
            ResolveCrit(s, out float c, out float m);
            crit = rng.NextDouble() < c;
            physical = CalcPerShotBeforeCrit(s, ammo.PhysicalBase) * (crit ? m : 1f);
            elemental = CalcElemental(s, ammo) * ElementalCritRolled(s, crit);
        }

        // ── 상태이상 부여량 (§4.3) ──
        // 즉발 데미지와 분리된 축. 순수 계층에 두어 하니스로 검증 가능하게 한다(BattleController는 호출만).
        // 탄별 StatusMultiplier는 '탄 자신의 속성 즉발값'에 곱하므로 자기 속성에만 적용됨이 구조적으로 보장된다.

        /// <summary>상태이상 부여의 공통 기준 = 속성 즉발 × 탄별 배율. 강도%는 각 상태이상에서 별도 적용.</summary>
        public static float CalcStatusSource(in BuildStats s, in AmmoSpec ammo)
            => CalcElemental(s, ammo) * ammo.StatusMultiplier;

        /// <summary>화상 부여량 — 중첩 X(max)라 펠릿 수 무관, 1회분. 강도% 반영.</summary>
        public static float CalcBurnAmount(in BuildStats s, in AmmoSpec ammo)
            => CalcStatusSource(s, ammo) * (1f + s.StatusIntensity);

        /// <summary>독 부여량 — 중첩 O라 펠릿마다 스택(×펠릿). 강도% 반영.</summary>
        public static float CalcPoisonAmount(in BuildStats s, in AmmoSpec ammo, int pellets)
            => CalcBurnAmount(s, ammo) * (pellets > 0 ? pellets : 1);

        /// <summary>감전 강도 산정에 넣을 전기 즉발(펠릿 1발 기준).
        /// ⚠ 강도%를 곱하지 않는다 — ShockState.ApplyFromHit이 내부에서 적용하므로 여기서 곱하면 이중 적용된다.</summary>
        public static float CalcShockHit(in BuildStats s, in AmmoSpec ammo)
            => CalcStatusSource(s, ammo);

        /// <summary>이 탄의 유효 펠릿 수 = 전역(총기·모드) + 탄환별 추가. 전역·탄환 펠릿을 단일 가산축으로 합성 (§2).</summary>
        public static int EffectivePellets(int globalMultiShot, int ammoMultiShot)
        {
            int g = globalMultiShot > 0 ? globalMultiShot : 1;
            int a = ammoMultiShot > 0 ? ammoMultiShot : 1;
            return g + (a - 1);
        }

        // 다수 표적 적용(관통=행 / 폭발=인접)은 BattleController.FireOneShot이 영향 슬롯 집합으로 처리한다 (GDD §3.3).
        // DamageCalculator는 순수 수치 계산만 담당 (영향 범위는 진형 의존이라 전투 컨트롤러 소관).

        /// <summary>
        /// 시퀀스 턴당 합(turnTotal)으로 결과 구성 — PerShot = 평균 탄당(턴당 ÷ 탄창 크기, 빈 칸 포함).
        /// 균일 기본탄 시퀀스면 Calc와 동일 결과(회귀). 중간값(AfterFlat 등)은 기본탄 기준 — 디테일 표시용.
        /// </summary>
        public static DamageResult CalcFromTurnTotal(in BuildStats s, float turnTotal, float multiTargetMult = 1f)
        {
            float afterFlat = s.BaseDamage + s.FlatAdded;
            float afterInc  = afterFlat * (1f + s.IncreasedPercent);
            float afterMore = afterInc * s.MoreMultiplier * s.AllMoreMultiplier;
            ResolveCrit(s, out float critChance, out float critMultiplier);

            return new DamageResult
            {
                AfterFlat      = afterFlat,
                AfterIncreased = afterInc,
                AfterMore      = afterMore,
                CritMult       = CritExpectedMultiplier(critChance, critMultiplier),
                PerShot        = s.MagazineSize > 0 ? turnTotal / s.MagazineSize : 0f,
                TotalShots     = s.TotalShots,
                StageTotal     = turnTotal * s.MagazineCount * multiTargetMult,
            };
        }
    }
}
