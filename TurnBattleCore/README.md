# Turn Battle Core

**프로젝트**: TurnCard (개인 프로젝트 — 세로형 턴제 카드 배틀, Android)
**역할**: 설계 및 전체 구현

전투 로직(코어)과 Unity 표현(뷰)을 완전히 분리한 턴제 전투 시스템입니다.

코어는 `UnityEngine` 참조가 없는 순수 C#으로 작성되어, **연출이 게임 상태에 영향을 줄 수 없는** 구조를 만들었습니다. 코어와 뷰가 주고받는 데이터는 전부 `int` 기반 ID로만 표현되므로, 그대로 직렬화·전송이 가능합니다.

---

## 설계 목표

- 연출(뷰)이 게임 판정(코어)에 개입할 수 없는 단방향 구조
- 입력 소스(로컬 / AI / 원격)가 바뀌어도 전투 루프는 변경 없음
- 시드만으로 전투 전체를 재현 가능 — 버그 재현·리플레이 대응
- 검증 책임의 소재를 코어 안팎으로 명확히 분리

---

## 전체 구조

```
IActionSource (Local / AI / [확장]Remote)
      ↓ ActionRequest (ID 기반)
ActionValidator          ← 코어 밖 검증 게이트
      ↓
CombatResolver           ← 순수 C# 코어 (진입점)
      ↓ List<BattleEvent> (ID 기반)
BattleManager
      ↓
BattleView               ← 이벤트 순차 재생 (연출 전용)
```

코어는 뷰를 전혀 알지 못하며, 뷰는 이벤트 목록을 받아 **재생만** 합니다.

---

## 핵심 설계

### 1. ID 기반 단방향 이벤트 — `BattleEvent`

코어는 오브젝트 참조 대신 `InstanceId`만 담은 이벤트 구조체 목록을 반환합니다.

```csharp
public struct BattleEvent
{
    public BattleEventType Type;
    public int SourceId;      // 시전/공격자 InstanceId
    public int TargetId;      // 대상 InstanceId
    public int Value;         // 피해/힐량
    public StatusType Status;
}
```

> **설계 의도**: 참조가 없으므로 이벤트 목록을 그대로 직렬화·전송·저장할 수 있습니다. 리플레이 저장이나 서버 측 재검증으로 확장할 때 코어를 수정할 필요가 없습니다.

뷰는 `InstanceId → CardView` 매핑으로 역참조해 연출을 순차 재생합니다.

### 2. 입력 소스 추상화 — `IActionSource`

```csharp
public interface IActionSource
{
    UniTask<ActionRequest> GetActionAsync(BattleContext ctx, Side side, CancellationToken ct);
}
```

`BattleManager`의 전투 루프는 소스 종류를 알지 못하고 `GetActionAsync`만 호출합니다.

| 구현 | 용도 |
|------|------|
| `LocalInputSource` | 플레이어 — 카드 탭 선택 → 드래그 타게팅 → 드롭 실행 |
| `AIActionSource` | AI 대전 |
| `RemoteActionSource` | 온라인 확장 지점 (미구현) |

> **설계 의도**: 온라인 대전으로 전환하더라도 소스 구현체 하나만 추가하면 되고, 루프·코어·뷰는 손대지 않습니다. `ActionRequest` 역시 ID 기반이라 그대로 네트워크 전송이 가능합니다.

### 3. 시드 기반 재현성 — `BattleContext`

무작위 요소(대상 추첨, 덱 드로우, 선공 결정)는 전부 `ctx.Rng`를 경유합니다. 직접 `UnityEngine.Random`을 호출하는 지점이 없으므로 **시드값만으로 전투 전체가 재현**됩니다.

> **설계 의도**: 밸런스 검증과 버그 재현 때문입니다. 개발 빌드에서는 고정 시드 + 양측 AI 모드로 오토배틀을 돌려 결과를 관찰할 수 있습니다.

### 4. 검증 책임 분리 — `ActionValidator`

코어(`CombatResolver`)는 "적법한 요청"을 전제로 동작합니다. 액터의 적법성 검증은 코어 **밖**의 게이트가 담당합니다.

| 검증 위치 | 대상 |
|-----------|------|
| `ActionValidator` (코어 밖) | 액터 진영 소속 · 생존 · 비기절 · 스킬 보유 → 위반 시 Pass로 강등 |
| `TargetResolver` (코어 안) | 대상 진영 보정 · 도발 강제 · 무효 대상 |

> **설계 의도**: 코어가 기절 액터를 막지 않기 때문에 게이트가 반드시 선행해야 합니다. 이 경계를 흐리면 같은 검증이 두 곳에 중복되고, 한쪽만 고치는 버그가 생깁니다. 코어 진입 직전 한 곳에서만 걸러내도록 고정했습니다.

---

## 전투 규칙 요약

게임의 핵심 규칙은 **공격력 = 현재 HP**입니다.

- 피해 = 공격자의 현재 HP
- 반격 = 대상의 현재 HP
- 둘 다 **공격 전 HP 기준으로 동시 적용** (선후 관계 없음)

때릴수록 자신도 약해지므로 매 턴 트레이드 판단이 발생합니다. 무반격(원거리)·스플래시·무적·기절 같은 예외는 `CombatCalculator`가 단일 지점에서 처리합니다.

---

## 파일 구성

| 파일 | 계층 | 설명 |
|------|------|------|
| `CombatResolver.cs` | 코어 | 행동 1건 해소 → 이벤트 생성. 사망 수거·충원·승패 판정까지 일괄 |
| `CombatCalculator.cs` | 코어 | 기본공격 계산 + 데미지 프리뷰 |
| `TargetResolver.cs` | 코어 | 대상 키워드 → 실제 카드 목록 해소 |
| `EffectExecutor.cs` | 코어 | 스킬 효과 실행 |
| `StatusSystem.cs` | 코어 | 기절·무적 부여 및 수명 관리 |
| `BattleContext.cs` | 코어 | 전투 전역 상태 · 시드 RNG 보유 |
| `CardInstance.cs` | 코어 | 카드 런타임 인스턴스 |
| `BattleEvent.cs` | 계약 | 코어 → 뷰 단방향 이벤트 |
| `ActionRequest.cs` | 계약 | 입력 → 코어 진입 형식 |
| `IActionSource.cs` | 계약 | 입력 소스 추상화 |
| `ActionValidator.cs` | 게이트 | 코어 진입 전 액터 적법성 검증 |
| `BattleManager.cs` | 조율 | 전투 루프 · 코어 호출 → 뷰 재생 |
| `LocalInputSource.cs` | 뷰 | 플레이어 입력 구현 (드래그 타게팅) |
