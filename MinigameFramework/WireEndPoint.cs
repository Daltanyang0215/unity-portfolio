using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace NightView
{
    /// <summary>
    /// 전선 끝점 소켓. SideIndex 0 = 왼쪽, 1 = 오른쪽.
    /// 어느 쪽이든 미연결 상태이면 드래그 시작 가능 (양방향).
    /// </summary>
    [RequireComponent(typeof(CircleCollider2D))]
    public class WireEndPoint : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        #region SerializeField

        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField] private float _snapRadius = 0.5f;

        #endregion

        #region 프로퍼티

        public Color WireColor { get; private set; }
        public int ColorIndex { get; private set; }
        public int SideIndex { get; private set; }  // 0 = 왼쪽, 1 = 오른쪽
        public bool IsConnected { get; private set; }

        #endregion

        #region 이벤트

        public event Action<WireEndPoint, WireEndPoint> OnConnectionAttempt;
        public event Action<WireEndPoint> OnDragStarted;
        public event Action<Vector2> OnDragMoved;   // 스크린 좌표 전달

        #endregion

        #region private 필드

        private Camera _camera;
        private bool _isDragging;

        #endregion

        #region Public 메서드

        public void Init(Color color, int colorIndex, int sideIndex, Camera cam)
        {
            WireColor = color;
            ColorIndex = colorIndex;
            SideIndex = sideIndex;
            IsConnected = false;
            _isDragging = false;
            _camera = cam;

            if (_renderer != null)
            {
                _renderer.color = color;
                _renderer.flipX = sideIndex == 1; // 오른쪽 소켓 반전
            }
        }

        public void SetConnected(bool connected)
        {
            IsConnected = connected;
            if (_renderer == null) return;
            Color c = WireColor;
            _renderer.color = connected ? new Color(c.r, c.g, c.b, 0.4f) : c;
        }

        #endregion

        #region IPointer / IDrag 구현

        public void OnPointerDown(PointerEventData eventData)
        {
            if (IsConnected) return;

            _isDragging = true;
            OnDragStarted?.Invoke(this);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_isDragging) return;
            OnDragMoved?.Invoke(eventData.position);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_isDragging) return;
            _isDragging = false;

            Vector3 worldPos = _camera.ScreenToWorldPoint(
                new Vector3(eventData.position.x, eventData.position.y, 0f));
            worldPos.z = 0f;

            WireEndPoint target = null;
            foreach (Collider2D hit in Physics2D.OverlapCircleAll(worldPos, _snapRadius))
            {
                if (hit.TryGetComponent(out WireEndPoint ep) && ep != this && !ep.IsConnected && ep.ColorIndex == ColorIndex)
                {
                    target = ep;
                    break;
                }
            }
            OnConnectionAttempt?.Invoke(this, target);
        }

        #endregion
    }
}
