using Cysharp.Text;
using UnityEngine;

namespace NightView
{
    /// <summary>
    /// 전선 연결하기 미니게임. BaseMinigame 상속.
    /// 자체 Update() 금지 — WorkPanelManager가 Tick(dt) 호출.
    /// </summary>
    public class WireConnectMinigame : BaseMinigame
    {
        #region 색상 팔레트

        private static readonly Color[] WireColors =
        {
            Color.red,
            Color.blue,
            Color.green,
            Color.yellow,
        };

        #endregion

        #region 상수

        private const int MAX_PAIRS = 4;

        #endregion

        #region SerializeField

        [Header("소켓 프리팹")]
        [SerializeField] private WireEndPoint _endPointPrefab;

        [Header("라인 프리팹")]
        [SerializeField] private LineRenderer _wireLinePrefab;

        [Header("소켓 레이아웃")]
        [SerializeField] private float _leftX = -1.5f;
        [SerializeField] private float _rightX = 1.5f;
        [SerializeField] private float _layoutHeight = 2.0f;

        [Header("테스트")]
        [SerializeField] private int _testStage = 0;

        #endregion

        #region private 필드

        private WireConnectStageData _config;

        // _allSockets: [0..count-1] 왼쪽, [count..2*count-1] 오른쪽
        private WireEndPoint[] _allSockets;
        private LineRenderer[] _connectedLines;
        private LineRenderer _dragLine;

        private WireEndPoint _draggingSource;
        private int _connectedCount;
        private int _activePairCount;
        private Camera _camera;
        private readonly int[] _rightOrder = new int[MAX_PAIRS];

        #endregion

        #region BaseMinigame 구현

        protected override void OnBegin()
        {
            _camera = Camera.main;
            _config = GetConfig();
            _connectedCount = 0;

            EnsurePreAllocated();
            SpawnSockets(_config.pairCount);
            UpdateGoalUI();
        }

        protected override void OnTick(float dt) { }

        protected override void OnEnd(bool success)
        {
            // 이벤트 해제만. 소켓·라인 비활성화는 ReturnToPool에서.
            if (_allSockets != null)
            {
                for (int i = 0; i < _activePairCount * 2; i++)
                {
                    _allSockets[i].OnDragStarted -= HandleDragStarted;
                    _allSockets[i].OnConnectionAttempt -= HandleConnectionAttempt;
                    _allSockets[i].OnDragMoved -= HandleDragMoved;
                }
            }

            if (_dragLine != null)
                _dragLine.gameObject.SetActive(false);
            _draggingSource = null;
        }

        public override void ReturnToPool()
        {
            if (_allSockets != null)
                for (int i = 0; i < _allSockets.Length; i++)
                    _allSockets[i].gameObject.SetActive(false);

            if (_connectedLines != null)
                for (int i = 0; i < _connectedLines.Length; i++)
                    _connectedLines[i].gameObject.SetActive(false);

            base.ReturnToPool();
        }

        #endregion

        #region Private 메서드

        private WireConnectStageData GetConfig()
        {
            int stage = _testStage > 0 ? _testStage : Stage;
            if (SpecData is WireConnectDataSO so)
                return so.GetStage(stage);
            return null;
        }

        /// <summary>최초 OnBegin 시 한 번만 실행. 이후 재사용.</summary>
        private void EnsurePreAllocated()
        {
            if (_allSockets != null) return;

            _allSockets = new WireEndPoint[MAX_PAIRS * 2];
            for (int i = 0; i < _allSockets.Length; i++)
            {
                _allSockets[i] = Instantiate(_endPointPrefab, transform);
                _allSockets[i].gameObject.SetActive(false);
            }

            _connectedLines = new LineRenderer[MAX_PAIRS];
            for (int i = 0; i < MAX_PAIRS; i++)
            {
                _connectedLines[i] = Instantiate(_wireLinePrefab, transform);
                _connectedLines[i].positionCount = 2;
                _connectedLines[i].gameObject.SetActive(false);
            }

            _dragLine = Instantiate(_wireLinePrefab, transform);
            _dragLine.positionCount = 2;
            _dragLine.gameObject.SetActive(false);
        }

        private void SpawnSockets(int count)
        {
            _activePairCount = count;

            // 오른쪽 Y 순서 셔플 (Fisher-Yates)
            for (int i = 0; i < count; i++) _rightOrder[i] = i;
            for (int i = count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (_rightOrder[i], _rightOrder[j]) = (_rightOrder[j], _rightOrder[i]);
            }

            for (int i = 0; i < count; i++)
            {
                WireEndPoint left = _allSockets[i];
                WireEndPoint right = _allSockets[count + i];

                left.transform.localPosition = new Vector3(_leftX, GetSocketY(i, count), 0f);
                right.transform.localPosition = new Vector3(_rightX, GetSocketY(_rightOrder[i], count), 0f);

                left.Init(WireColors[i], i, 0, _camera);
                right.Init(WireColors[i], i, 1, _camera);

                left.gameObject.SetActive(true);
                right.gameObject.SetActive(true);

                left.OnDragStarted -= HandleDragStarted;
                left.OnConnectionAttempt -= HandleConnectionAttempt;
                left.OnDragMoved -= HandleDragMoved;
                left.OnDragStarted += HandleDragStarted;
                left.OnConnectionAttempt += HandleConnectionAttempt;
                left.OnDragMoved += HandleDragMoved;

                right.OnDragStarted -= HandleDragStarted;
                right.OnConnectionAttempt -= HandleConnectionAttempt;
                right.OnDragMoved -= HandleDragMoved;
                right.OnDragStarted += HandleDragStarted;
                right.OnConnectionAttempt += HandleConnectionAttempt;
                right.OnDragMoved += HandleDragMoved;
            }

            // 미사용 소켓 비활성
            for (int i = count * 2; i < _allSockets.Length; i++)
                _allSockets[i].gameObject.SetActive(false);
        }

        private float GetSocketY(int index, int count)
        {
            if (count <= 1) return 0f;
            return _layoutHeight * 0.5f - _layoutHeight * index / (count - 1) - 0.35f;
        }

        private void HandleDragStarted(WireEndPoint source)
        {
            if (!IsActive) return;

            _draggingSource = source;
            _dragLine.startColor = source.WireColor;
            _dragLine.endColor = source.WireColor;
            _dragLine.SetPosition(0, source.transform.localPosition);
            _dragLine.SetPosition(1, source.transform.localPosition);
            _dragLine.gameObject.SetActive(true);
        }

        private void HandleDragMoved(Vector2 screenPos)
        {
            if (!IsActive || _draggingSource == null) return;

            Vector3 world = _camera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
            world.z = 0f;
            world = Transform.InverseTransformPoint(world);
            _dragLine.SetPosition(0, _draggingSource.transform.localPosition);
            _dragLine.SetPosition(1, world);
        }

        private void HandleConnectionAttempt(WireEndPoint source, WireEndPoint target)
        {
            if (!IsActive) return;

            _dragLine.gameObject.SetActive(false);
            _draggingSource = null;

            if (target == null ||
                source.SideIndex == target.SideIndex ||
                source.IsConnected || target.IsConnected ||
                source.ColorIndex != target.ColorIndex)
            {
                ShakeOnMistake();
                return;
            }

            source.SetConnected(true);
            target.SetConnected(true);

            // _connectedCount를 인덱스로 직접 사용 (null 탐색 불필요)
            LineRenderer line = _connectedLines[_connectedCount];
            line.startColor = source.WireColor;
            line.endColor = source.WireColor;
            line.SetPosition(0, source.transform.localPosition);
            line.SetPosition(1, target.transform.localPosition);
            line.gameObject.SetActive(true);

            _connectedCount++;
            UpdateGoalUI();

            if (_connectedCount >= _config.pairCount)
                Succeed();
        }

        private void UpdateGoalUI()
        {
            _goalText.SetText(ZString.Format("{0} / {1}", _connectedCount, _config.pairCount));
            UpdateGoalSlider((float)_connectedCount / _config.pairCount);
        }

        #endregion
    }
}
