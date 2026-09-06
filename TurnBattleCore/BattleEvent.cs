namespace NightView.Gameplay
{
    /// <summary>
    /// 코어 → 뷰 단방향 이벤트(ID 기반 — ⓐ). 오브젝트 참조 없이 InstanceId로만 표현
    /// → 직렬화·전송·리플레이 가능. 뷰는 InstanceId → CardView 매핑으로 역참조해 연출을 순차 재생.
    /// 잔여 HP가 필요하면 뷰가 CardInstance.CurrentHp(현재)/MaxHp(최대)를 직접 참조한다.
    /// </summary>
    public struct BattleEvent
    {
        public BattleEventType Type;
        public int SourceId;      // 시전/공격자 InstanceId (없으면 NONE)
        public int TargetId;      // 대상 InstanceId (없으면 NONE)
        public int Value;         // 피해/힐량 등 주 수치
        public StatusType Status; // StatusApplied/Removed 시 유효

        public const int NONE = -1;

        // ── 팩토리 (세션 A 사용분) — 호출부 가독성·일관성 ──

        public static BattleEvent DamageDealt(int sourceId, int targetId, int amount)
            => new BattleEvent { Type = BattleEventType.DamageDealt, SourceId = sourceId, TargetId = targetId, Value = amount };

        public static BattleEvent CounterAttack(int sourceId, int targetId, int amount)
            => new BattleEvent { Type = BattleEventType.CounterAttack, SourceId = sourceId, TargetId = targetId, Value = amount };

        public static BattleEvent Healed(int sourceId, int targetId, int amount)
            => new BattleEvent { Type = BattleEventType.Healed, SourceId = sourceId, TargetId = targetId, Value = amount };

        public static BattleEvent StatusApplied(int sourceId, int targetId, StatusType status)
            => new BattleEvent { Type = BattleEventType.StatusApplied, SourceId = sourceId, TargetId = targetId, Status = status };

        public static BattleEvent StatusRemoved(int targetId, StatusType status)
            => new BattleEvent { Type = BattleEventType.StatusRemoved, SourceId = NONE, TargetId = targetId, Status = status };

        public static BattleEvent CardDied(int targetId)
            => new BattleEvent { Type = BattleEventType.CardDied, SourceId = NONE, TargetId = targetId };

        public static BattleEvent CardSpawned(int targetId)
            => new BattleEvent { Type = BattleEventType.CardSpawned, SourceId = NONE, TargetId = targetId };

        public static BattleEvent SkillCast(int sourceId)
            => new BattleEvent { Type = BattleEventType.SkillCast, SourceId = sourceId, TargetId = NONE };

        public static BattleEvent Pass(int sourceId)
            => new BattleEvent { Type = BattleEventType.Pass, SourceId = sourceId, TargetId = NONE };
    }
}
