namespace NightView.Gameplay
{
    /// <summary>
    /// 순수 모드 표현 (ScriptableObject 비의존).
    /// 런타임에 ModDataSO → ModSpec 으로 변환해 BuildStats 집계에 투입한다.
    /// 데미지 코어를 Unity 비의존으로 유지하기 위한 경량 구조체.
    /// </summary>
    public readonly struct ModSpec
    {
        public readonly ModType Type;
        public readonly float Value;
        public readonly TriggerType Trigger; // None = 상시 적용 (기존 모드 — DAMAGE_SYSTEM §5)
        public readonly int TriggerParam;    // NthShot의 N (1-기반)

        // 시퀀스 상태 조건 (트리거와 배타 — None=무조건). 트리거가 우선.
        public readonly ConditionType Condition;
        public readonly ElementType CondElement; // PrevShotElement·CountElementAtLeast 대상 속성
        public readonly int CondParam;           // CountElementAtLeast의 발 수(N) 등

        /// <summary>
        /// 속성 축 모드(ElementalFlat·ElementalIncreased·ElementalMore)의 대상 속성. None = 속성 전체.
        /// ⚠ CondElement(조건 판정용)와 의미가 다르다 — "독탄 3발이면 화염 증가" 같은 조합을 위해 필드를 분리했다.
        /// </summary>
        public readonly ElementType TargetElement;

        /// <summary>
        /// 액션형 동작 (Type == ModType.Action일 때만 유효). 스탯 파이프라인을 거치지 않고
        /// BattleController의 실행 훅이 직접 처리한다 (§10.2).
        /// </summary>
        public readonly ModActionKind Action;

        /// <summary>
        /// 이 모드의 등급 — <b>M42(정예 편성)가 "특정 등급 모드 1개당"을 세기 위해</b> 필요하다.
        /// ⚠ 2026-08-26 추가. 종전엔 등급이 ModDataSO에만 있어 순수 계층에서 볼 수 없었고,
        ///   그래서 "빌드 구성을 보는 모드"를 만들 수 없었다. 기본값 Common은 기존 생성자 호환용이다.
        /// </summary>
        public readonly Rarity Rarity;

        /// <summary>
        /// 계열 지정 — <c>Auto</c>면 아래 <see cref="IsGunMod"/> 파생 판정을 쓴다 (2026-08-27 · MOD_REVIEW §1).
        /// 시점도 조건도 없이 <b>판정 규칙만 바꾸는</b> 모드(불씨 유지·잔류 전하·원소 치명·이중 사격·정밀 초과)는
        /// 파생으로 구분할 수 없어 데이터가 직접 지정한다.
        /// </summary>
        public readonly ModFamily Family;

        public ModSpec(ModType type, float value)
            : this(type, value, TriggerType.None, 0, ConditionType.None, ElementType.None, 0) { }

        public ModSpec(ModType type, float value, TriggerType trigger, int triggerParam = 0)
            : this(type, value, trigger, triggerParam, ConditionType.None, ElementType.None, 0) { }

        public ModSpec(ModType type, float value, ConditionType condition, ElementType condElement = ElementType.None, int condParam = 0)
            : this(type, value, TriggerType.None, 0, condition, condElement, condParam) { }

        /// <summary>속성 축 모드 전용 — 대상 속성 지정(None = 속성 전체).</summary>
        public ModSpec(ModType type, float value, ElementType targetElement)
            : this(type, value, TriggerType.None, 0, ConditionType.None, ElementType.None, 0, targetElement) { }

        /// <summary>액션형 전용 — 동작(kind)과 크기(value). 조건을 붙이려면 아래 전체 생성자를 쓴다.</summary>
        public ModSpec(ModActionKind action, float value, int condParam = 0)
            : this(ModType.Action, value, TriggerType.None, 0, ConditionType.None, ElementType.None, condParam,
                   ElementType.None, action) { }

        public ModSpec(ModType type, float value, TriggerType trigger, int triggerParam,
                       ConditionType condition, ElementType condElement, int condParam,
                       ElementType targetElement = ElementType.None,
                       ModActionKind action = ModActionKind.None,
                       Rarity rarity = Rarity.Common,
                       ModFamily family = ModFamily.Auto)
        {
            Type = type;
            Value = value;
            Trigger = trigger;
            TriggerParam = triggerParam;
            Condition = condition;
            CondElement = condElement;
            CondParam = condParam;
            TargetElement = targetElement;
            Action = action;
            Rarity = rarity;
            Family = family;
        }

        /// <summary>액션형인가 — 스탯 집계가 아니라 실행 훅이 처리해야 하는 모드.</summary>
        public bool IsAction => Type == ModType.Action && Action != ModActionKind.None;

        /// <summary>탄별(발사 시점) 판정 조건 집합 — 전역 집계(ToBuildStats) 제외 + 총기 모드 분류의 단일 소스.
        /// 신규 탄별 조건 추가 시 여기만 갱신하면 집계·분류·라우팅이 함께 정합된다.</summary>
        public static bool IsPerShotCondition(ConditionType condition)
            => condition == ConditionType.PrevShotElement
            || condition == ConditionType.PrevSameElement
            || condition == ConditionType.PrevShotEmpty
            || condition == ConditionType.PrevDifferentElement
            || condition == ConditionType.ThisShotElement;

        /// <summary>조준 표적 런타임 상태 조건 — 발사 시점에만 평가(덱 집계·통계 UI 미개입, §B.2 경량).
        /// per-shot 조건과 달리 시퀀스가 아닌 표적 상태에 의존 → RunBuild.ApplyTargetStatus가 별도 처리. 총기 모드.
        /// ⚠ L2-b 조건을 새로 추가하면 <b>여기·RunBuild.EvaluateTargetCondition·ModConditionText</b> 셋을 함께 갱신할 것 —
        ///   여기서 누락되면 전역 집계(ToBuildStats)에 잘못 편입되어 조건 없이 상시 적용된다.</summary>
        public static bool IsTargetCondition(ConditionType condition)
            => condition == ConditionType.TargetHasStatus
            || condition == ConditionType.TargetHealthBelow
            || condition == ConditionType.TargetFullHealth
            || condition == ConditionType.TargetNoStatus
            || condition == ConditionType.TargetAllStatus
            || condition == ConditionType.TargetShotElementStatus
            || condition == ConditionType.TargetHasAdjacent
            || condition == ConditionType.TargetRowCountAtLeast
            || condition == ConditionType.TargetPoisonAtLeast
            || condition == ConditionType.TargetIsBoss
            || condition == ConditionType.TargetBurnOverHealth
            || condition == ConditionType.TargetAdjacentAtMost
            || condition == ConditionType.TargetFrontMost
            || condition == ConditionType.TargetRearMost;

        /// <summary>
        /// 판 상태 조건(L0-d) — 턴 시작 스냅샷을 본다. <b>총기 계열</b>이면서 전역 집계라
        /// "계열 = 판정 단계" 등식이 여기서 해제된다(MOD_AMMO_AUDIT §9.4-①).
        /// ⚠ 신규 Board 조건을 추가하면 여기에도 등록할 것 — 빠지면 탄창으로 잘못 분류된다.
        /// </summary>
        public static bool IsBoardCondition(ConditionType condition)
            => condition == ConditionType.BoardFirstTurn
            || condition == ConditionType.BoardLastTurn
            || condition == ConditionType.BoardPerTurnLeft
            || condition == ConditionType.BoardPerFieldCap
            || condition == ConditionType.BoardPerLastTurnKill
            || condition == ConditionType.BoardAllShocked;

        /// <summary>
        /// 총기 모드 여부 — <b>데이터 지정이 우선</b>, 없으면(Auto) 파생 판정 (2026-08-27 · MOD_REVIEW §1).
        ///
        /// 파생이 잡는 것 = ① 시점(트리거) · ② 탄 문맥 · ③ 표적 · ④ 판 상황 · ⑤-액션형 · 인에이블러.
        /// 파생이 못 잡는 것 = ⑤ 중 ModType으로만 구분되는 규칙 변경형 → <c>Family</c>로 지정한다.
        ///
        /// ⚠ 이 판정은 <b>슬롯 라우팅·상점 이분·도감 분류·드래그 검증</b>에만 쓰인다.
        ///   데미지 계산에는 한 곳도 쓰이지 않으므로 계열을 바꿔도 전투 수치는 흔들리지 않는다(확인 완료).
        /// </summary>
        public bool IsGunMod => Family switch
        {
            ModFamily.Gun => true,
            ModFamily.Mag => false,
            _ => Trigger != TriggerType.None
              || IsPerShotCondition(Condition)
              || IsTargetCondition(Condition)
              || IsBoardCondition(Condition)
              || Condition == ConditionType.EnableSkipEmptyAsPrev // 인에이블러 = 시퀀스 해석 규칙 변경
              || IsAction,                                        // 전투 이벤트에 직접 반응
        };
    }
}
