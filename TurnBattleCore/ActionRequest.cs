namespace NightView.Gameplay
{
    /// <summary>
    /// 입력 → 코어 진입점(ID 기반 — ⓐ). 소스(Local/AI/[확장]Remote) 무관 동일 형태라
    /// 직렬화·전송 가능. 코어는 이 요청만 보고 Resolve한다.
    /// </summary>
    public struct ActionRequest
    {
        public int ActorId;     // 행위자 InstanceId
        public ActionType Type; // BasicAttack / Skill / Pass
        public int TargetId;    // 대상 InstanceId (-1 = 없음/패스)

        public const int NO_TARGET = -1;

        public static ActionRequest BasicAttack(int actorId, int targetId)
            => new ActionRequest { ActorId = actorId, Type = ActionType.BasicAttack, TargetId = targetId };

        public static ActionRequest Skill(int actorId, int targetId)
            => new ActionRequest { ActorId = actorId, Type = ActionType.Skill, TargetId = targetId };

        public static ActionRequest Pass(int actorId)
            => new ActionRequest { ActorId = actorId, Type = ActionType.Pass, TargetId = NO_TARGET };
    }
}
