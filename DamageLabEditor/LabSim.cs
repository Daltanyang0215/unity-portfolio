using UnityEditor;
using UnityEngine;
using NightView.Data;
using NightView.Gameplay;

namespace NightView.EditorTools.DamageLab
{
    /// <summary>
    /// 수동 시뮬 컨트롤러 (DAMAGE_LAB §7) — 인게임 루프(클리어→보상→상점→다음)를 숫자판으로 재현.
    /// 기존 순수 시스템 재사용: RunState·RunLoadout·ShopManager·StageProgression·LabEngine.Walk.
    /// ShopManager(MonoBehaviour)는 숨김 GameObject로 인스턴스화하고 풀을 SerializedObject로 주입(§7.7) — 로직 무수정.
    /// </summary>
    public sealed class LabSim
    {
        public GunDataSO Gun;
        public ShopPoolSO Pool;
        public AmmoDataSO DefaultAmmo;
        public int BaseMagazineCount = 4;
        public int StartCurrency = 0;

        public RunState Run { get; private set; }
        public RunLoadout Loadout { get; private set; }
        public ShopManager Shop { get; private set; }
        public int Stage { get; private set; }
        public string Message { get; private set; }

        /// <summary>현재 스테이지 보스 패시브 (자동 시뮬이 스테이지마다 설정). 기본 None — 수동 시뮬은 보스 무영향.</summary>
        public BossSpec Boss;

        public bool Ready => Shop != null && Loadout != null && Run != null;
        /// <summary>요구치 — 곡선(보스 가중 ×2 포함) × 보스 가중요구치(HighDemand). 게임 GetStage와 동일.</summary>
        public float Required => StageProgression.GetRequired(Stage) * BossGimmickResolver.RequiredMultiplier(Boss);

        public void Reset()
        {
            Dispose();
            Message = null;

            if (Gun == null) { Message = "총기(GunDataSO)를 지정하세요."; return; }
            if (Pool == null) Pool = FindFirst<ShopPoolSO>();

            AmmoDataSO ammo = DefaultAmmo != null ? DefaultAmmo : (Pool != null ? Pool.DefaultAmmo : null);
            if (ammo == null) { Message = "기본탄(AmmoDataSO)이 필요합니다 — 기본탄을 지정하거나 ShopPool.DefaultAmmo를 설정하세요."; return; }

            Run = new RunState(Mathf.Max(0, StartCurrency));
            RunBuild build = BuildFactory.Create(Gun, ammo, Mathf.Max(1, BaseMagazineCount));
            Loadout = new RunLoadout(build);
            Shop = CreateShop();
            Stage = 1;
        }

        /// <summary>[다음] — 현재 빌드로 현재 스테이지 도달 시: 보상 → 다음 스테이지 → 상점 재오픈.</summary>
        public void Next(LabSeries series)
        {
            if (!Ready || series == null || !series.Cleared) return;
            int cleared = Stage;
            int unused = Mathf.Max(0, Loadout.Build.MagazineCount - series.ClearedTurn);
            Run.AwardStageClear(unused);     // 보상 = 기본(고정) + 잔여 탄창 보너스 (§11.3 — 이자·스테이지 스케일 폐지)
            Loadout.ResetVisitFlags();       // 환급 경계 100%→20%
            Stage = cleared + 1;
            Shop.Open(cleared);              // 다음 라인업 + 리롤비 초기화
        }

        public void Dispose()
        {
            if (Shop != null) { Object.DestroyImmediate(Shop.gameObject); Shop = null; }
            Run = null;
            Loadout = null;
            Stage = 0;
        }

        private ShopManager CreateShop()
        {
            var go = EditorUtility.CreateGameObjectWithHideFlags("[DamageLab Shop]", HideFlags.HideAndDontSave);
            var shop = go.AddComponent<ShopManager>();

            // 인스펙터가 하는 일을 코드로 — 풀 SO 1개 주입(로직 무수정, §7.7)
            var so = new SerializedObject(shop);
            SerializedProperty p = so.FindProperty("_pool");
            if (p != null) p.objectReferenceValue = Pool;
            so.ApplyModifiedPropertiesWithoutUndo();

            shop.Initialize(Run, Loadout);
            shop.Open(0); // 시작 상점(스테이지1 준비)
            return shop;
        }

        private static T FindFirst<T>() where T : Object
        {
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
        }
    }
}
