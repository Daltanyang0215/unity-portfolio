using System.Collections.Generic;

namespace NightView.Gameplay
{
    /// <summary>
    /// 한 행동(ActionRequest)을 해소해 BattleEvent 목록을 생성하는 코어 진입점. 순수 C#(ⓒ)·정적 클래스.
    /// 기본공격=CombatCalculator 위임(도발 강제 후) / 스킬=SkillData.Effects 순회(TargetResolver→EffectExecutor).
    /// 턴 시작 트리거(힐러 오라) → 행동 → 사망 수거 → 충원 → 상태 턴종료 차감 → 승패·교착 판정·턴 진행까지 일괄.
    /// 결과(승/패/무/진행)는 ctx.Outcome으로 노출(반환은 이벤트 목록 — ⓐ ID 기반·직렬화 가능).
    /// </summary>
    public static class CombatResolver
    {
        /// <summary>턴 시작 트리거(힐러 치유 오라 등) — 입력 전에 BattleManager가 호출. 적용된 이벤트 반환.</summary>
        public static List<BattleEvent> StartTurn(BattleContext ctx)
        {
            var log = new List<BattleEvent>();
            ProcessTurnStart(ctx.CurrentTurn, ctx, log);
            return log;
        }

        public static List<BattleEvent> Resolve(ActionRequest req, BattleContext ctx)
        {
            var log = new List<BattleEvent>();
            Side side = ctx.CurrentTurn;

            // (A) 행동 해소 (턴 시작 트리거는 StartTurn으로 분리 — 입력 전 BattleManager가 호출)
            var actor = Find(ctx, req.ActorId);
            switch (req.Type)
            {
                case ActionType.Pass:
                    log.Add(BattleEvent.Pass(req.ActorId));
                    break;

                case ActionType.BasicAttack:
                    if (actor != null)
                    {
                        var target = ResolveBasicTarget(actor, req.TargetId, ctx);
                        if (target != null) CombatCalculator.ResolveBasicAttack(actor, target, ctx, log);
                    }
                    break;

                case ActionType.Skill:
                    if (actor != null && actor.HasSkill) ResolveSkill(actor, req.TargetId, ctx, log);
                    break;
            }

            // (C) 사망 수거 → CardDied + 슬롯 비우기
            SweepBoard(ctx.Player, log);
            SweepBoard(ctx.Enemy, log);

            // (D) 충원 → CardSpawned
            RefillSide(ctx.Player, log);
            RefillSide(ctx.Enemy, log);

            // (E) 상태 턴 종료 처리 (TurnCount 증가 前 — 부여 턴 판정)
            StatusSystem.OnTurnEnd(side, ctx, log);

            // (F) 승패·교착 판정 + 턴 진행
            Judge(ctx);

            return log;
        }

        #region 행동 해소

        // 힐러 치유 오라 — 행동 진영 생존 카드 중 HealAura의 PassiveEffects를 TargetResolver+EffectExecutor로 재사용 실행
        private static void ProcessTurnStart(Side side, BattleContext ctx, List<BattleEvent> log)
        {
            var board = ctx.Of(side);
            for (int s = 0; s < BoardSide.SLOT_COUNT; s++)
            {
                var card = board.Slots[s];
                if (card == null || !card.IsAlive) continue;
                if (card.Spec.Passive != PassiveType.HealAura) continue;

                var effects = card.Spec.PassiveEffects;
                if (effects == null) continue;
                for (int e = 0; e < effects.Count; e++)
                {
                    var targets = TargetResolver.Resolve(effects[e], card, null, ctx);
                    EffectExecutor.Execute(effects[e], card, targets, ctx, log);
                }
            }
        }

        private static void ResolveSkill(CardInstance actor, int pickedId, BattleContext ctx, List<BattleEvent> log)
        {
            log.Add(BattleEvent.SkillCast(actor.InstanceId));
            var picked = pickedId == BattleEvent.NONE ? null : Find(ctx, pickedId);

            var effects = actor.Skill.Effects;
            for (int e = 0; e < effects.Count; e++)
            {
                var targets = TargetResolver.Resolve(effects[e], actor, picked, ctx);
                EffectExecutor.Execute(effects[e], actor, targets, ctx, log);
            }
        }

        // 도발 강제 — 생존 도발 적이 있으면 기본공격 대상을 도발로 강제(요청이 도발이면 유지). 스킬·즉사는 미적용.
        private static CardInstance ResolveBasicTarget(CardInstance attacker, int requestedId, BattleContext ctx)
        {
            var enemy = ctx.Opponent(attacker.Owner);
            var requested = Find(ctx, requestedId);

            CardInstance taunt = null;
            for (int s = 0; s < BoardSide.SLOT_COUNT; s++)
            {
                var c = enemy.Slots[s];
                if (c != null && c.IsAlive && c.HasPassive(PassiveType.Taunt)) { taunt = c; break; }
            }

            if (taunt != null)
            {
                // 요청 대상이 (생존) 도발이면 그대로, 아니면 첫 도발로 강제
                if (requested != null && requested.IsAlive && requested.HasPassive(PassiveType.Taunt)) return requested;
                return taunt;
            }

            if (requested != null && requested.IsAlive) return requested;

            // 방어적 폴백 — 요청 무효 시 첫 생존 적
            foreach (var c in enemy.AliveCards()) return c;
            return null;
        }

        #endregion

        #region 사망·충원·판정

        private static void SweepBoard(BoardSide board, List<BattleEvent> log)
        {
            for (int s = 0; s < BoardSide.SLOT_COUNT; s++)
            {
                var card = board.Slots[s];
                if (card == null || card.IsAlive) continue;
                log.Add(BattleEvent.CardDied(card.InstanceId));
                board.Slots[s] = null;
            }
        }

        private static void RefillSide(BoardSide board, List<BattleEvent> log)
        {
            var spawned = board.Refill();
            if (spawned == null) return;
            for (int i = 0; i < spawned.Count; i++)
                log.Add(BattleEvent.CardSpawned(spawned[i].InstanceId));
        }

        // 전멸 → 승/패/무. 미결 시 TurnCount++ → 상한 도달이면 총 HP 비교, 아니면 턴 교대.
        private static void Judge(BattleContext ctx)
        {
            bool playerDead = ctx.Player.IsDefeated;
            bool enemyDead = ctx.Enemy.IsDefeated;

            if (playerDead && enemyDead) { ctx.Outcome = BattleOutcome.Draw; return; }
            if (enemyDead) { ctx.Outcome = BattleOutcome.PlayerWin; return; }
            if (playerDead) { ctx.Outcome = BattleOutcome.EnemyWin; return; }

            ctx.TurnCount++;
            if (ctx.ReachedTurnLimit)
            {
                ctx.Outcome = JudgeByTotalHp(ctx);
                return;
            }

            ctx.CurrentTurn = ctx.CurrentTurn == Side.Player ? Side.Enemy : Side.Player;
            ctx.Outcome = BattleOutcome.Ongoing;
        }

        private static BattleOutcome JudgeByTotalHp(BattleContext ctx)
        {
            int p = ctx.Player.TotalHp;
            int e = ctx.Enemy.TotalHp;
            if (p > e) return BattleOutcome.PlayerWin;
            if (e > p) return BattleOutcome.EnemyWin;
            return BattleOutcome.Draw;
        }

        #endregion

        // InstanceId → CardInstance 역참조 (필드 우선, 드물게 대기열). 행위자·대상 해소용.
        private static CardInstance Find(BattleContext ctx, int instanceId)
        {
            if (instanceId == BattleEvent.NONE) return null;
            return FindIn(ctx.Player, instanceId) ?? FindIn(ctx.Enemy, instanceId);
        }

        private static CardInstance FindIn(BoardSide board, int instanceId)
        {
            for (int s = 0; s < BoardSide.SLOT_COUNT; s++)
            {
                if (board.Slots[s] != null && board.Slots[s].InstanceId == instanceId) return board.Slots[s];
            }
            foreach (var c in board.Bench)
                if (c.InstanceId == instanceId) return c;
            return null;
        }
    }
}
