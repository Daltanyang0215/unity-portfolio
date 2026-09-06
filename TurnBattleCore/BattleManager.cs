using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NightView.AI;
using NightView.Core;
using NightView.Data;
using NightView.UI;
using NightView.Utils;
using UnityEngine;

namespace NightView.Gameplay
{
    /// <summary>
    /// 전투 루프·턴 진행 총괄(씬 내 단일·비글로벌). 코어(CombatResolver) 호출 → 뷰(BattleView) 재생 조율.
    /// Side별 IActionSource(Local/AI)에서 행동을 받아 ActionValidator로 검증 후 Resolve → PlayEvents 반복
    /// (입력 추상화 — ARCHITECTURE §3.4. 소스가 로컬/AI/[확장]원격이어도 루프 동일).
    ///
    /// 인계책임(코어 밖):
    ///  ① 기절·무효 액터는 Pass로 강등 — ActionValidator + AIController가 선행 차단
    ///  ② 액터 진영·적법성 검증 후 Resolve 전달 — ActionValidator.Sanitize
    ///  ③ 턴 시작 트리거(힐러 오라)는 CombatResolver.Resolve가 소유 — 여기서 별도 호출 금지(중복 방지)
    /// </summary>
    public class BattleManager : MonoBehaviour
    {
        private const int DECK_SIZE = 6;

        [Header("참조")]
        [SerializeField] private BattleView _view;
        [SerializeField] private BattleHUD _hud;
        [SerializeField] private ResultPanel _resultPanel;
        [SerializeField] private LocalInputSource _localInput;   // 플레이어 입력(3-B). 미할당이면 AI로 폴백

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Header("디버그/모드 (개발 빌드 전용)")]
        [Tooltip("GameManager 없이 Battle 씬 단독 실행 시 Start에서 자동 시작")]
        [SerializeField] private bool _autoBootstrap = true;
        [Tooltip("플레이어도 AI로 진행(오토배틀 검증). 릴리스 빌드에선 항상 플레이어 입력")]
        [SerializeField] private bool _autoBattlePlayer = false;
        [SerializeField] private bool _useFixedSeed = false;
        [SerializeField] private int _seed = 12345;
#endif

        private BattleContext _ctx;
        private AIController _ai;
        private IActionSource _playerSource;
        private IActionSource _enemySource;
        private bool _running;

        public BattleContext Context => _ctx;
        public event Action<BattleOutcome> OnBattleFinished;

        private void Start()
        {
            if (_resultPanel != null)
            {
                _resultPanel.OnRetry += Restart;
                _resultPanel.OnLobby += GoLobby;
            }

#if UNITY_EDITOR
            // 에디터상 단독 실행이면 스스로 부트스트랩한다.
            if (_autoBootstrap && GameManager.Instance == null)
            {
                if (!SpecDataManager.Instance.IsInitialized) SpecDataManager.Instance.Init();
                StartBattle().Forget();
            }
#endif
        }

        private void OnDestroy()
        {
            if (_resultPanel != null)
            {
                _resultPanel.OnRetry -= Restart;
                _resultPanel.OnLobby -= GoLobby;
            }
        }

        private void Restart() => StartBattle().Forget();   // 같은 씬에서 새 전투(다시 하기)

        private void GoLobby()
        {
            if (GameManager.Instance != null) GameManager.Instance.GoLobby().Forget();
            else Debug.LogWarning("[BattleManager] GameManager 없음 — 로비 전환 불가(Battle 단독 실행)");
        }

        /// <summary>덱 구성(DrawCards 6)·초기 배치·선공 결정(ctx.Rng) 후 동전 던지기 연출 → 턴 루프 시작.</summary>
        public async UniTaskVoid StartBattle()
        {
            int seed = Environment.TickCount;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_useFixedSeed) seed = _seed;
#endif
            var rng = new SeededRng(seed);
            Side first = rng.Next(2) == 0 ? Side.Player : Side.Enemy;   // 선공 결정(ⓑ — 시드 재현)
            _ctx = new BattleContext(rng, seed, first);

            if (_resultPanel != null) _resultPanel.Hide();   // 다시 하기 시 이전 결과 숨김

            BuildSide(_ctx.Player, Side.Player);
            BuildSide(_ctx.Enemy, Side.Enemy);

            if (_view != null) _view.Build(_ctx);   // 슬롯·카드 생성 및 초기 배치

            _ai = new AIController();
            _enemySource = new AIActionSource(_ai);

            // 플레이어 소스: LocalInputSource가 할당되면 플레이어 입력, 아니면 AI 폴백
            bool playerIsLocal = _localInput != null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_autoBattlePlayer) playerIsLocal = false;   // 오토배틀 검증 시 플레이어도 AI
#endif
            _playerSource = playerIsLocal ? (IActionSource)_localInput : new AIActionSource(_ai);

            Debug.Log($"[Battle] 시작 — seed={seed}, 선공={first}");
            if (_hud != null) await _hud.PlayCoinFlip(first, destroyCancellationToken);
            RunTurnLoop().Forget();
        }

        private void BuildSide(BoardSide board, Side side)
        {
            var specs = SpecDataManager.Instance.DrawCards(DECK_SIZE, _ctx.Rng);   // 양측 각각 풀에서 추출
            var cards = new List<CardInstance>(specs.Count);
            for (int i = 0; i < specs.Count; i++)
            {
                var skill = SpecDataManager.Instance.GetSkill(specs[i].SkillId);   // 키 → 스킬 해소(T1=null)
                cards.Add(new CardInstance(_ctx.NextInstanceId(), specs[i], skill, side));
            }
            board.Setup(cards);
        }

        /// <summary>source.GetActionAsync → 검증 → Resolve → PlayEvents → (승패/턴 교대는 Resolve 내부) 반복.</summary>
        public async UniTask RunTurnLoop()
        {
            if (_running) return;
            _running = true;
            CancellationToken ct = destroyCancellationToken;

            while (_ctx.Outcome == BattleOutcome.Ongoing && !ct.IsCancellationRequested)
            {
                Side side = _ctx.CurrentTurn;
                var source = side == Side.Player ? _playerSource : _enemySource;
                if (_hud != null) _hud.SetTurn(_ctx);

                await UniTask.Delay(1000);

                // 턴 시작 트리거(힐러 오라 등) — 입력(조작) 전에 먼저 재생
                var startEvents = CombatResolver.StartTurn(_ctx);
                if (_view != null && startEvents.Count > 0) await _view.PlayEvents(startEvents, _ctx);

                ActionRequest req = await source.GetActionAsync(_ctx, side, ct);
                req = ActionValidator.Sanitize(req, side, _ctx);   // 인계책임 ①②

                var events = CombatResolver.Resolve(req, _ctx);    // 인계책임 ③ — 턴시작 트리거는 Resolve 내부 소유

                if (_view != null) await _view.PlayEvents(events, _ctx);
            }

            _running = false;
            if (ct.IsCancellationRequested) return;

            Debug.Log($"[Battle] 종료 — {_ctx.Outcome} (turn {_ctx.TurnCount}, seed {_ctx.Seed})");
            if (_resultPanel != null) _resultPanel.Show(_ctx.Outcome);
            OnBattleFinished?.Invoke(_ctx.Outcome);
        }
    }
}
