using NightView.Utils;

namespace NightView.Gameplay
{
    /// <summary>
    /// 전투 전역 상태 — 양 진영 보드·턴·시드 RNG 보유. 순수 C#(ⓒ).
    /// 무작위(EnemyRandom·DrawRandom·선공)는 전부 Rng 경유 → 시드로 재현 가능(ⓑ).
    /// </summary>
    public class BattleContext
    {
        /// <summary>교착 방지 턴 상한(행동 기준 — 1턴 = 한 진영 1행동). 도달 시 총 HP 높은 쪽 승.</summary>
        public const int MAX_TURNS = 30;

        public BoardSide Player { get; }
        public BoardSide Enemy { get; }
        public IRng Rng { get; }
        public int Seed { get; }

        public Side CurrentTurn;
        public int TurnCount;   // 누적 행동 수(양측 합산) — 1턴 = 1행동
        public BattleOutcome Outcome;   // CombatResolver 판정 결과 (기본 Ongoing)

        private int _nextInstanceId = 1;

        public BattleContext(IRng rng, int seed, Side firstTurn = Side.Player)
        {
            Rng = rng;
            Seed = seed;
            Player = new BoardSide(Side.Player);
            Enemy = new BoardSide(Side.Enemy);
            CurrentTurn = firstTurn;
            TurnCount = 0;
            Outcome = BattleOutcome.Ongoing;
        }

        public BoardSide Of(Side side) => side == Side.Player ? Player : Enemy;
        public BoardSide Opponent(Side side) => side == Side.Player ? Enemy : Player;

        /// <summary>InstanceId → 카드 역참조(필드 우선, 대기열 포함). 뷰·입력 레이어가 ID로 카드를 찾을 때 사용.</summary>
        public CardInstance FindCard(int instanceId)
        {
            if (instanceId < 0) return null;
            return FindIn(Player, instanceId) ?? FindIn(Enemy, instanceId);
        }

        private static CardInstance FindIn(BoardSide board, int instanceId)
        {
            for (int i = 0; i < BoardSide.SLOT_COUNT; i++)
            {
                if (board.Slots[i] != null && board.Slots[i].InstanceId == instanceId) return board.Slots[i];
            }
            foreach (var c in board.Bench)
                if (c.InstanceId == instanceId) return c;
            return null;
        }

        /// <summary>턴 상한 도달 — 교착 종료 판정 시점.</summary>
        public bool ReachedTurnLimit => TurnCount >= MAX_TURNS;

        /// <summary>전투 시작 셋업에서 카드별 고유 InstanceId 발급.</summary>
        public int NextInstanceId() => _nextInstanceId++;
    }
}
