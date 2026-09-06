using System.Collections.Generic;
using NightView.Data;

namespace NightView.Gameplay
{
    /// <summary>
    /// 효과 대상 키워드(GDD §4.6)를 실제 CardInstance 목록으로 해소. 순수 C#(ⓒ)·정적 클래스.
    /// 드래그 지정(EnemyTarget/AllyTarget)은 picked로 주입(플레이어=UI, AI=AIController).
    /// 무작위(EnemyRandom/EnemyAdjRandom)는 전부 ctx.Rng 경유 → 시드로 재현(ⓑ).
    /// 적/아군은 caster.Owner 기준(적=상대 진영, 아군=자기 진영). 슬롯 생존 카드만 대상(대기열 제외).
    /// </summary>
    public static class TargetResolver
    {
        public static List<CardInstance> Resolve(EffectData effect, CardInstance caster,
            CardInstance picked, BattleContext ctx)
        {
            var result = new List<CardInstance>();
            var enemy = ctx.Opponent(caster.Owner);
            var ally = ctx.Of(caster.Owner);

            switch (effect.Target)
            {
                case TargetType.Self:
                    result.Add(caster);
                    break;

                case TargetType.EnemyTarget:
                case TargetType.AllyTarget:
                    if (picked != null && picked.IsAlive) result.Add(picked);
                    break;

                case TargetType.EnemyAll:
                    foreach (var c in enemy.AliveCards()) result.Add(c);
                    break;

                case TargetType.AllyAllExceptSelf:
                    foreach (var c in ally.AliveCards())
                        if (c != caster) result.Add(c);
                    break;

                case TargetType.EnemyRandom:
                    ResolveRandom(enemy, effect.Count, ctx, result);
                    break;

                case TargetType.EnemyAdjRandom:
                    ResolveAdjRandom(enemy, ctx, result);
                    break;
            }
            return result;
        }

        /// <summary>
        /// 생존 적 중 count회 독립 무작위 추출(중복 허용 — "데미지 N ×Count" 다단히트).
        /// 스냅샷 방식: 시전 시점 생존 목록에서 매 히트 무작위 선택(같은 카드 중복 가능). 적 0이면 빈 목록.
        /// </summary>
        private static void ResolveRandom(BoardSide enemy, int count, BattleContext ctx, List<CardInstance> result)
        {
            var alive = new List<CardInstance>();
            foreach (var c in enemy.AliveCards()) alive.Add(c);
            if (alive.Count == 0) return;

            for (int i = 0; i < count; i++)
            {
                result.Add(alive[ctx.Rng.Next(alive.Count)]);
            }
        }

        /// <summary>무작위 적 1 + 좌우 인접(스플래시형 광역). 적 0이면 빈 목록.</summary>
        private static void ResolveAdjRandom(BoardSide enemy, BattleContext ctx, List<CardInstance> result)
        {
            var alive = new List<CardInstance>();
            foreach (var c in enemy.AliveCards()) alive.Add(c);
            if (alive.Count == 0) return;

            var main = alive[ctx.Rng.Next(alive.Count)];
            result.Add(main);
            foreach (var adj in enemy.GetAdjacent(main.SlotIndex)) result.Add(adj);
        }
    }
}
