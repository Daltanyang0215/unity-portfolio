namespace NightView.Gameplay
{
    /// <summary>
    /// 코어 진입 전 행동 검증 게이트 — BattleManager 책임(코어 밖). 순수 C·정적.
    /// 인계책임 ①② 적용: 액터가 행동 진영 소속·생존·비기절이 아니면 Pass로 강등하고,
    /// 스킬 요청인데 스킬이 없으면 역시 Pass. (대상 진영·도발·무효 대상 보정은 코어 TargetResolver/
    /// ResolveBasicTarget이 담당하므로 여기선 '액터' 적법성만 본다.)
    /// 코어(CombatResolver)는 기절 액터를 막지 않으므로 이 게이트가 반드시 선행되어야 한다.
    /// </summary>
    public static class ActionValidator
    {
        public static ActionRequest Sanitize(ActionRequest req, Side side, BattleContext ctx)
        {
            if (req.Type == ActionType.Pass) return req;

            var actor = FindOnField(ctx.Of(side), req.ActorId);

            // 무효·죽음·기절 액터 → 강제 Pass (인계책임 ①: 기절 카드 선택 차단)
            if (actor == null || !actor.IsAlive || actor.IsStunned)
                return ActionRequest.Pass(req.ActorId);

            // 스킬 없는 카드의 스킬 요청 → Pass
            if (req.Type == ActionType.Skill && !actor.HasSkill)
                return ActionRequest.Pass(req.ActorId);

            return req;
        }

        // 행동은 필드(슬롯) 카드만 가능 — 대기열 카드는 액터가 될 수 없다.
        private static CardInstance FindOnField(BoardSide board, int instanceId)
        {
            for (int s = 0; s < BoardSide.SLOT_COUNT; s++)
            {
                var c = board.Slots[s];
                if (c != null && c.InstanceId == instanceId) return c;
            }
            return null;
        }
    }
}
