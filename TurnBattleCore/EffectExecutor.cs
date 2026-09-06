using System;
using System.Collections.Generic;
using NightView.Data;

namespace NightView.Gameplay
{
    /// <summary>
    /// 효과 키워드(EffectType)를 해소된 대상에 즉시 적용. 순수 C#(ⓒ)·정적 클래스.
    /// 스킬 데미지는 상수(시전자 HP 무관)·반격 없음. 무적은 '피해'만 차단(처형·상태 부여는 관통).
    /// 사망 수거/충원/CardDied·CardSpawned 이벤트는 CombatResolver 책임(여기선 HP·상태 변경 + 직접 이벤트만).
    /// </summary>
    public static class EffectExecutor
    {
        public static void Execute(EffectData effect, CardInstance caster,
            IReadOnlyList<CardInstance> targets, BattleContext ctx, List<BattleEvent> log)
        {
            switch (effect.Effect)
            {
                case EffectType.Damage:
                    for (int i = 0; i < targets.Count; i++)
                        ApplyDamage(caster, targets[i], effect.Value, log);
                    break;

                case EffectType.Heal:
                    for (int i = 0; i < targets.Count; i++)
                        ApplyHeal(caster, targets[i], effect.Value, log);
                    break;

                case EffectType.Kill:
                    for (int i = 0; i < targets.Count; i++)
                        targets[i].CurrentHp = 0;   // 처형 — 무적 관통(피해 아님). CardDied는 Resolver 수거
                    break;

                case EffectType.ApplyStatus:
                    for (int i = 0; i < targets.Count; i++)
                        StatusSystem.Apply(targets[i], effect.Status, effect.Duration, caster, ctx, log);
                    break;

                case EffectType.SelfDestruct:
                    caster.CurrentHp = 0;   // 자폭 — 시전자 즉시 제거(수거는 Resolver)
                    break;
            }
        }

        // 상수 피해 — 무적이면 0(이벤트는 0으로 기록 → 뷰가 '막힘' 표기). 스킬은 반격 없음.
        private static void ApplyDamage(CardInstance caster, CardInstance target, int value, List<BattleEvent> log)
        {
            int damage = target.IsInvincible ? 0 : value;
            target.CurrentHp = Math.Max(0, target.CurrentHp - damage);
            log.Add(BattleEvent.DamageDealt(caster.InstanceId, target.InstanceId, damage));
        }

        // 회복 — MaxHp 상한. 회복량 0이면 이벤트 생략(턴 시작 오라 등 무의미 +0 방지).
        private static void ApplyHeal(CardInstance caster, CardInstance target, int value, List<BattleEvent> log)
        {
            int heal = Math.Min(value, target.MaxHp - target.CurrentHp);
            if (heal <= 0) return;
            target.CurrentHp += heal;
            log.Add(BattleEvent.Healed(caster.InstanceId, target.InstanceId, heal));
        }
    }
}
