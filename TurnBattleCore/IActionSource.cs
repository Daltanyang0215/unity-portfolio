using System.Threading;
using Cysharp.Threading.Tasks;

namespace NightView.Gameplay
{
    /// <summary>
    /// 입력 소스 추상화(ARCHITECTURE §3.4) — 한 진영의 행동(ActionRequest)을 비동기로 제공한다.
    /// 구현: LocalInputSource(드래그·드롭 UI) / AIActionSource(AIController 래핑) / [확장]RemoteActionSource(미구현).
    /// BattleManager 루프는 소스 종류와 무관하게 GetActionAsync만 호출 → 온라인 전환 시 루프 무변경.
    /// </summary>
    public interface IActionSource
    {
        UniTask<ActionRequest> GetActionAsync(BattleContext ctx, Side side, CancellationToken ct);
    }
}
