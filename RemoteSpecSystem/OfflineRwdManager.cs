using System;
using Cysharp.Text;
using IdleMineForge.Data;
using UnityEngine;

namespace IdleMineForge.Core
{
    /// <summary>
    /// 오프라인 보상 계산 및 지급 (순수 C# 싱글톤)
    ///
    /// 계산 공식:
    ///   totalDps     = Σ (ApplyMiningPower(BaseMiningPower) / ApplyMiningInterval(BaseAps))  — 장착 슬롯 합산
    ///   oreMaxHp     = OreSpec.BaseHp × StageSpec.OreMultiplier
    ///   oresDestroyed = totalDps × offlineSeconds / oreMaxHp
    ///   coinBase     = oresDestroyed × (OreSpec.GoldReward × StageSpec.CoinMultiplier)
    ///   coins        = StatCalculator.ApplyCoinReward(coinBase)
    ///
    /// Init() 이후 HasPendingReward가 true이면 UI에서 ClaimReward() 호출.
    /// </summary>
    public class OfflineRwdManager
    {
        #region 싱글톤

        private static OfflineRwdManager _instance;
        public static OfflineRwdManager Instance => _instance ??= new OfflineRwdManager();

        private OfflineRwdManager() { }

        #endregion

        #region 프로퍼티

        /// <summary>지급 대기 중인 보상이 있으면 true.</summary>
        public bool HasPendingReward { get; private set; }

        /// <summary>지급 예정 코인 합계 (UI 표시용).</summary>
        public long PendingCoins { get; private set; }

        private static readonly int WORLD_COUNT = Enum.GetValues(typeof(WorldType)).Length;

        /// <summary>보상 적용 최소 오프라인 시간.</summary>
        private static readonly TimeSpan MIN_OFFLINE = TimeSpan.FromMinutes(5);

        /// <summary>세계별 지급 예정 코인. 인덱스는 (int)WorldType.</summary>
        private readonly long[] _pendingCoinsByWorld = new long[WORLD_COUNT];

        /// <summary>계산에 사용된 오프라인 경과 시간.</summary>
        public TimeSpan PendingElapsed { get; private set; }

        #endregion

        #region 초기화

        /// <summary>
        /// GameManager 초기화 완료 후 호출. TimeManager.OfflineElapsed 기준으로 보상 계산.
        /// offlineElapsed가 0 이하면 보상 없음.
        /// </summary>
        public void Init(TimeSpan offlineElapsed)
        {
            HasPendingReward = false;
            PendingCoins     = 0;
            Array.Clear(_pendingCoinsByWorld, 0, WORLD_COUNT);
            PendingElapsed   = offlineElapsed;

            if (offlineElapsed < MIN_OFFLINE)
            {
                Debug.Log($"[OfflineRwdManager] 오프라인 시간 {offlineElapsed.TotalMinutes:F1}분 — 최소 5분 미만, 보상 없음");
                return;
            }

            for (int w = 0; w < WORLD_COUNT; w++)
            {
                var world = (WorldType)w;
                long coins = CalcOfflineCoins(offlineElapsed, world);
                _pendingCoinsByWorld[w] = coins;
                PendingCoins += coins;
            }

            if (PendingCoins <= 0)
            {
                Debug.Log("[OfflineRwdManager] 계산 결과 0 코인 — 보상 없음 (장착 슬롯 비어있거나 스테이지 스펙 없음)");
                return;
            }

            HasPendingReward = true;

            Debug.Log(ZString.Format(
                "[OfflineRwdManager] 오프라인 보상 준비 완료 — 경과: {0:hh\\:mm\\:ss}, 합계 코인: {1}",
                offlineElapsed, PendingCoins));
        }

        #endregion

        #region Public API

        /// <summary>
        /// 보상 지급. HasPendingReward == true 일 때 UI에서 호출.
        /// </summary>
        public void ClaimReward()
        {
            if (!HasPendingReward) return;

            for (int w = 0; w < WORLD_COUNT; w++)
            {
                if (_pendingCoinsByWorld[w] <= 0) continue;
                var currency = WorldManager.GetWorldCurrency((WorldType)w);
                DataManager.Instance.AddCurrency(currency, _pendingCoinsByWorld[w]);
            }

            HasPendingReward = false;

            Debug.Log(ZString.Format("[OfflineRwdManager] 보상 지급 완료 — 합계 +{0}", PendingCoins));
        }

        #endregion

        #region 계산

        private static long CalcOfflineCoins(TimeSpan elapsed, WorldType world)
        {
            float offlineSeconds = (float)elapsed.TotalSeconds;

            float totalDps = CalcTotalDps(world);
            if (totalDps <= 0f) return 0;

            // 세계별 스테이지 스펙 조회 (PlayerData 기반, StageManager 의존 없음)
            var worldProgress = DataManager.Instance.PlayerData.Progress.GetWorldProgress(world);
            int stageLevel    = worldProgress.CurrentStageLevel;
            var stageSpec     = SpecDataManager.Instance.GetStageSpec(stageLevel);
            if (stageSpec == null) return 0;

            var oreSpec = SpecDataManager.Instance.GetOreSpec(stageSpec.MainOreId);
            if (oreSpec == null) return 0;

            float oreMaxHp = oreSpec.BaseHp * stageSpec.OreMultiplier;
            if (oreMaxHp <= 0f) return 0;

            double oresDestroyed = (double)totalDps * offlineSeconds / oreMaxHp;

            // 코인: 스테이지 CoinMultiplier 적용 후 StatCalculator로 업그레이드 배율 적용
            long coinPerOre = (long)(oreSpec.GoldReward * stageSpec.CoinMultiplier);
            long coins      = StatCalculator.ApplyCoinReward((long)(oresDestroyed * coinPerOre));

            // TODO: 광고 버프 시 오프라인 캡 확장(OfflineTimeCapAdd) 또는 배율 추가 지급

            return coins;
        }

        private static float CalcTotalDps(WorldType world)
        {
            var slots = DataManager.Instance.PlayerData.Equip.GetEquipSlots(world);
            float totalDps = 0f;

            for (int i = 0; i < slots.Count; i++)
            {
                int toolId = slots[i];
                if (toolId == 0) continue;

                var spec = SpecDataManager.Instance.GetToolSpec(toolId);
                if (spec == null) continue;

                long power     = StatCalculator.ApplyMiningPower(spec.BaseMiningPower);
                float interval = StatCalculator.ApplyMiningInterval(spec.BaseAps);

                if (interval > 0f)
                    totalDps += power / interval;
            }

            return totalDps;
        }

        #endregion
    }
}
