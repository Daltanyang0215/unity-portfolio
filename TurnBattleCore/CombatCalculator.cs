using System;
using System.Collections.Generic;
using NightView.Data;

namespace NightView.Gameplay
{
    /// <summary>스킬 프리뷰 항목 — GDD §6 호버 표기용(무변이). 무작위 대상은 미바인딩(TargetId=NONE).</summary>
    public struct SkillPreview
    {
        public int TargetId;       // 영향 대상 InstanceId (무작위 미확정 = NONE)
        public PreviewKind Kind;
        public int Value;          // 피해/힐량(히트당). Kill/Status/SelfDestruct = 0
        public int Count;          // 타격/대상 수 (EnemyRandom = Count, 그 외 1)
        public int RemainHp;       // 예상 잔여 HP (바인딩 대상만 유효, 무작위 = -1)
        public StatusType Status;  // Status 종류(ApplyStatus 시)

        public const int NONE = -1;
    }

    /// <summary>
    /// 기본공격 전용 계산기 + 데미지 프리뷰. 순수 C#(ⓒ)·정적 함수.
    /// 핵심 규칙(GDD §3.3·§4.4): 피해 = 공격자 현재 HP / 반격 = 대상 현재 HP, **공격 전 HP 기준 동시 적용**.
    ///  - 무반격(원거리): 반격 0  · 스플래시(무쌍): 인접 적 50%  · 무적: 받는 피해 0  · 기절 대상: 반격 0
    /// 대상 사망 판정·제거·충원은 CombatResolver 책임(여기선 HP 차감 + 이벤트 누적까지).
    /// 도발에 의한 대상 강제도 진입 전(Resolver) 단계 — 여기는 확정된 attacker/target만 받는다.
    /// </summary>
    public static class CombatCalculator
    {
        /// <summary>기본공격 해소 — HP 차감 + DamageDealt/CounterAttack 이벤트를 log에 누적.</summary>
        public static void ResolveBasicAttack(CardInstance attacker, CardInstance target, BattleContext ctx, List<BattleEvent> log)
        {
            int atkHp = attacker.CurrentHp;   // 스플래시 기준(반격으로 깎이기 전 원본 HP)
            var (damage, counter) = CalcExchange(attacker, target);

            // 1) 주 대상 피해
            ApplyDamage(attacker.InstanceId, target, damage, log);

            // 2) 스플래시(무쌍) — 인접 적 중 무작위 1장에게 50%(내림), 반격 없음. 인접 없으면 추가 피해 없음.
            if (attacker.HasPassive(PassiveType.Splash) && target.SlotIndex >= 0)
            {
                var board = ctx.Of(target.Owner);
                int slot = target.SlotIndex;
                var left = SplashAdjacent(board, slot - 1);
                var right = SplashAdjacent(board, slot + 1);

                CardInstance adj;
                if (left == null) adj = right;
                else if (right == null) adj = left;
                else adj = ctx.Rng.Next(2) == 0 ? left : right;   // 둘 다면 무작위 1장(ⓑ — 시드 재현)

                if (adj != null)
                {
                    int splashDmg = adj.IsInvincible ? 0 : atkHp / 2;
                    ApplyDamage(attacker.InstanceId, adj, splashDmg, log);
                }
            }

            // 3) 반격 — 공격자가 받음(대상 사망해도 발생)
            if (counter > 0)
            {
                attacker.CurrentHp = Math.Max(0, attacker.CurrentHp - counter);
                log.Add(BattleEvent.CounterAttack(target.InstanceId, attacker.InstanceId, counter));
            }
        }

        /// <summary>데미지 프리뷰(적용 없음) — GDD §6 호버: 대상 -X(잔여) / 내 반격 -Y(잔여). 주 대상 기준.</summary>
        public static (int targetRemainHp, int attackerRemainHp) PreviewBasicAttack(CardInstance attacker, CardInstance target)
        {
            var (damage, counter) = CalcExchange(attacker, target);
            int targetRemain = Math.Max(0, target.CurrentHp - damage);
            int attackerRemain = Math.Max(0, attacker.CurrentHp - counter);
            return (targetRemain, attackerRemain);
        }

        /// <summary>
        /// 스킬 프리뷰(적용·RNG 비소비) — GDD §6. 결정적 대상(Self/지정/전체/인접)만 수치 산출,
        /// 무작위(EnemyRandom/EnemyAdjRandom)는 볼리 정보만 미바인딩으로 표기. 광역은 영향 대상별 항목.
        /// </summary>
        public static List<SkillPreview> PreviewSkill(CardInstance caster, SkillData skill, CardInstance picked, BattleContext ctx)
        {
            var result = new List<SkillPreview>();
            if (skill == null) return result;

            var enemy = ctx.Opponent(caster.Owner);
            var ally = ctx.Of(caster.Owner);

            foreach (var eff in skill.Effects)
            {
                switch (eff.Target)
                {
                    case TargetType.Self:
                        AddEntry(result, caster, eff);
                        break;

                    case TargetType.EnemyTarget:
                    case TargetType.AllyTarget:
                        if (picked != null && picked.IsAlive) AddEntry(result, picked, eff);
                        break;

                    case TargetType.EnemyAll:
                        foreach (var c in enemy.AliveCards()) AddEntry(result, c, eff);
                        break;

                    case TargetType.AllyAllExceptSelf:
                        foreach (var c in ally.AliveCards())
                            if (c != caster) AddEntry(result, c, eff);
                        break;

                    case TargetType.EnemyRandom:
                    case TargetType.EnemyAdjRandom:
                        // 무작위 — 대상 미확정. 볼리 정보(히트당 수치·횟수)만 표기.
                        result.Add(new SkillPreview
                        {
                            TargetId = SkillPreview.NONE,
                            Kind = ToKind(eff.Effect),
                            Value = eff.Value,
                            Count = eff.Count,
                            RemainHp = SkillPreview.NONE,
                            Status = eff.Status,
                        });
                        break;
                }
            }
            return result;
        }

        private static void AddEntry(List<SkillPreview> result, CardInstance target, EffectData eff)
        {
            var p = new SkillPreview { TargetId = target.InstanceId, Count = eff.Count, Status = eff.Status };
            switch (eff.Effect)
            {
                case EffectType.Damage:
                    p.Kind = PreviewKind.Damage;
                    p.Value = target.IsInvincible ? 0 : eff.Value;
                    p.RemainHp = Math.Max(0, target.CurrentHp - p.Value);
                    break;
                case EffectType.Heal:
                    p.Kind = PreviewKind.Heal;
                    p.Value = Math.Max(0, Math.Min(eff.Value, target.MaxHp - target.CurrentHp));
                    p.RemainHp = target.CurrentHp + p.Value;
                    break;
                case EffectType.Kill:
                    p.Kind = PreviewKind.Kill;
                    p.RemainHp = 0;
                    break;
                case EffectType.ApplyStatus:
                    p.Kind = PreviewKind.Status;
                    p.RemainHp = target.CurrentHp;
                    break;
                case EffectType.SelfDestruct:
                    p.Kind = PreviewKind.SelfDestruct;
                    p.RemainHp = 0;
                    break;
            }
            result.Add(p);
        }

        private static PreviewKind ToKind(EffectType e) => e switch
        {
            EffectType.Heal => PreviewKind.Heal,
            EffectType.Kill => PreviewKind.Kill,
            EffectType.ApplyStatus => PreviewKind.Status,
            EffectType.SelfDestruct => PreviewKind.SelfDestruct,
            _ => PreviewKind.Damage,
        };

        /// <summary>
        /// 피해·반격 동시 교환 수치(공격 전 HP 기준). Resolve·Preview가 공유 →
        /// 규칙을 한 곳으로 일원화해 "프리뷰 수치 = 실제 결과"를 보장한다.
        /// </summary>
        private static (int damage, int counter) CalcExchange(CardInstance attacker, CardInstance target)
        {
            int damage = target.IsInvincible ? 0 : attacker.CurrentHp;
            int counter = (target.IsStunned || attacker.HasPassive(PassiveType.NoCounter) || attacker.IsInvincible)
                ? 0 : target.CurrentHp;
            return (damage, counter);
        }

        private static void ApplyDamage(int sourceId, CardInstance target, int damage, List<BattleEvent> log)
        {
            target.CurrentHp = Math.Max(0, target.CurrentHp - damage);
            log.Add(BattleEvent.DamageDealt(sourceId, target.InstanceId, damage));
        }

        // 인접 슬롯의 생존 적(스플래시 후보). 범위 밖·빈·사망이면 null.
        private static CardInstance SplashAdjacent(BoardSide board, int slot)
        {
            if (slot < 0 || slot >= BoardSide.SLOT_COUNT) return null;
            var c = board.Slots[slot];
            return (c != null && c.IsAlive) ? c : null;
        }
    }
}
