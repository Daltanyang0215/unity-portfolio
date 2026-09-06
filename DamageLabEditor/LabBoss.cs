using NightView.Gameplay;

namespace NightView.EditorTools.DamageLab
{
    /// <summary>
    /// DamageLab 보스 해석 — 스테이지별 오버라이드(없음=시드 셔플)를 BossSpec으로 변환.
    /// 기믹별 파라미터는 게임 BossLibrary 풀을 그대로 재사용한다(Lab이 따로 정의하면 값이 조용히 어긋남).
    /// </summary>
    public static class LabBoss
    {
        /// <summary>기믹 → BossSpec (게임 풀 파라미터 그대로). None = BossSpec.None.</summary>
        public static BossSpec Make(BossGimmick g) => BossLibrary.Make(g);

        /// <summary>
        /// 스테이지의 보스 — 비보스면 None. 보스면: 오버라이드(occurrence별, None=랜덤)가 있으면 그 기믹,
        /// 없으면 시드 셔플(BossLibrary, 호출 전 SetRunSeed 선행). overrides[occurrence-1] 참조.
        /// </summary>
        public static BossSpec Resolve(int stage, BossGimmick[] overrides)
        {
            int cycle = GameConfig.BossStageCycle;
            if (cycle <= 0 || stage < 1 || stage % cycle != 0) return BossSpec.None;

            int occ = stage / cycle; // 1-기반 (스3→1, 스6→2 …)
            if (overrides != null && occ - 1 < overrides.Length && overrides[occ - 1] != BossGimmick.None)
                return Make(overrides[occ - 1]);
            return BossLibrary.GetBoss(stage); // 시드 셔플 (게임 규칙 그대로)
        }
    }
}
