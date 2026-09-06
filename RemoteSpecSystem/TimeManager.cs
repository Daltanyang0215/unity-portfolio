using System;
using UnityEngine;

namespace IdleMineForge.Core
{
    /// <summary>
    /// 시간 관련 매니저 (MonoBehaviour 싱글톤)
    /// GameManager가 초기화 순서 3번으로 Init() 호출 (DataManager, SpecDataManager 이후)
    ///
    /// 제공 기능:
    ///   OnSecondTick  : 1초마다 발생. MineManager 등 고우선도 시스템 구독
    ///   OnMinuteTick  : 1분마다 발생. 저우선도 시스템 구독
    ///   OfflineElapsed: 오프라인 경과 시간 (캡 적용). OfflineRwdManager가 사용
    ///   CurrentUtcTime: 현재 UTC 시간. DataManager가 저장 시점에 호출
    ///
    /// 주의:
    ///   maxOfflineHours 기본값 4시간. 추후 스펙 데이터에서 받을 예정
    ///   PlayerData.LastSavedTime은 DataManager가 저장/로드 담당
    /// </summary>
    public class TimeManager : SingletonBase<TimeManager>
    {
        #region 상수

        private const float DEFAULT_MAX_OFFLINE_HOURS = 4f;
        private const float SECOND_INTERVAL = 1f;
        private const int SECONDS_PER_MINUTE = 60;

        #endregion

        #region 이벤트

        /// <summary>1초마다 발생. 고우선도 시스템(MineManager 등)이 구독.</summary>
        public event Action OnSecondTick;

        /// <summary>1분마다 발생. 저우선도 시스템이 구독.</summary>
        public event Action OnMinuteTick;

        #endregion

        #region 프로퍼티

        /// <summary>
        /// 앱 종료 시점부터 현재까지의 오프라인 경과 시간 (캡 적용).
        /// Init() 이후 유효. OfflineRwdManager가 사용.
        /// </summary>
        public TimeSpan OfflineElapsed { get; private set; }

        /// <summary>현재 UTC 시간. DataManager가 저장 시점에 호출하여 LastSavedTime 기록.</summary>
        public DateTime CurrentUtcTime => DateTime.UtcNow;

        #endregion

        #region 내부 상태

        private float _secondAccumulator;
        private int _secondCounter; // 분 틱 카운터
        private float _maxOfflineHours;
        private DateTime _pauseStartTime;

        #endregion

        #region 초기화

        /// <summary>
        /// GameManager에서 DataManager 로드 직후 호출.
        /// </summary>
        /// <param name="lastSavedTime">PlayerData.LastSavedTime (UTC). 첫 실행 시 DateTime.UtcNow 전달.</param>
        /// <param name="maxOfflineHours">최대 오프라인 보상 시간(시). 기본 4시간. 추후 스펙 데이터값으로 교체.</param>
        public void Init(DateTime lastSavedTime, float maxOfflineHours = DEFAULT_MAX_OFFLINE_HOURS)
        {
            _maxOfflineHours = maxOfflineHours;
            _pauseStartTime  = default;

            var rawElapsed = DateTime.UtcNow - lastSavedTime;

            // 시스템 시계 역행 방어
            if (rawElapsed < TimeSpan.Zero)
                rawElapsed = TimeSpan.Zero;

            var maxElapsed = TimeSpan.FromHours(maxOfflineHours);
            OfflineElapsed = rawElapsed > maxElapsed ? maxElapsed : rawElapsed;

            _secondAccumulator = 0f;
            _secondCounter = 0;

            Debug.Log($"[TimeManager] 오프라인 경과: {OfflineElapsed:hh\\:mm\\:ss} (최대 {maxOfflineHours}시간)");
        }

        #endregion

        #region Public API

        /// <summary>앱 일시정지 시점을 기록. GameManager.OnApplicationPause(true)에서 호출.</summary>
        public void RecordPauseStart()
        {
            _pauseStartTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 일시정지 이후 경과 시간을 계산하고 리셋. GameManager.OnApplicationPause(false)에서 호출.
        /// 캡 적용 포함. 기록된 시점이 없으면 Zero 반환.
        /// </summary>
        public TimeSpan GetAndConsumePausedElapsed()
        {
            if (_pauseStartTime == default) return TimeSpan.Zero;

            var rawElapsed  = DateTime.UtcNow - _pauseStartTime;
            _pauseStartTime = default;

            if (rawElapsed < TimeSpan.Zero) rawElapsed = TimeSpan.Zero;

            var maxElapsed = TimeSpan.FromHours(_maxOfflineHours);
            return rawElapsed > maxElapsed ? maxElapsed : rawElapsed;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// [디버그] 오프라인 경과 시간에 지정한 시간을 추가. 오프라인 보상 테스트용.
        /// 캡 초과 시 캡 값으로 클램프.
        /// </summary>
        public void DEBUG_AddOfflineHours(float hours)
        {
            var added      = OfflineElapsed + TimeSpan.FromHours(hours);
            var maxElapsed = TimeSpan.FromHours(_maxOfflineHours);
            OfflineElapsed = added > maxElapsed ? maxElapsed : added;
            Debug.Log($"[TimeManager] DEBUG +{hours}h → OfflineElapsed: {OfflineElapsed:hh\\:mm\\:ss}");
        }
#endif

        #endregion

        #region Unity 이벤트

        private void Update()
        {
            _secondAccumulator += Time.deltaTime;
            if (_secondAccumulator < SECOND_INTERVAL) return;

            _secondAccumulator -= SECOND_INTERVAL;
            OnSecondTick?.Invoke();

            _secondCounter++;
            if (_secondCounter < SECONDS_PER_MINUTE) return;

            _secondCounter = 0;
            OnMinuteTick?.Invoke();
        }

        #endregion
    }
}
