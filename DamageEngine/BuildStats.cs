namespace NightView.Gameplay
{
    /// <summary>
    /// 속성 1종의 집계 슬롯 — 물리 축과 동형(가산·증가·승산).
    /// 속성별 모드(화염 첨가제 등)가 여기 쌓이고, 속성 전체 모드는 BuildStats의 전역 필드에 쌓인다.
    /// </summary>
    public struct ElementalStats
    {
        public float Flat;             // 속성 가산 합 (+N)
        public float IncreasedPercent; // Σ속성 증가%
        public float MoreMultiplier;   // Π(1+속성 승산%). 기본 1

        /// <summary>보너스 없음(무속성 조회 등) — MoreMultiplier=1이라 곱해도 값이 변하지 않는다.</summary>
        public static ElementalStats Neutral => new ElementalStats { Flat = 0f, IncreasedPercent = 0f, MoreMultiplier = 1f };
    }

    /// <summary>
    /// 데미지 계산 입력값. 모드 집계 결과를 스칼라로 보관해 GC 없이 전달.
    /// 물리 파이프라인 기준 (DAMAGE_SYSTEM.md 층1). 속성/DoT는 M1에서 별도 필드 추가.
    /// </summary>
    public struct BuildStats
    {
        // ── 탄약 ──
        public float BaseDamage;       // 탄약 기본 데미지

        // ── 모드 집계 (물리) ──
        public float FlatAdded;        // 가산 합 (+N)
        public float IncreasedPercent; // Σ증가% (0.2 = 20%) — 다 더해 1회 곱
        public float MoreMultiplier;   // Π(1+승산%) — 각각 곱한 누적값. 기본 1

        // ── 모드 집계 (속성 전체 — 별도 파이프라인, Phase 2) ──
        public float ElementalIncreased;     // Σ속성 증가%(전 속성) — 속성 즉발+DoT 부여량 동반
        public float ElementalMoreMultiplier; // Π(1+속성 승산%)(전 속성). 기본 1

        // ── 모드 집계 (속성별 — 대상 속성이 일치하는 탄에만 적용) ──
        public ElementalStats Fire, Poison, Shock;

        // ── 모드 집계 (전체 — 물리·속성 공통) ──
        public float AllMoreMultiplier;  // Π(1+전체 승산%) — 물리·속성 모두 곱 (트리거 "탄 강화"). 기본 1

        // ── 상태이상 강도 (속성 부여량 전용 — DoT·감전 증폭만, 즉발 무관 §4.3) ──
        public float StatusIntensity;    // Σ상태이상 강도% — 화상·독 부여량·감전 증폭에 ×(1+값). 즉발 데미지엔 미적용

        // ── 멀티샷 (총 탄 수 축 — 펠릿 수, §2) ──
        public int MultiShot;            // 전역 펠릿 수(총기 base + 모드 가산). 기본 1. 탄환별 추가는 AmmoSpec.MultiShot

        /// <summary>
        /// 모드가 <b>더한</b> 타격 패턴 비트마스크 (bit = 1 &lt;&lt; (int)HitPattern) — M34 독성 관통 등.
        /// ⭐ 탄 자신의 패턴과 <b>합집합</b>으로 합성되고 중복 타격은 1회다(§10.7) —
        ///   모드 관통 + 탄 폭발이면 "같은 행 ∪ 인접"을 한 번씩만 때린다.
        /// </summary>
        public int GrantedPatternMask;

        // ── 크리 (독립 축) ──
        public float CritChance;       // 크리율 0~1 (100% 초과분은 CritOverflowRate가 없으면 버려진다)
        public float CritMultiplier;   // 크리배율(배수). 크리 없음 = 1
        /// <summary>크리율 100% 초과분 → 배율 전환 비율 (M-정밀 초과). 0 = 전환 없음. DamageCalculator.ResolveCrit이 소비.</summary>
        public float CritOverflowRate;
        /// <summary>속성 즉발에 걸리는 크리 배율분 (M-원소 치명). 0 = 속성에 크리 미적용(종전 동작).
        /// ⚠ 상태이상 부여량에는 안 걸린다 — CalcStatusSource는 크리 이전 값을 쓴다.</summary>
        public float ElementalCritBonus;

        // ── 층2: 총 탄 수 ──
        public int MagazineSize;       // 탄창 크기 (한 턴에 나가는 탄 수)
        public int MagazineCount;      // 탄창 수 (= 턴 = 발라트로 hands)

        /// <summary>총 탄 수 = 탄창 크기 × 탄창 수 × 펠릿 수 (DAMAGE_SYSTEM.md 층2 — 멀티샷 반영, 전역 펠릿 기준)</summary>
        public readonly int TotalShots => MagazineSize * MagazineCount * (MultiShot > 0 ? MultiShot : 1);

        /// <summary>승산/크리 기본값(1)이 세팅된 빈 빌드 생성. multiShot = 총기 base 펠릿(기본 1).</summary>
        public static BuildStats Create(float baseDamage, int magazineSize, int magazineCount, int multiShot = 1)
        {
            return new BuildStats
            {
                BaseDamage = baseDamage,
                FlatAdded = 0f,
                IncreasedPercent = 0f,
                MoreMultiplier = 1f,
                ElementalIncreased = 0f,
                ElementalMoreMultiplier = 1f,
                Fire = ElementalStats.Neutral,
                Poison = ElementalStats.Neutral,
                Shock = ElementalStats.Neutral,
                AllMoreMultiplier = 1f,
                StatusIntensity = 0f,
                MultiShot = multiShot > 0 ? multiShot : 1,
                GrantedPatternMask = 0,
                CritChance = GameConfig.BaseCritChance, // 기본 크리율(10%) — 모드로 가산
                CritMultiplier = GameConfig.BaseCritMultiplier, // 기본 배율(2.0) — 모드로 가감

                MagazineSize = magazineSize,
                MagazineCount = magazineCount,
            };
        }

        /// <summary>
        /// 모드 1개를 누적 적용. 승산은 각각 따로 곱해(*=) 지수 본질을 보존한다.
        /// (가산→Σ합산 / 증가→Σ합산 / 승산→Π곱 / 크리→가산)
        /// </summary>
        public void Apply(in ModSpec mod)
        {
            switch (mod.Type)
            {
                case ModType.Flat:               FlatAdded += mod.Value;             break;
                case ModType.Increased:          IncreasedPercent += mod.Value;      break;
                case ModType.More:               MoreMultiplier *= (1f + mod.Value); break;
                // 전체 증가 — 물리·속성 증가% 버킷 양쪽에 합산 (MoreAll의 가산 짝)
                case ModType.IncreasedAll:
                    IncreasedPercent += mod.Value;
                    ElementalIncreased += mod.Value;
                    break;
                case ModType.CritChance:         CritChance += mod.Value;            break;
                case ModType.CritMultiplier:     CritMultiplier += mod.Value;        break;
                case ModType.CritOverflow:       CritOverflowRate += mod.Value;      break;
                case ModType.ElementalCrit:      ElementalCritBonus += mod.Value;    break;
                case ModType.MoreAll:            AllMoreMultiplier *= (1f + mod.Value); break;
                case ModType.StatusIntensity:    StatusIntensity += mod.Value;       break;
                case ModType.MultiShot:          MultiShot += (int)mod.Value;        break; // 펠릿 가산(정수)
                // 패턴 부여 — Value에 HitPattern 인덱스를 담는다. 합성은 IsAffected가 합집합으로 처리
                case ModType.GrantPattern:       GrantedPatternMask |= 1 << (int)mod.Value; break;

                // 속성 축 — TargetElement None = 전 속성 / 지정 = 해당 속성 탄에만
                case ModType.ElementalIncreased:
                    if (mod.TargetElement == ElementType.None) ElementalIncreased += mod.Value;
                    else AddElementalIncreased(mod.TargetElement, mod.Value);
                    break;
                case ModType.ElementalMore:
                    if (mod.TargetElement == ElementType.None) ElementalMoreMultiplier *= (1f + mod.Value);
                    else AddElementalMore(mod.TargetElement, mod.Value);
                    break;
                case ModType.ElementalFlat:
                    AddElementalFlat(mod.TargetElement, mod.Value); // None = 대상 없음(전체 가산은 미지원) → 무효
                    break;
            }
        }

        // 속성별 누적 — struct 필드라 ref 반환이 불가(this 이탈)해 switch로 직접 대입한다. 할당 0.
        private void AddElementalFlat(ElementType element, float value)
        {
            switch (element)
            {
                case ElementType.Fire:   Fire.Flat += value;   break;
                case ElementType.Poison: Poison.Flat += value; break;
                case ElementType.Shock:  Shock.Flat += value;  break;
            }
        }

        private void AddElementalIncreased(ElementType element, float value)
        {
            switch (element)
            {
                case ElementType.Fire:   Fire.IncreasedPercent += value;   break;
                case ElementType.Poison: Poison.IncreasedPercent += value; break;
                case ElementType.Shock:  Shock.IncreasedPercent += value;  break;
            }
        }

        private void AddElementalMore(ElementType element, float value)
        {
            switch (element)
            {
                case ElementType.Fire:   Fire.MoreMultiplier *= (1f + value);   break;
                case ElementType.Poison: Poison.MoreMultiplier *= (1f + value); break;
                case ElementType.Shock:  Shock.MoreMultiplier *= (1f + value);  break;
            }
        }

        /// <summary>해당 속성의 집계 슬롯 (무속성 = 중립값). 탄별 속성 계산의 단일 진입점.</summary>
        public readonly ElementalStats ElementalFor(ElementType element) => element switch
        {
            ElementType.Fire => Fire,
            ElementType.Poison => Poison,
            ElementType.Shock => Shock,
            _ => ElementalStats.Neutral,
        };
    }
}
