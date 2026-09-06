using System;
using Cysharp.Text;
using UnityEngine;
using Random = UnityEngine.Random;

namespace NightView
{
    /// <summary>
    /// 색상 시퀀스 커맨드 입력 미니게임. BaseMinigame 상속.
    /// 자체 Update() 금지 — WorkPanelManager가 Tick(dt) 호출.
    /// </summary>
    public class ColorSequenceMinigame : BaseMinigame
    {
        #region 색상 팔레트

        // ColorSelectMinigame과 동일한 팔레트
        private static readonly Color[] DistinctColors =
        {
            Color.red, Color.blue, Color.green, Color.yellow,
            new(0.6f, 0f, 0.8f)  // 보라
        };

        #endregion

        #region SerializeField

        [Header("버튼")]
        [SerializeField] private ColorBtnObject[] _colorBtns;       // colorCount개 활성화

        [Header("시퀀스 인디케이터")]
        [SerializeField] private SeqIndicatorItem[] _indicators;    // sequenceLength 최대치에 맞게 Inspector 배치

        [Header("진행")]
        // _goalText / _goalSlider → BaseMinigame

        [Header("레이아웃")]
        [SerializeField] private float _btnHalfWidth = 1.5f;
        [SerializeField] private float _btnSpacing = 0.8f;      // 고정 간격. 범위 초과 시 균등 배치로 전환
        [SerializeField] private float _btnY = -0.8f;
        [SerializeField] private float _indicatorHalfWidth = 1.75f;
        [SerializeField] private float _indicatorSpacing = 0.7f; // 고정 간격. 범위 초과 시 균등 배치로 전환
        [SerializeField] private float _indicatorY = 0.5f;

        #endregion

        #region private 필드

        private ColorSequenceStageData _config;
        private int[] _sequence;        // 시퀀스 (색상 인덱스 배열)
        private Color[] _palette;       // 이번 게임에서 사용하는 colorCount개 색상
        private int _inputIndex;        // 현재 입력 위치
        private int _roundCount;        // 완료한 라운드 수

        #endregion

        #region BaseMinigame 구현

        protected override void OnBegin()
        {
            _config = GetConfig();
            if (_config == null) return;

            _roundCount = 0;
            _inputIndex = 0;
            _sequence = new int[_config.sequenceLength];

            // 팔레트에서 colorCount개 색상 추출 (Fisher-Yates)
            int count = _config.colorCount;
            Color[] pool = new Color[DistinctColors.Length];
            Array.Copy(DistinctColors, pool, DistinctColors.Length);

            for (int i = 0; i < count; i++)
            {
                int j = Random.Range(i, pool.Length);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            _palette = new Color[count];
            Array.Copy(pool, _palette, count);

            // colorCount개만큼 버튼 활성화, 나머지 비활성
            for (int i = 0; i < _colorBtns.Length; i++)
            {
                bool active = i < count;
                _colorBtns[i].gameObject.SetActive(active);
                if (active)
                    _colorBtns[i].Init(_palette[i], i, HandleBtnClick);
            }

            LayoutBtns(count);
            UpdateGoalUI();
            StartNewRound();
        }

        protected override void OnTick(float dt)
        {
            // 패널티 없음 — 빈 구현
        }

        protected override void OnEnd(bool success)
        {
            // 버튼 이벤트 해제
            for (int i = 0; i < _colorBtns.Length; i++)
                _colorBtns[i].ClearClick();
        }

        #endregion

        #region Private 메서드

        private ColorSequenceStageData GetConfig()
        {
            if (SpecData is ColorSequenceDataSO so)
                return so.GetStage(Stage);
            return null;
        }

        /// <summary>새 라운드 시작. 시퀀스 랜덤 생성 후 인디케이터 갱신.</summary>
        private void StartNewRound()
        {
            int len = _config.sequenceLength;

            for (int i = 0; i < len; i++)
                _sequence[i] = Random.Range(0, _config.colorCount);

            _inputIndex = 0;
            RefreshIndicators();
        }

        /// <summary>인디케이터 전체 UI 갱신. 현재 inputIndex 기준으로 Dimmed/Marker 적용.</summary>
        private void RefreshIndicators()
        {
            int len = _config.sequenceLength;

            for (int i = 0; i < _indicators.Length; i++)
            {
                bool inRange = i < len;
                _indicators[i].gameObject.SetActive(inRange);

                if (!inRange) continue;

                _indicators[i].SetColor(_palette[_sequence[i]]);
                _indicators[i].SetDimmed(i < _inputIndex);
                _indicators[i].SetMarker(i == _inputIndex);
            }

            LayoutIndicators(len);
        }

        private void HandleBtnClick(ColorBtnObject btn)
        {
            if (!IsActive) return;

            if (btn.Index == _sequence[_inputIndex])
            {
                // 정답: 해당 인디케이터 Dimmed 처리 후 inputIndex 진행
                _indicators[_inputIndex].SetDimmed(true);
                _indicators[_inputIndex].SetMarker(false);
                _inputIndex++;

                if (_inputIndex >= _config.sequenceLength)
                {
                    // 시퀀스 완료 — 라운드 처리
                    _roundCount++;
                    UpdateGoalUI();

                    if (_roundCount >= _config.targetRoundCount)
                        Succeed();
                    else
                        StartNewRound();
                }
                else
                {
                    // 다음 인디케이터에 마커 이동
                    _indicators[_inputIndex].SetMarker(true);
                }
            }
            else
            {
                // 오답
                if (_config.failOnWrong)
                {
                    Fail();
                }
                else
                {
                    ShakeOnMistake();
                    _inputIndex = 0;
                    RefreshIndicators();
                }
            }
        }

        private void UpdateGoalUI()
        {
            _goalText.SetText(ZString.Format("{0} / {1}", _roundCount, _config.targetRoundCount));
            UpdateGoalSlider(_roundCount <= 0 ? 0f : (float)_roundCount / _config.targetRoundCount);
        }

        // 고정 간격이 범위 안이면 고정 간격 + 중앙 정렬, 초과하면 균등 배치
        private void LayoutBtns(int count)
        {
            if (count <= 1) { _colorBtns[0].transform.localPosition = new Vector3(0f, _btnY, 0f); return; }
            float rangeStep = _btnHalfWidth * 2f / (count - 1);
            float step = Mathf.Min(_btnSpacing, rangeStep);
            float startX = -step * (count - 1) / 2f;
            for (int i = 0; i < count; i++)
                _colorBtns[i].transform.localPosition = new Vector3(startX + step * i, _btnY, 0f);
        }

        private void LayoutIndicators(int count)
        {
            if (count <= 1) { _indicators[0].transform.localPosition = new Vector3(0f, _indicatorY, 0f); return; }
            float rangeStep = _indicatorHalfWidth * 2f / (count - 1);
            float step = Mathf.Min(_indicatorSpacing, rangeStep);
            float startX = -step * (count - 1) / 2f;
            for (int i = 0; i < count; i++)
                _indicators[i].transform.localPosition = new Vector3(startX + step * i, _indicatorY, 0f);
        }

        #endregion
    }
}
