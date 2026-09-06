using System;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using Firebase;
using Firebase.Analytics;
using IdleMineForge.Data;
using UnityEngine;

namespace IdleMineForge.Core
{
    /// <summary>
    /// Firebase Analytics 이벤트 및 유저 프로퍼티 관리자 (MonoBehaviour 싱글톤)
    ///
    /// 초기화 순서:
    ///   1. GameManager.InitAllAsync() 초반 → Init() 호출 (Firebase 의존성 비동기 체크 시작)
    ///   2. 모든 매니저 초기화 완료 후 → SetupEventSubscriptions() 호출
    ///   3. Firebase 체크 완료 시 → _isReady = true, install_date(최초 1회), first_session_start 발송
    ///
    /// 1회성 이벤트: PlayerPrefs "Analytics_First_*" 키로 중복 발송 방지
    ///   예외 - first_equip: Tutorial.HasEquipped 플래그로 판단 (EquipHint와 동일 조건)
    /// 문자열 결합: ZString 사용 (GC 방지)
    /// </summary>
    public class AnalyticsManager : SingletonBase<AnalyticsManager>
    {
        private bool _isReady;
        private DateTime _appStartTime;
        private DateTime _lastPrestigeTime;

        private const string PREFS_PREFIX = "Analytics_First_";
        private const string PREFS_INSTALL_DATE = "Analytics_InstallDate";

        // upgrade_spec.csv 환생 강화 ID — 게임 구조에 영향을 주는 항목만 추적
        private const int UPGRADE_ID_MergeSlot = 2005;
        private const int UPGRADE_ID_EquipSlot = 2006;
        private const int UPGRADE_ID_DrawLevel = 2012;

        #region Unity 이벤트

        protected override void Awake()
        {
            base.Awake();
            _appStartTime = DateTime.UtcNow;
            _lastPrestigeTime = DateTime.UtcNow;
        }

        private void OnDestroy()
        {
            if (ToolEquipManager.Instance != null) ToolEquipManager.Instance.OnToolEquipped -= HandleToolEquipped;
            if (UpgradeManager.Instance != null) UpgradeManager.Instance.OnUpgraded -= HandleUpgraded;
        }

        #endregion

        #region 초기화

        public void Init() => InitFirebaseAsync().Forget();

        /// <summary>
        /// 모든 매니저 초기화 완료 후 GameManager에서 호출.
        /// Firebase 준비 여부와 무관하게 C# 이벤트 구독만 처리 → 핸들러 내부에서 _isReady 검사.
        /// </summary>
        public void SetupEventSubscriptions()
        {
            ToolEquipManager.Instance.OnToolEquipped += HandleToolEquipped;
            UpgradeManager.Instance.OnUpgraded += HandleUpgraded;
        }

        private async UniTaskVoid InitFirebaseAsync()
        {
            var status = await FirebaseApp.CheckAndFixDependenciesAsync();
            await UniTask.SwitchToMainThread();

            if (status != DependencyStatus.Available)
            {
                Debug.LogWarning($"[AnalyticsManager] Firebase 의존성 해결 실패: {status}");
                return;
            }

            FirebaseAnalytics.SetAnalyticsCollectionEnabled(true);
            _isReady = true;

            // install_date: 최초 설치 시 1회만 저장 및 전송
            if (!PlayerPrefs.HasKey(PREFS_INSTALL_DATE))
            {
                PlayerPrefs.SetString(PREFS_INSTALL_DATE, DateTime.UtcNow.ToString("yyyyMMdd"));
                PlayerPrefs.Save();
                FirebaseAnalytics.SetUserProperty("install_date", PlayerPrefs.GetString(PREFS_INSTALL_DATE));
            }

            LogFirst(FirstEventType.SessionStart);

            Debug.Log("[AnalyticsManager] Firebase Analytics 초기화 완료");
        }

        #endregion

        #region 이벤트 핸들러

        private void HandleToolEquipped(int slotIndex, int toolId)
        {
            if (toolId == 0 || !_isReady) return;

            // Tutorial.HasEquipped = EquipHint와 동일 조건. false인 시점 = 최초 장착 직전
            if (!DataManager.Instance.PlayerData.Tutorial.HasEquipped)
            {
                FirebaseAnalytics.LogEvent("first_equip", new[]
                {
                    new Parameter("playtime_minutes", GetPlaytimeMinutes()),
                });
            }
        }

        private void HandleUpgraded(int upgradeId, int newLevel)
        {
            LogFirst(FirstEventType.Upgrade);
            LogUpgrade(upgradeId, newLevel);
        }

        #endregion

        #region 공개 API

        /// <summary>1회성 첫 경험 이벤트. PlayerPrefs로 중복 발송 방지.</summary>
        public void LogFirst(FirstEventType type)
        {
            if (!_isReady) return;

            string key = ZString.Concat(PREFS_PREFIX, type.ToString());
            if (PlayerPrefs.GetInt(key, 0) == 1) return;
            PlayerPrefs.SetInt(key, 1);
            PlayerPrefs.Save();

            // first_prestige는 LogPrestige() 내에서 처리
            if (type == FirstEventType.Prestige) return;

            FirebaseAnalytics.LogEvent(GetFirstEventName(type), new[]
            {
                new Parameter("playtime_minutes", GetPlaytimeMinutes()),
            });
        }

        /// <summary>
        /// 환생 이벤트. RebirthManager.TryRebirth()에서 DataManager.ResetForRebirth() 호출 전에 실행.
        /// first_prestige(1회성) + prestige(매회) 동시 발송.
        /// </summary>
        public void LogPrestige()
        {
            if (!_isReady) return;

            var data = DataManager.Instance.PlayerData;
            long prestigeCount = data.Progress.RebirthCount + 1; // ResetForRebirth 전 호출 → +1이 이번 횟수
            int playtimeMinutes = GetPlaytimeMinutes();
            int playtimeSinceLast = (int)(DateTime.UtcNow - _lastPrestigeTime).TotalMinutes;
            int equipTier = GetMaxEquipTier();
            int maxMineId = StageManager.Instance.MaxStageLevel;
            int codexMaxTier = GetCodexMaxTier();
            int mergeLv = UpgradeManager.Instance.GetLevel(UPGRADE_ID_MergeSlot);
            int equipLv = UpgradeManager.Instance.GetLevel(UPGRADE_ID_EquipSlot);
            int drawLv = UpgradeManager.Instance.GetLevel(UPGRADE_ID_DrawLevel);

            // first_prestige (1회성)
            string firstKey = ZString.Concat(PREFS_PREFIX, FirstEventType.Prestige.ToString());
            if (PlayerPrefs.GetInt(firstKey, 0) == 0)
            {
                PlayerPrefs.SetInt(firstKey, 1);
                PlayerPrefs.Save();
                FirebaseAnalytics.LogEvent("first_prestige", new[]
                {
                    new Parameter("playtime_minutes", playtimeMinutes),
                    new Parameter("equip_tier",       equipTier),
                    new Parameter("max_mine_id",      maxMineId),
                    new Parameter("codex_max_tier",   codexMaxTier),
                });
            }

            // prestige (매번)
            FirebaseAnalytics.LogEvent("prestige", new[]
            {
                new Parameter("prestige_count",        prestigeCount),
                new Parameter("playtime_minutes",      playtimeMinutes),
                new Parameter("playtime_since_last",   playtimeSinceLast),
                new Parameter("equip_tier",            equipTier),
                new Parameter("max_mine_id",           maxMineId),
                new Parameter("codex_max_tier",        codexMaxTier),
                new Parameter("upgrade_merge_slot_lv", mergeLv),
                new Parameter("upgrade_equip_slot_lv", equipLv),
                new Parameter("upgrade_draw_lv",       drawLv),
            });

            FirebaseAnalytics.SetUserProperty("current_prestige", prestigeCount.ToString());
            _lastPrestigeTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 환생 강화 중 구조 변화에 영향을 주는 항목(머지슬롯/장착슬롯/뽑기)만 User Property 갱신.
        /// UpgradeManager.OnUpgraded 구독으로 자동 호출.
        /// </summary>
        public void LogUpgrade(int upgradeId, int levelAfter)
        {
            if (!_isReady) return;

            if (upgradeId == UPGRADE_ID_MergeSlot)
                FirebaseAnalytics.SetUserProperty("upgrade_merge_slot_lv", levelAfter.ToString());
            else if (upgradeId == UPGRADE_ID_EquipSlot)
                FirebaseAnalytics.SetUserProperty("upgrade_equip_slot_lv", levelAfter.ToString());
            else if (upgradeId == UPGRADE_ID_DrawLevel)
                FirebaseAnalytics.SetUserProperty("upgrade_draw_lv", levelAfter.ToString());
        }

        /// <summary>보상형 광고 요청 시.</summary>
        public void LogAdRequest(AdContext context)
        {
            if (!_isReady) return;
            FirebaseAnalytics.LogEvent("rewarded_ad_request", new[]
            {
                new Parameter("context", GetAdContextString(context)),
            });
        }

        /// <summary>보상형 광고 시청 완료 시.</summary>
        public void LogAdComplete(AdContext context)
        {
            if (!_isReady) return;
            FirebaseAnalytics.LogEvent("rewarded_ad_complete", new[]
            {
                new Parameter("context", GetAdContextString(context)),
            });
        }

        /// <summary>보상형 광고 중도 이탈 시.</summary>
        public void LogAdSkip(AdContext context)
        {
            if (!_isReady) return;
            FirebaseAnalytics.LogEvent("rewarded_ad_skip", new[]
            {
                new Parameter("context", GetAdContextString(context)),
            });
        }

        /// <summary>세션 시작 후 유저 프로퍼티 일괄 갱신. GameManager에서 호출.</summary>
        public void RefreshAllUserProperties()
        {
            if (!_isReady) return;

            var data = DataManager.Instance.PlayerData;
            FirebaseAnalytics.SetUserProperty("current_prestige", data.Progress.RebirthCount.ToString());
            FirebaseAnalytics.SetUserProperty("equip_main_tier", GetMaxEquipTier().ToString());
            FirebaseAnalytics.SetUserProperty("codex_max_tier", GetCodexMaxTier().ToString());
            FirebaseAnalytics.SetUserProperty("upgrade_merge_slot_lv", UpgradeManager.Instance.GetLevel(UPGRADE_ID_MergeSlot).ToString());
            FirebaseAnalytics.SetUserProperty("upgrade_equip_slot_lv", UpgradeManager.Instance.GetLevel(UPGRADE_ID_EquipSlot).ToString());
            FirebaseAnalytics.SetUserProperty("upgrade_draw_lv", UpgradeManager.Instance.GetLevel(UPGRADE_ID_DrawLevel).ToString());
        }

        #endregion

        #region 내부 헬퍼

        private int GetPlaytimeMinutes() => (int)(DateTime.UtcNow - _appStartTime).TotalMinutes;

        private int GetMaxEquipTier()
        {
            var slots = DataManager.Instance.PlayerData.Equip.GetEquipSlots(WorldManager.Instance.ActiveWorld);
            int maxTier = 0;
            foreach (int toolId in slots)
            {
                if (toolId == 0) continue;
                var spec = SpecDataManager.Instance.GetToolSpec(toolId);
                if (spec != null && spec.Tier > maxTier) maxTier = spec.Tier;
            }
            return maxTier;
        }

        private int GetCodexMaxTier()
        {
            var seenIds = DataManager.Instance.PlayerData.Equip.SeenToolIds;
            int maxTier = 0;
            foreach (int toolId in seenIds)
            {
                var spec = SpecDataManager.Instance.GetToolSpec(toolId);
                if (spec != null && spec.Tier > maxTier) maxTier = spec.Tier;
            }
            return maxTier;
        }

        private static string GetFirstEventName(FirstEventType type) => type switch
        {
            FirstEventType.SessionStart => "first_session_start",
            FirstEventType.Equip => "first_equip",
            FirstEventType.Upgrade => "first_upgrade",
            FirstEventType.Prestige => "first_prestige",
            FirstEventType.UpgradePanelOpen => "first_upgrade_panel_open",
            _ => "unknown_first_event",
        };

        private static string GetAdContextString(AdContext context) => context switch
        {
            AdContext.OfflineReward => "offline_reward",
            AdContext.PrestigeBonus => "prestige_bonus",
            _ => "unknown",
        };

        #endregion
    }
}
