using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NightView.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NightView.UI
{
    /// <summary>
    /// 플레이어 입력 소스(IActionSource) — 카드 탭 선택 → [공격]/[스킬] 드래그 타게팅 → 드롭 실행(GDD §6, 드래그 충실).
    /// 카드는 2D 월드 오브젝트라 카메라 레이캐스트로 선택/대상 판정. 행동은 UniTaskCompletionSource로 비동기 대기.
    /// 인계책임: 기절·비소유 카드는 선택 불가, 행동 가능 카드 없으면 자동 Pass. 상대 턴/해소 중에는 _active=false로 입력 잠금.
    /// 코어 진입 직전 BattleManager의 ActionValidator가 한 번 더 검증한다(이중 안전).
    /// </summary>
    public class LocalInputSource : MonoBehaviour, IActionSource
    {
        [Header("참조")]
        [SerializeField] private Camera _camera;
        [SerializeField] private BattleView _view;
        [SerializeField] private DamagePreviewUI _preview;
        [SerializeField] private BattleHUD _hud;

        [Header("선택/액션 바 (2D 월드)")]
        [SerializeField] private Transform _selectAnchor;   // 선택 시 카드가 이동할 '중앙 아래' 위치(고정)
        [SerializeField] private GameObject _actionBar;     // 선택 앵커 기준 좌우 버튼 묶음
        [SerializeField] private GameObject _skillButton;   // 스킬 없는 카드(T1)면 숨김

        private BattleContext _ctx;
        private Side _side;
        private UniTaskCompletionSource<ActionRequest> _tcs;
        private CancellationTokenRegistration _ctReg;

        private bool _active;
        private bool _targeting;
        private ActionKind _pendingKind;
        private CardView _selected;
        private readonly HashSet<int> _validIds = new();
        private readonly List<CardView> _highlighted = new();

        private void Awake()
        {
            if (_hud != null) _hud.OnPassPressed += OnPass;
            HideActionBar();
            if (_preview != null) _preview.Show(false);
        }

        private void OnDestroy()
        {
            if (_hud != null) _hud.OnPassPressed -= OnPass;
            _ctReg.Dispose();
        }

        #region IActionSource

        public UniTask<ActionRequest> GetActionAsync(BattleContext ctx, Side side, CancellationToken ct)
        {
            _ctx = ctx;
            _side = side;

            // 로컬로 캡처 — BeginInput에서 즉시 자동 Pass(Complete)되면 _tcs가 null이 되므로 반환은 로컬에서.
            var tcs = new UniTaskCompletionSource<ActionRequest>();
            _tcs = tcs;
            _ctReg = ct.Register(() => tcs.TrySetCanceled());

            BeginInput();
            return tcs.Task;
        }

        #endregion

        #region 입력 시작/종료

        private void BeginInput()
        {
            _active = true;
            _targeting = false;
            ClearSelection();
            if (_hud != null) _hud.SetPassInteractable(true);

            // 행동 가능 카드가 없으면(전부 기절 등) 자동 Pass — 소프트락 방지
            if (!HasSelectableCard()) CompletePass();
        }

        private void EndInput(bool keepSelectedCard = false)
        {
            _active = false;
            _targeting = false;
            ClearSelection(returnCard: !keepSelectedCard);
            if (_hud != null) _hud.SetPassInteractable(false);
            if (_preview != null) _preview.Hide();
            _ctReg.Dispose();
        }

        private void Complete(ActionRequest req)
        {
            if (!_active) return;
            // 기본공격과 스킬은 카드를 중앙에 둔 채 연출이 슬롯 복귀까지 담당 → 여기서 복귀 안 함
            EndInput(keepSelectedCard: req.Type == ActionType.BasicAttack || req.Type == ActionType.Skill);
            var tcs = _tcs;
            _tcs = null;
            tcs?.TrySetResult(req);
        }

        private void CompletePass() => Complete(ActionRequest.Pass(FirstOwnFieldId()));

        private void OnPass()
        {
            if (!_active) return;
            int actorId = _selected != null ? _selected.InstanceId : FirstOwnFieldId();
            Complete(ActionRequest.Pass(actorId));
        }

        #endregion

        #region 카드 선택 (월드 탭)

        private void Update()
        {
            if (!_active || _targeting) return;
            var pointer = Pointer.current;
            if (pointer == null || !pointer.press.wasReleasedThisFrame) return;

            var cv = RaycastCard(pointer.position.ReadValue(), out bool hitAny);
            if (!hitAny)
            {
                if (_selected != null) Deselect();   // 빈 곳 탭 → 취소·복귀 (#5)
                return;
            }
            if (cv != null && IsSelectableOwn(cv)) Select(cv);
        }

        private void Select(CardView cv)
        {
            if (_selected == cv) return;
            if (_selected != null) { _selected.SetSelected(false); _selected.PlayReturn(); }

            _selected = cv;
            cv.SetSelected(true);
            cv.PlaySelectMove(_selectAnchor != null ? _selectAnchor.position : cv.transform.position);

            if (_actionBar != null)
            {
                _actionBar.SetActive(true);
                if (_selectAnchor != null) _actionBar.transform.position = _selectAnchor.position;   // 고정 위치
            }
            if (_skillButton != null) _skillButton.SetActive(cv.Model.HasSkill);

            if (_preview != null) _preview.ShowSelected(cv.Model);
        }

        // 빈 곳 탭 → 선택 취소·카드 복귀(턴은 유지)
        private void Deselect()
        {
            if (_selected != null) { _selected.SetSelected(false); _selected.PlayReturn(); }
            _selected = null;
            HideActionBar();
            if (_preview != null) _preview.Show(false);
        }

        #endregion

        #region 액션 드래그 (ActionDragHandler → 호출)

        public void OnActionClick(ActionKind kind)
        {
            if (!_active || _selected == null) return;
            if (kind == ActionKind.Skill) TryInstantSkill();
        }

        public void OnActionDragBegin(ActionKind kind)
        {
            if (!_active || _selected == null) return;

            if (kind == ActionKind.Skill)
            {
                var skill = _selected.Model.Skill;
                if (skill == null) return;
                if (SkillMode(skill) == SkillTargetMode.SelfOnly)   // 자기 대상 스킬 — 즉시 시전
                {
                    Complete(ActionRequest.Skill(_selected.InstanceId, ActionRequest.NO_TARGET));
                    return;
                }
            }

            _pendingKind = kind;
            BeginTargeting();
        }

        public void OnActionDrag(Vector2 screenPos)
        {
            if (!_targeting) return;
            var cv = RaycastCard(screenPos, out _);
            if (cv != null && _validIds.Contains(cv.InstanceId)) UpdatePreview(cv);
            else if (_preview != null) _preview.ClearTarget();
        }

        public void OnActionDragEnd(Vector2 screenPos)
        {
            if (!_targeting) return;
            var cv = RaycastCard(screenPos, out _);
            if (cv != null && _validIds.Contains(cv.InstanceId))
            {
                var req = _pendingKind == ActionKind.Skill
                    ? ActionRequest.Skill(_selected.InstanceId, cv.InstanceId)
                    : ActionRequest.BasicAttack(_selected.InstanceId, cv.InstanceId);
                StopTargeting();
                Complete(req);
            }
            else
            {
                StopTargeting();
                if (_preview != null) _preview.ShowSelected(_selected.Model);   // 취소 — 선택 상태 복귀
            }
        }

        private void TryInstantSkill()
        {
            var skill = _selected.Model.Skill;
            if (skill != null && SkillMode(skill) == SkillTargetMode.SelfOnly)
                Complete(ActionRequest.Skill(_selected.InstanceId, ActionRequest.NO_TARGET));
        }

        #endregion

        #region 타게팅·프리뷰

        private void BeginTargeting()
        {
            _targeting = true;
            ComputeValidTargets();
            foreach (int id in _validIds)
            {
                var v = _view != null ? _view.GetView(id) : null;
                if (v != null) { v.SetTargetable(true); _highlighted.Add(v); }
            }
            if (_preview != null) _preview.ClearTarget();
        }

        private void StopTargeting()
        {
            _targeting = false;
            ClearHighlights();
        }

        private void UpdatePreview(CardView cv)
        {
            if (_preview == null) return;
            var actor = _selected.Model;
            var target = cv.Model;
            if (_pendingKind == ActionKind.Attack)
            {
                var (tRemain, aRemain) = CombatCalculator.PreviewBasicAttack(actor, target);
                _preview.ShowBasicPreview(actor, target, tRemain, aRemain);
            }
            else
            {
                var list = CombatCalculator.PreviewSkill(actor, actor.Skill, target, _ctx);
                _preview.ShowSkillPreview(actor, target, list);
            }
        }

        private void ComputeValidTargets()
        {
            _validIds.Clear();
            var actor = _selected.Model;

            if (_pendingKind == ActionKind.Attack)
            {
                var enemy = _ctx.Opponent(_side);
                bool hasTaunt = AnyTaunt(enemy);
                for (int i = 0; i < BoardSide.SLOT_COUNT; i++)
                {
                    var c = enemy.Slots[i];
                    if (c == null || !c.IsAlive) continue;
                    if (!hasTaunt || c.HasPassive(PassiveType.Taunt)) _validIds.Add(c.InstanceId);
                }
                return;
            }

            // 스킬 — 지정형=해당 진영 / 전체·무작위=적 전체에서 대표 선택 / 자기=타게팅 없음
            var skill = actor.Skill;
            switch (SkillMode(skill))
            {
                case SkillTargetMode.Pick:
                    var pick = FindPickType(skill);
                    if (pick == TargetType.EnemyTarget) AddAliveField(_ctx.Opponent(_side));
                    else if (pick == TargetType.AllyTarget) AddAliveField(_ctx.Of(_side));
                    break;
                case SkillTargetMode.EnemyArea:
                    AddAliveField(_ctx.Opponent(_side));   // 아무 적에 드롭 = 시전(대표=프리뷰)
                    break;
                case SkillTargetMode.AllyArea:
                    AddAliveField(_ctx.Of(_side));          // 아무 아군에 드롭 = 시전(대표=프리뷰)
                    break;
            }
        }

        /// <summary>스킬 타게팅 방식: 지정(적/아군 1) / 적 전역 / 아군 전역 / 자기(대상 불필요).</summary>
        private enum SkillTargetMode { Pick, EnemyArea, AllyArea, SelfOnly }

        private static SkillTargetMode SkillMode(NightView.Data.SkillData skill)
        {
            bool enemyArea = false, allyArea = false;
            var effs = skill.Effects;
            for (int i = 0; i < effs.Count; i++)
            {
                var t = effs[i].Target;
                if (t == TargetType.EnemyTarget || t == TargetType.AllyTarget) return SkillTargetMode.Pick;
                if (t == TargetType.EnemyAll || t == TargetType.EnemyRandom || t == TargetType.EnemyAdjRandom) enemyArea = true;
                else if (t == TargetType.AllyAllExceptSelf) allyArea = true;
            }
            if (enemyArea) return SkillTargetMode.EnemyArea;
            if (allyArea) return SkillTargetMode.AllyArea;
            return SkillTargetMode.SelfOnly;
        }

        private void AddAliveField(BoardSide board)
        {
            for (int i = 0; i < BoardSide.SLOT_COUNT; i++)
            {
                var c = board.Slots[i];
                if (c != null && c.IsAlive) _validIds.Add(c.InstanceId);
            }
        }

        private static bool AnyTaunt(BoardSide board)
        {
            for (int i = 0; i < BoardSide.SLOT_COUNT; i++)
            {
                var c = board.Slots[i];
                if (c != null && c.IsAlive && c.HasPassive(PassiveType.Taunt)) return true;
            }
            return false;
        }

        private static TargetType FindPickType(NightView.Data.SkillData skill)
        {
            var effs = skill.Effects;
            for (int i = 0; i < effs.Count; i++)
            {
                if (effs[i].Target == TargetType.EnemyTarget) return TargetType.EnemyTarget;
                if (effs[i].Target == TargetType.AllyTarget) return TargetType.AllyTarget;
            }
            return TargetType.Self;
        }

        #endregion

        #region 헬퍼

        // hitAny: 콜라이더에 맞았는지(빈 곳 판별용). 반환: 맞은 카드(없으면 null)
        private CardView RaycastCard(Vector2 screenPos, out bool hitAny)
        {
            hitAny = false;
            if (_camera == null) return null;
            var hit = Physics2D.GetRayIntersection(_camera.ScreenPointToRay(screenPos));
            if (hit.collider == null) return null;
            hitAny = true;
            return hit.collider.GetComponentInParent<CardView>();
        }

        private bool IsSelectableOwn(CardView cv)
        {
            var c = cv.Model;
            return c != null && c.Owner == _side && c.IsAlive && !c.IsStunned && c.SlotIndex >= 0;
        }

        private bool HasSelectableCard()
        {
            var board = _ctx.Of(_side);
            for (int i = 0; i < BoardSide.SLOT_COUNT; i++)
            {
                var c = board.Slots[i];
                if (c != null && c.IsAlive && !c.IsStunned) return true;
            }
            return false;
        }

        private int FirstOwnFieldId()
        {
            var board = _ctx.Of(_side);
            for (int i = 0; i < BoardSide.SLOT_COUNT; i++)
            {
                var c = board.Slots[i];
                if (c != null && c.IsAlive) return c.InstanceId;
            }
            return ActionRequest.NO_TARGET;
        }

        private void ClearSelection(bool returnCard = true)
        {
            if (_selected != null)
            {
                _selected.SetSelected(false);
                if (returnCard) _selected.PlayReturn();
            }
            _selected = null;
            ClearHighlights();
            HideActionBar();
        }

        private void ClearHighlights()
        {
            for (int i = 0; i < _highlighted.Count; i++)
                if (_highlighted[i] != null) _highlighted[i].SetTargetable(false);
            _highlighted.Clear();
            _validIds.Clear();
        }

        private void HideActionBar()
        {
            if (_actionBar != null) _actionBar.SetActive(false);
        }

        #endregion
    }
}
