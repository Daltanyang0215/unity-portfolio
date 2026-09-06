using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace NightView
{
    /// <summary>
    /// 모든 미니게임 구현체의 추상 베이스.
    /// 자체 Update() 금지 — WorkPanelManager가 Tick(dt) 호출.
    /// 입력은 각 미니게임 구현체·자식 오브젝트가 Collider2D + IPointer*/IDrag* 로 직접 수신.
    /// 베이스에서는 생성, 반환에 대한 처리
    /// </summary>
    public abstract class BaseMinigame : PoolingObject
    {
        #region 프로퍼티

        public MinigameDataSO SpecData { get; private set; }
        public int Stage { get; private set; }
        public bool IsActive { get; private set; }

        public int PanelIndex { get; private set; }

        #endregion

        #region 이벤트

        public event Action<BaseMinigame> OnSucceeded;
        public event Action<BaseMinigame> OnFailed;

        #endregion

        #region SerializeField

        [SerializeField] protected PanelFrame _frame;
        [SerializeField] protected TextMeshPro _goalText;
        [SerializeField] protected Transform _goalSlider;

        #endregion

        #region private 필드

        private CancellationTokenSource _cts;
        private CancellationTokenSource _endCts;

        #endregion

        #region Unity 이벤트

        protected virtual void OnDestroy()
        {
            _cts?.Dispose();
            _endCts?.Dispose();
        }

        #endregion

        #region Public 메서드

        /// <summary>외부에서 직접 퇴장 요청 (WorkPanelManager 등). 퇴장 연출 후 비활성화.</summary>
        public void Dismiss() => DismissInternalAsync(destroyCancellationToken).Forget();

        /// <summary>MinigameRegistry가 Instantiate 직후 호출.</summary>
        public void Initialize(MinigameDataSO data, int stage, int panelIndex)
        {
            SpecData = data;
            Stage = stage;
            PanelIndex = panelIndex;
        }

        /// <summary>WorkPanelManager가 패널 활성화 시 호출.</summary>
        public void Begin()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            _endCts?.Cancel();
            _endCts?.Dispose();
            _endCts = null;

            transform.DOKill();
            float offsetY = PanelIndex <= 2 ? +3f : -3f;
            Vector3 targetPos = transform.localPosition;
            transform.localPosition = new Vector3(targetPos.x, targetPos.y + offsetY, targetPos.z);
            transform.DOLocalMoveY(targetPos.y, 0.4f).SetEase(Ease.OutCubic).SetLink(gameObject);

            IsActive = true;
            OnBegin();
        }

        /// <summary>WorkPanelManager가 매 프레임 호출. IsPaused 시 skip은 호출자 책임.</summary>
        public void Tick(float dt)
        {
            if (!IsActive) return;
            OnTick(dt);
        }

        /// <summary>
        /// 라운드 종료 등 외부에서 강제 종료.
        /// silent=true: 이벤트/연출 없이 즉시 정리.
        /// silent=false: 성공/실패 async 흐름 실행.
        /// </summary>
        public void ForceEnd(bool success, bool silent = false)
        {
            if (!IsActive) return;

            if (silent)
            {
                IsActive = false;
                _cts?.Cancel();
                OnEnd(success);

                transform.DOLocalMoveY(PanelIndex <= 2 ? 3f : -3f, 0.4f).SetRelative(true).SetEase(Ease.InCubic)
                .OnComplete(() => ReturnToPool());
            }
            else
            {
                if (success) Succeed();
                else Fail();
            }
        }

        #endregion

        #region Protected 추상 메서드

        protected abstract void OnBegin();
        protected abstract void OnTick(float dt);
        protected abstract void OnEnd(bool success);

        #endregion

        #region Protected 메서드

        /// <summary>목표 슬라이더 위치·스케일 갱신. ratio 0~1.</summary>
        protected void UpdateGoalSlider(float ratio)
        {
            if (_goalSlider == null) return;
            if (ratio <= 0f)
            {
                _goalSlider.localPosition = new Vector3(-1.75f, 1.05f, 0);
                _goalSlider.localScale = new Vector3(0f, 0.1f, 1f);
            }
            else
            {
                _goalSlider.localPosition = new Vector3((1f - ratio) * -1.75f, 1.05f, 0);
                _goalSlider.localScale = new Vector3(ratio * 3.5f, 0.1f, 1f);
            }
        }

        /// <summary>자식 클래스가 성공 조건 충족 시 호출.</summary>
        protected void Succeed()
        {
            if (!IsActive) return;
            EndSequenceAsync(true).Forget();
        }

        /// <summary>자식 클래스가 실패 조건 충족 시 호출.</summary>
        protected void Fail()
        {
            if (!IsActive) return;
            EndSequenceAsync(false).Forget();
        }

        /// <summary>오답 등 실수 피드백용 짧은 흔들림. 게임 종료 없이 시각적 반응만.</summary>
        protected void ShakeOnMistake()
        {
            transform.DOKill(false);
            transform.DOPunchPosition(new Vector3(0.15f, 0f, 0f), 0.3f, 10, 0.5f);
        }

        #endregion

        #region Private 메서드

        private async UniTaskVoid EndSequenceAsync(bool success)
        {
            IsActive = false;
            _cts?.Cancel();
            OnEnd(success);

            _endCts?.Cancel();
            _endCts?.Dispose();
            _endCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            var ct = _endCts.Token;

            if (_frame != null)
            {
                if (success)
                    await _frame.PlaySuccessOutroAsync(ct);
                else
                    await _frame.PlayFailOutroAsync(ct);
            }

            await DismissInternalAsync(ct);

            await transform.DOLocalMoveY(PanelIndex <= 2 ? 3f : -3f, 0.4f)
                .SetRelative(true).SetEase(Ease.InCubic).ToUniTask(cancellationToken: ct);

            if (success) OnSucceeded?.Invoke(this);
            else OnFailed?.Invoke(this);
            ReturnToPool();
        }

        private async UniTask DismissInternalAsync(CancellationToken ct)
        {
            if (_frame != null)
                await _frame.PlayExitAsync(ct);
        }

        #endregion

        public override void ReturnToPool()
        {
            OnSucceeded = null;
            OnFailed = null;
            base.ReturnToPool();
        }
    }
}
