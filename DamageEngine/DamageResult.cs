namespace NightView.Gameplay
{
    /// <summary>
    /// 데미지 계산 결과. 단계별 중간값을 포함해 통계 UI(재미 3)·검증에 활용.
    /// </summary>
    public struct DamageResult
    {
        public float PerShot;    // 평균 탄당 데미지 (시퀀스 턴당 ÷ 탄창 크기, 크리 기댓값 반영) — 통계 UI "탄당"
        public int   TotalShots; // 총 탄 수 (탄창 크기 × 탄창 수)
        public float StageTotal; // 스테이지 총 데미지 — 통계 UI "예상 총"

        // ── 단계별 중간값 (디버그/프리뷰) ──
        public float AfterFlat;      // (기본 + 가산)
        public float AfterIncreased; // × (1 + Σ증가%)
        public float AfterMore;      // × Π(1 + 승산%)
        public float CritMult;       // 크리 기댓값 배수

        /// <summary>턴당 데미지 = 탄당 × 탄창 크기 — 통계 UI "턴당"</summary>
        public readonly float PerTurn(int magazineSize) => PerShot * magazineSize;
    }
}
