using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using NightView.Data;
using NightView.Gameplay;

namespace NightView.EditorTools.DamageLab
{
    /// <summary>플레이 유형(AI 성향) — 바닥~천장 + 아키타입. (DAMAGE_LAB §7.8)</summary>
    public enum PlayStyle { Greedy, MoreStack, Volume, Elemental, Random }

    /// <summary>
    /// 자동 시뮬(통계) 엔진 — AI가 픽하는 수동 런을 N회 반복해 밸런스 통계 산출.
    /// LabSim(M6)을 1런 엔진으로 재사용, 픽만 정책이 대신한다(RunLoadout.Preview* 활용). 코어 무수정.
    /// </summary>
    public static class LabAuto
    {
        public static readonly PlayStyle[] Styles =
            { PlayStyle.Greedy, PlayStyle.MoreStack, PlayStyle.Volume, PlayStyle.Elemental, PlayStyle.Random };

        public static string Name(PlayStyle s) => s switch
        {
            PlayStyle.Greedy => "탐욕(천장)",
            PlayStyle.MoreStack => "승산 스택",
            PlayStyle.Volume => "물량",
            PlayStyle.Elemental => "속성",
            PlayStyle.Random => "랜덤(바닥)",
            _ => s.ToString(),
        };

        public static string Desc(PlayStyle s) => s switch
        {
            PlayStyle.Greedy => "매 구매에서 출력 증가가 가장 큰 항목을 산다 — '잘 짜면 어디까지'의 상한.",
            PlayStyle.MoreStack => "승산(More·전체승산) 모드를 우선 — 안정적 지수 성장.",
            PlayStyle.Volume => "탄창 수·크기(악세)를 우선 — 많은 탄·작은 숫자.",
            PlayStyle.Elemental => "속성 증가·화염탄을 우선 — 누적·지속(DoT) 빌드.",
            PlayStyle.Random => "살 수 있는 것 중 무작위 — '막 해도 어디까지'의 하한.",
            _ => "",
        };

        public struct AutoConfig
        {
            public GunDataSO Gun;
            public ShopPoolSO Pool;
            public AmmoDataSO DefaultAmmo;
            public int BaseMagazineCount;
            public int StartCurrency;
            public int MaxStage;
            public int Runs;
            public int Seed;
            /// <summary>보스 오버라이드 — occurrence별(스3=[0], 스6=[1]…). None = 시드 랜덤(셔플). null = 전부 랜덤.</summary>
            public BossGimmick[] BossOverrides;
        }

        /// <summary>통계창 표시용 — 시드/오버라이드로 최종 적용된 보스 라인업 1줄.</summary>
        public struct BossLine
        {
            public int Stage;
            public BossGimmick Gimmick;
        }

        public sealed class StyleResult
        {
            public PlayStyle Style;
            public int Runs;
            public int MaxStage;                    // 도달 최대
            public int[] ClearedAtLeast;            // [stage] = stage 이상 도달한 런 수 (1-기반)
            public readonly List<int> Reached = new List<int>();

            public double[] OutSum;                 // [stage] 도달 런의 출력 합 (Walk StageTotal)
            public int[] OutCount;                  // [stage] 도달 런 수
            public long EarnSum, SpentSum, LeftSum; // 경제 (런별 합산)
            public readonly Dictionary<string, int> Picks = new Dictionary<string, int>(); // 채택 횟수

            public float ClearRate(int stage) => Runs > 0 ? (float)ClearedAtLeast[stage] / Runs : 0f;
            public float AvgOutput(int stage) => OutCount[stage] > 0 ? (float)(OutSum[stage] / OutCount[stage]) : 0f;
            public float AvgEarned => Runs > 0 ? (float)EarnSum / Runs : 0f;
            public float AvgSpent => Runs > 0 ? (float)SpentSum / Runs : 0f;
            public float AvgLeft => Runs > 0 ? (float)LeftSum / Runs : 0f;

            public int Median()
            {
                if (Reached.Count == 0) return 0;
                var c = new List<int>(Reached);
                c.Sort();
                return c[c.Count / 2];
            }
        }

        public static List<StyleResult> RunBatch(in AutoConfig cfg, out string error, out List<BossLine> lineup)
        {
            error = null;
            lineup = new List<BossLine>();
            var results = new List<StyleResult>();

            if (cfg.Gun == null) { error = "총기를 지정하세요."; return results; }
            ShopPoolSO pool = cfg.Pool != null ? cfg.Pool : FindFirst<ShopPoolSO>();
            AmmoDataSO ammo = cfg.DefaultAmmo != null ? cfg.DefaultAmmo : (pool != null ? pool.DefaultAmmo : null);
            if (ammo == null) { error = "기본탄(AmmoDataSO)이 필요합니다."; return results; }

            int maxStage = Mathf.Max(1, cfg.MaxStage);
            int runs = Mathf.Max(1, cfg.Runs);
            UnityEngine.Random.InitState(cfg.Seed);
            var rng = new System.Random(cfg.Seed);
            BossLibrary.SetRunSeed(cfg.Seed);  // 시드 셔플 보스 배정 — 오버라이드 없는 보스가 이걸 따름

            // 최종 적용 보스 라인업 (통계창 표시용) — 오버라이드 우선, 없으면 시드 셔플
            int cycle = GameConfig.BossStageCycle;
            if (cycle > 0)
                for (int s = cycle; s <= maxStage; s += cycle)
                {
                    BossSpec b = LabBoss.Resolve(s, cfg.BossOverrides);
                    if (b.Gimmick != BossGimmick.None) lineup.Add(new BossLine { Stage = s, Gimmick = b.Gimmick });
                }

            var sim = new LabSim
            {
                Gun = cfg.Gun,
                Pool = pool,
                DefaultAmmo = ammo,
                BaseMagazineCount = Mathf.Max(1, cfg.BaseMagazineCount),
                StartCurrency = Mathf.Max(0, cfg.StartCurrency),
            };

            foreach (PlayStyle style in Styles)
            {
                var sr = new StyleResult
                {
                    Style = style,
                    Runs = runs,
                    ClearedAtLeast = new int[maxStage + 2],
                    OutSum = new double[maxStage + 2],
                    OutCount = new int[maxStage + 2],
                };
                for (int r = 0; r < runs; r++)
                {
                    int reached = RunOnce(sim, style, maxStage, rng, sr, cfg.BossOverrides);
                    sr.Reached.Add(reached);
                    if (reached > sr.MaxStage) sr.MaxStage = reached;
                    for (int s = 1; s <= reached && s <= maxStage; s++) sr.ClearedAtLeast[s]++;
                }
                results.Add(sr);
            }

            sim.Dispose();
            return results;
        }

        private static int RunOnce(LabSim sim, PlayStyle style, int maxStage, System.Random rng, StyleResult sr, BossGimmick[] overrides)
        {
            sim.Reset();
            if (!sim.Ready) return 0;

            int reached = 0;
            int guard = 0;
            while (sim.Stage <= maxStage && guard++ < 10000)
            {
                int stage = sim.Stage;
                sim.Boss = LabBoss.Resolve(stage, overrides);   // 스테이지 보스 — 요구치(HighDemand)·Walk(면역·경감)에 반영
                while (ShopStep(sim, style, rng, sr)) { }       // 상점 픽 페이즈
                // 판 상태 조건(첫 턴·막 턴·잔여 턴당·정원당)을 측정에 넣는다 — 정원은 스테이지가 아는 값이다
                LabSeries series = LabEngine.Walk(sim.Loadout.Build, sim.Required, boss: sim.Boss,
                                                  fieldCap: StageProgression.GetStage(stage).FieldCap);

                if (stage <= maxStage) { sr.OutSum[stage] += series.StageTotal; sr.OutCount[stage]++; }

                if (!series.Cleared) break;                      // 현재 스테이지 못 넘김 → 종료
                reached = stage;
                sim.Next(series);                                // 보상 + 다음 스테이지 + 상점 재오픈
            }

            sr.EarnSum += sim.Run.TotalEarned;
            sr.SpentSum += sim.Run.TotalSpent;
            sr.LeftSum += sim.Run.Currency;
            return reached;
        }

        private static bool ShopStep(LabSim sim, PlayStyle style, System.Random rng, StyleResult sr)
        {
            if (style == PlayStyle.Random) return RandomBuy(sim, rng, sr);

            ShopManager shop = sim.Shop;
            RunLoadout lo = sim.Loadout;
            int money = sim.Run.Currency;
            float baseTotal = lo.GetResult().StageTotal;

            float bestScore = 0f;
            int bestKind = -1, bestIndex = -1, bestSlot = -1; // kind: 0=총기모드 1=악세 2=탄약 3=탄창모드

            var gunMods = shop.GunModOffers;
            for (int i = 0; i < gunMods.Count; i++)
            {
                ModDataSO it = gunMods[i].Item;
                if (gunMods[i].SoldOut || it == null || it.Price > money) continue;
                float delta = lo.PreviewEquipMod(it.ToSpec()).StageTotal - baseTotal;
                float sc = delta * ModPref(style, it.ModType);
                if (delta > 0f && sc > bestScore) { bestScore = sc; bestKind = 0; bestIndex = i; }
            }

            var magMods = shop.MagModOffers;
            for (int i = 0; i < magMods.Count; i++)
            {
                ModDataSO it = magMods[i].Item;
                if (magMods[i].SoldOut || it == null || it.Price > money) continue;
                float delta = lo.PreviewEquipMod(it.ToSpec()).StageTotal - baseTotal;
                float sc = delta * ModPref(style, it.ModType);
                if (delta > 0f && sc > bestScore) { bestScore = sc; bestKind = 3; bestIndex = i; }
            }

            var accs = shop.AccessoryOffers;
            for (int i = 0; i < accs.Count; i++)
            {
                AccessoryDataSO it = accs[i].Item;
                if (accs[i].SoldOut || it == null || it.Price > money) continue;
                float delta = lo.PreviewEquipAccessory(it.ToSpec()).StageTotal - baseTotal;
                float sc = delta * AccPref(style, it.AccessoryType);
                if (delta > 0f && sc > bestScore) { bestScore = sc; bestKind = 1; bestIndex = i; }
            }

            int slot = FirstDefaultSlot(lo);
            if (slot >= 0)
            {
                var ammo = shop.AmmoOffers;
                for (int i = 0; i < ammo.Count; i++)
                {
                    AmmoDataSO it = ammo[i].Item;
                    if (ammo[i].SoldOut || it == null || it == shop.DefaultAmmo || it.Price > money) continue;
                    float delta = lo.PreviewPlaceAmmo(slot, it.ToSpec()).StageTotal - baseTotal;
                    float sc = delta * AmmoPref(style, it);
                    if (delta > 0f && sc > bestScore) { bestScore = sc; bestKind = 2; bestIndex = i; bestSlot = slot; }
                }
            }

            if (bestKind < 0) return false;
            if (bestKind == 0) { RecordPick(sr, gunMods[bestIndex].Item); shop.TryBuyGunMod(bestIndex); }
            else if (bestKind == 3) { RecordPick(sr, magMods[bestIndex].Item); shop.TryBuyMagMod(bestIndex); }
            else if (bestKind == 1) { RecordPick(sr, accs[bestIndex].Item); shop.TryBuyAccessory(bestIndex); }
            else { RecordPick(sr, shop.AmmoOffers[bestIndex].Item); shop.TryBuyAmmoToSlot(bestIndex, bestSlot); }
            return true;
        }

        private static bool RandomBuy(LabSim sim, System.Random rng, StyleResult sr)
        {
            ShopManager shop = sim.Shop;
            int money = sim.Run.Currency;
            var actions = new List<(int kind, int idx, int slot)>(); // kind: 0=총기모드 1=악세 2=탄약 3=탄창모드

            var gunMods = shop.GunModOffers;
            for (int i = 0; i < gunMods.Count; i++)
                if (!gunMods[i].SoldOut && gunMods[i].Item != null && gunMods[i].Item.Price <= money) actions.Add((0, i, -1));

            var magMods = shop.MagModOffers;
            for (int i = 0; i < magMods.Count; i++)
                if (!magMods[i].SoldOut && magMods[i].Item != null && magMods[i].Item.Price <= money) actions.Add((3, i, -1));

            var accs = shop.AccessoryOffers;
            for (int i = 0; i < accs.Count; i++)
                if (!accs[i].SoldOut && accs[i].Item != null && accs[i].Item.Price <= money) actions.Add((1, i, -1));

            int slot = FirstDefaultSlot(sim.Loadout);
            if (slot >= 0)
            {
                var ammo = shop.AmmoOffers;
                for (int i = 0; i < ammo.Count; i++)
                    if (!ammo[i].SoldOut && ammo[i].Item != null && ammo[i].Item != shop.DefaultAmmo && ammo[i].Item.Price <= money)
                        actions.Add((2, i, slot));
            }

            if (actions.Count == 0 || rng.NextDouble() < 0.35) return false; // 35% 확률 중단(하한 분산)
            var a = actions[rng.Next(actions.Count)];
            if (a.kind == 0) { RecordPick(sr, gunMods[a.idx].Item); shop.TryBuyGunMod(a.idx); }
            else if (a.kind == 3) { RecordPick(sr, magMods[a.idx].Item); shop.TryBuyMagMod(a.idx); }
            else if (a.kind == 1) { RecordPick(sr, accs[a.idx].Item); shop.TryBuyAccessory(a.idx); }
            else { RecordPick(sr, shop.AmmoOffers[a.idx].Item); shop.TryBuyAmmoToSlot(a.idx, a.slot); }
            return true;
        }

        private static void RecordPick(StyleResult sr, ModDataSO it) => Inc(sr, Key(it != null ? it.DisplayName : null, it));
        private static void RecordPick(StyleResult sr, AccessoryDataSO it) => Inc(sr, Key(it != null ? it.DisplayName : null, it));
        private static void RecordPick(StyleResult sr, AmmoDataSO it) => Inc(sr, Key(it != null ? it.DisplayName : null, it));

        private static void Inc(StyleResult sr, string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            sr.Picks.TryGetValue(key, out int n);
            sr.Picks[key] = n + 1;
        }

        public static string Key(string display, Object o)
            => !string.IsNullOrEmpty(display) ? display : (o != null ? o.name : "-");

        private static float ModPref(PlayStyle s, ModType t) => s switch
        {
            PlayStyle.MoreStack => (t == ModType.More || t == ModType.MoreAll) ? 3f : 1f,
            PlayStyle.Elemental => (t == ModType.ElementalIncreased) ? 3f : 1f,
            _ => 1f,
        };

        private static float AccPref(PlayStyle s, AccessoryType t) => s == PlayStyle.Volume ? 3f : 1f;

        private static float AmmoPref(PlayStyle s, AmmoDataSO a)
            => (s == PlayStyle.Elemental && a != null && a.Element == ElementType.Fire) ? 3f : 1f;

        private static int FirstDefaultSlot(RunLoadout lo)
        {
            var slots = lo.Build.Sequence.Slots;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].IsDefault) return i;
            return -1;
        }

        private static T FindFirst<T>() where T : Object
        {
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
        }
    }
}
