using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace NightView
{
    /// <summary>
    /// 패널 공통 프레임 UI. Phase B는 색상 플래시만 구현.
    /// 패널 외각에 시각 효과
    /// Phase F에서 파티클/애니메이션으로 교체 예정.
    /// </summary>
    public class PanelFrame : MonoBehaviour
    {
        #region SerializeField

        [SerializeField] private Image _progressGauge;          // fillAmount로 게이지 표현
        [SerializeField] private SpriteRenderer _frameBorder;   // 포커스 강조 + 성공/실패 색상

        [SerializeField] private Color _focusColor = Color.yellow;
        [SerializeField] private Color _defaultColor = Color.white;
        [SerializeField] private Color _successColor = Color.green;
        [SerializeField] private Color _failColor = Color.red;

        #endregion

        #region Public 메서드

        /// <summary>포커스 강조 색상 적용.</summary>
        public void SetFocusHighlight(bool focused)
        {
            if (_frameBorder == null) return;
            _frameBorder.color = focused ? _focusColor : _defaultColor;
        }

        /// <summary>게이지 갱신. normalized = 0~1.</summary>
        public void SetProgress(float normalized)
        {
            if (_progressGauge == null) return;
            _progressGauge.fillAmount = normalized;
        }

        /// <summary>성공 연출 — 초록색 0.5초.</summary>
        public async UniTask PlaySuccessOutroAsync(CancellationToken ct)
        {
            if (_frameBorder == null) return;
            _frameBorder.color = _successColor;
            await UniTask.Delay(500, cancellationToken: ct);
        }

        /// <summary>실패 연출 — 빨간색 0.5초.</summary>
        public async UniTask PlayFailOutroAsync(CancellationToken ct)
        {
            if (_frameBorder == null) return;
            _frameBorder.color = _failColor;
            await UniTask.Delay(500, cancellationToken: ct);
        }

        // 패널 퇴장 연출. 결과 연출 이후 호출됨.
        // Phase F에서 슬라이드/페이드 아웃 애니메이션으로 교체 예정.
        public async UniTask PlayExitAsync(CancellationToken ct)
        {
            if (_frameBorder != null)
                _frameBorder.color = _defaultColor;

            await UniTask.Delay(300, cancellationToken: ct);
        }

        #endregion
    }
}
