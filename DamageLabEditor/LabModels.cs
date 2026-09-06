using System.Collections.Generic;
using NightView.Data;

namespace NightView.EditorTools.DamageLab
{
    /// <summary>그래프 x축 단위 — 탄(발사) / DoT칸(턴 종료 화상 틱). (DAMAGE_LAB §3.1)</summary>
    public enum LabSampleKind { Shot, Dot }

    /// <summary>막대 1개 = 탄 1발 또는 탄창 끝 DoT칸 1개. (DAMAGE_LAB §3.2)</summary>
    public struct LabSample
    {
        public int Turn;            // 1-기반 탄창(턴)
        public int ShotIndex;       // 턴 내 발사 순번 (DoT칸 = -1)
        public LabSampleKind Kind;
        public float Physical;      // 물리 (크리 기댓값)
        public float ElementalHit;  // 속성 즉발
        public float Dot;           // DoT칸 화상 강도(틱)
        public float BaseValue;     // 조건 무(상시만) 탄당 — 보너스 차분 기준
        public string AmmoName;
        public string TriggerLabel; // 발동 트리거(첫/막/N) — 없으면 ""
        public bool IsEmpty;

        public readonly float Total => Kind == LabSampleKind.Dot ? Dot : Physical + ElementalHit;
        /// <summary>특수상황 기여분 = (물리+속성즉발) − 조건무 베이스. (DAMAGE_LAB §1 보너스 차분)</summary>
        public readonly float Bonus => Kind == LabSampleKind.Dot ? 0f : (Physical + ElementalHit) - BaseValue;
    }

    /// <summary>턴(탄창) 단위 집계 — 텍스트 요약·그래프 그룹용.</summary>
    public struct LabTurnRow
    {
        public int Turn;
        public float Immediate;  // 그 턴 즉발 합 (탄들)
        public float Dot;        // 그 턴 DoT 틱
        public float CumAtEnd;   // 턴 종료 시 누적
    }

    /// <summary>한 프리셋의 계산 결과 — 윈도우(텍스트/그래프)가 그대로 바인딩.</summary>
    public sealed class LabSeries
    {
        public readonly List<LabSample> Samples = new List<LabSample>();    // 전 막대 (탄+DoT), 시간순
        public readonly List<float> Cumulative = new List<float>();        // Samples와 1:1 누적
        public readonly List<LabTurnRow> TurnRows = new List<LabTurnRow>();

        public float PerShotAvg;  // 평균 탄당 (1턴 즉발 ÷ 탄창크기)
        public float PerTurn;     // 턴당 즉발 합 (1턴 기준)
        public float StageTotal;  // 예상 총 = 최종 누적
        public float Required;
        public bool Cleared;
        public int ClearedTurn;   // 도달 턴 (0 = 미달)
        public int Turns;         // 탄창 수
        public int MagazineSize;
        public float MaxBar;      // 막대 최댓값 (좌축 스케일 — M2)
        public bool HasGun;       // 총기 미지정 = false
    }

    /// <summary>실험 델타 — SO 무수정 임시 가산값(목표 도달 탐색, DAMAGE_LAB §6.6).</summary>
    public struct LabDelta
    {
        public float FlatAdd;          // 가산 +
        public float IncreasedAdd;     // 증가% (0.2 = +20%)
        public float MoreAdd;          // 승산% (0.5 = ×1.5)
        public float CritChanceAdd;    // 크리율 +
        public float CritMultAdd;      // 크리배율 +
        public int MagazineCountAdd;   // 탄창수(턴) +
        public int MultiShotAdd;       // 멀티샷 펠릿 + (M2 세션3 — §2 총 탄 수 축)
        public float StatusIntensityAdd; // 상태이상 강도% (0.5 = +50%) — DoT 부여량·감전 증폭만 (M2 세션3, §4.3)

        public readonly bool Any =>
            FlatAdd != 0f || IncreasedAdd != 0f || MoreAdd != 0f ||
            CritChanceAdd != 0f || CritMultAdd != 0f || MagazineCountAdd != 0 ||
            MultiShotAdd != 0 || StatusIntensityAdd != 0f;
    }

    /// <summary>분석 프리셋 입력 — 총기 + 탄창 시퀀스 + 장착 모드 + 실험 델타. (악세는 후속 M)</summary>
    public sealed class LabPreset
    {
        public string Name = "A";
        public bool Enabled = true;
        public GunDataSO Gun;
        public readonly List<AmmoDataSO> Sequence = new List<AmmoDataSO>(); // 칸별 탄종 (null = 빈 칸)
        public readonly List<ModDataSO> Mods = new List<ModDataSO>();
        public LabDelta Delta;
    }
}
