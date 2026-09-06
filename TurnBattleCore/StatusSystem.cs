using System.Collections.Generic;

namespace NightView.Gameplay
{
    /// <summary>
    /// 기절·무적 상태의 부여·수명을 관리. 순수 C#(ⓒ)·정적 클래스.
    /// 차감 정책(확정): 상태 보유 카드의 '상대 진영' 턴이 종료될 때, 부여 턴(AppliedTurnNo)이 아니면
    /// RemainingTurns 1 감소 → 0이면 해제. 기절·무적 모두 중첩 금지(이미 보유 시 재부여 무시).
    /// → 적에게 건 기절은 시전자가 다음 행동을 마칠 때까지 유지(그동안 반격 0 = 안전 처치),
    ///   자신에게 건 상태는 상대 턴만 지나면 해제(내 다음 턴 시작 전).
    /// </summary>
    public static class StatusSystem
    {
        /// <summary>상태 부여 — 중첩 금지(이미 보유 시 갱신 없이 무시). 신규 부여 시에만 StatusApplied 이벤트.</summary>
        public static void Apply(CardInstance target, StatusType type, int duration,
            CardInstance caster, BattleContext ctx, List<BattleEvent> log)
        {
            // 기절·무적 모두 중첩 금지 — 이미 보유 중이면 지속 갱신 없이 무시
            if (target.HasStatus(type)) return;

            target.Statuses.Add(new StatusInstance(
                type, duration, caster.InstanceId, caster.Owner, ctx.TurnCount));
            log.Add(BattleEvent.StatusApplied(caster.InstanceId, target.InstanceId, type));
        }

        /// <summary>
        /// 행동 진영(endingSide) 턴 종료 처리 — 양 보드 전 카드의 상태 중
        /// 보유 카드의 진영이 endingSide가 '아니고'(=상대 턴 종료) 부여 턴이 아닌 것만 1 차감. 0이면 해제 + StatusRemoved.
        /// ⚠️ TurnCount 증가 前에 호출(현재 TurnCount = 종료되는 턴 번호 = 부여 턴 판정 기준).
        /// </summary>
        public static void OnTurnEnd(Side endingSide, BattleContext ctx, List<BattleEvent> log)
        {
            int endingTurnNo = ctx.TurnCount;
            DecrementBoard(ctx.Player, endingSide, endingTurnNo, log);
            DecrementBoard(ctx.Enemy, endingSide, endingTurnNo, log);
        }

        private static void DecrementBoard(BoardSide board, Side endingSide, int endingTurnNo, List<BattleEvent> log)
        {
            for (int s = 0; s < BoardSide.SLOT_COUNT; s++)
            {
                var card = board.Slots[s];
                if (card == null || !card.IsAlive) continue;

                var statuses = card.Statuses;
                for (int i = statuses.Count - 1; i >= 0; i--)
                {
                    var st = statuses[i];
                    if (card.Owner == endingSide) continue;        // 보유자의 '상대' 턴 종료에만 차감(자기 진영 턴엔 안 깎임)
                    if (st.AppliedTurnNo == endingTurnNo) continue; // 부여 턴 제외

                    st.RemainingTurns--;
                    if (st.RemainingTurns <= 0)
                    {
                        statuses.RemoveAt(i);
                        log.Add(BattleEvent.StatusRemoved(card.InstanceId, st.Type));
                    }
                }
            }
        }
    }
}
