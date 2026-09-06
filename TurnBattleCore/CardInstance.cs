using System.Collections.Generic;
using NightView.Data;

namespace NightView.Gameplay
{
    /// <summary>
    /// 런타임 카드 모델 — 정적 스펙(CardSpecData)을 참조하는 전투 중 가변 상태.
    /// 뷰(CardView)와 InstanceId로 1:1 매핑. 순수 C#(UnityEngine 비의존 — ⓒ).
    /// HP·상태 등 런타임 변경은 모두 이 클래스에서만. Spec/Skill POCO는 공유 참조라 직접 수정 금지.
    /// </summary>
    public class CardInstance
    {
        #region 불변 (셋업 시 확정)

        public int InstanceId { get; }      // 전투 시작 시 부여 — 양측 고유(같은 CardId 중복 대비)
        public CardSpecData Spec { get; }   // 정적 스펙 (읽기 전용 취급)
        public SkillData Skill { get; }     // Spec.SkillId 해소 결과 (무스킬 T1 = null)
        public Side Owner { get; }

        #endregion

        #region 가변 (전투 진행 중)

        public int CurrentHp;   // = 현재 공격력 (공격력 = 현재 HP)
        public int MaxHp;
        public int SlotIndex;   // 0~2 = 필드 슬롯, -1 = 대기열

        public readonly List<StatusInstance> Statuses = new();

        #endregion

        public CardInstance(int instanceId, CardSpecData spec, SkillData skill, Side owner)
        {
            InstanceId = instanceId;
            Spec = spec;
            Skill = skill;
            Owner = owner;
            MaxHp = spec.MaxHp;
            CurrentHp = spec.MaxHp;   // 시작 HP = 최대 HP
            SlotIndex = -1;
        }

        public bool IsAlive => CurrentHp > 0;
        public bool HasSkill => Skill != null;

        public bool HasPassive(PassiveType type) => Spec.Passive == type;

        public bool HasStatus(StatusType type) => GetStatus(type) != null;

        public StatusInstance GetStatus(StatusType type)
        {
            for (int i = 0; i < Statuses.Count; i++)
            {
                if (Statuses[i].Type == type) return Statuses[i];
            }
            return null;
        }

        public bool IsStunned => HasStatus(StatusType.Stun);
        public bool IsInvincible => HasStatus(StatusType.Invincible);
    }

    /// <summary>
    /// 카드에 부여된 상태 1개 — 기절·무적.
    /// 차감 정책: 보유 카드의 '상대 진영' 턴이 종료될 때, 부여 턴(AppliedTurnNo)이 아니면 RemainingTurns 감소.
    /// 0이 되면 해제 → 적에게 건 기절은 시전자 다음 행동까지 유지(반격 0·안전 처치), 자기 상태는 상대 턴 후 해제.
    /// </summary>
    public class StatusInstance
    {
        public StatusType Type;
        public int RemainingTurns;
        public int SourceId;      // 시전자 InstanceId — 이벤트 표기·확장용
        public Side SourceSide;   // 시전자 진영 — 이벤트 표기·참고용 (차감 게이트는 보유자 기준으로 변경됨)
        public int AppliedTurnNo; // 부여 시점 BattleContext.TurnCount — 부여 턴 차감 제외용

        public StatusInstance(StatusType type, int remainingTurns, int sourceId, Side sourceSide, int appliedTurnNo)
        {
            Type = type;
            RemainingTurns = remainingTurns;
            SourceId = sourceId;
            SourceSide = sourceSide;
            AppliedTurnNo = appliedTurnNo;
        }
    }
}
