namespace NightView.Gameplay
{
    /// <summary>
    /// 순수 탄약 표현 (ScriptableObject 비의존).
    /// 물리/속성 별도 파이프라인 (DAMAGE_SYSTEM.md 2·4장) — 소이탄처럼 둘 다 보유 가능.
    /// 런타임에 AmmoDataSO → AmmoSpec 으로 변환해 탄창 시퀀스(MagazineSequence)에 투입한다.
    /// </summary>
    public readonly struct AmmoSpec
    {
        public readonly float PhysicalBase;     // 물리 기본댐 (크리 적용 축)
        public readonly ElementType Element;    // 속성 종류 (None = 순수 물리탄)
        public readonly float ElementalBase;    // 속성 기본댐 (즉발 + DoT 부여 기준, 크리 미적용)
        /// <summary>타격 패턴 — 단일(조준 1개)/관통(같은 행)/폭발(인접 슬롯) (GDD §3.3). 기본 Single.</summary>
        public readonly HitPattern Pattern;
        /// <summary>탄환별 멀티샷 — 이 탄 1발이 쪼개지는 펠릿 수(산탄형 특수탄). 기본 1. 전역(BuildStats.MultiShot)과 합산.</summary>
        public readonly int MultiShot;
        /// <summary>
        /// 상태이상 부여 배율 — 이 탄이 <b>자기 속성</b>의 상태이상을 걸 때만 곱한다(즉발 데미지엔 미적용).
        /// "화염탄이 화상을 더 잘 건다" 같은 탄별 개성용. 기본 1(무변화).
        /// 부여량은 탄 자신의 속성 즉발값에서 파생되므로, 곱하는 위치상 '자기 속성에만' 적용이 구조적으로 보장된다.
        /// </summary>
        public readonly float StatusMultiplier;

        // ── 손잡이 신설 (2026-08-26 · §15.4) — 22종을 만드는 데 필요한 최소 확장 ──
        /// <summary>이 탄에만 얹히는 크리율 가산 (텅스텐탄). 0 = 없음.</summary>
        public readonly float CritChanceBonus;
        /// <summary>감전 증폭 천장 가산 (과부하탄) — 기본 ShockMaxMagnitude 위에 더한다. 0 = 없음.</summary>
        public readonly float ShockCapBonus;
        /// <summary>수치로 표현되지 않는 특수 동작 (기폭·역병·방전).</summary>
        public readonly AmmoGimmick Gimmick;

        public AmmoSpec(float physicalBase, ElementType element = ElementType.None, float elementalBase = 0f,
                        HitPattern pattern = HitPattern.Single, int multiShot = 1, float statusMultiplier = 1f,
                        float critChanceBonus = 0f, float shockCapBonus = 0f, AmmoGimmick gimmick = AmmoGimmick.None)
        {
            PhysicalBase = physicalBase;
            Element = element;
            ElementalBase = elementalBase;
            Pattern = pattern;
            MultiShot = multiShot > 0 ? multiShot : 1;
            StatusMultiplier = statusMultiplier > 0f ? statusMultiplier : 1f; // 미지정·불량값(0) = 기본 1 (MultiShot과 동일 방어)
            CritChanceBonus = critChanceBonus;
            ShockCapBonus = shockCapBonus;
            Gimmick = gimmick;
        }

        public readonly bool HasElement => Element != ElementType.None && ElementalBase > 0f;
    }
}
