# Minigame Framework

**프로젝트**: Mr.Kim (개인 프로젝트 — 미니게임 기반 근무 시뮬레이션)
**역할**: 설계 및 전체 구현

성격이 전혀 다른 미니게임들을 **하나의 규약**으로 붙이기 위한 확장 프레임워크입니다.

여러 미니게임이 한 화면에 동시에 여러 개 떠 있고, 각각이 독립적으로 진행·성공·실패하는 구조라 개별 미니게임이 자기 수명을 직접 관리하면 상태가 엉킵니다. 그래서 **수명과 시간 흐름의 소유권을 베이스로 올리고**, 구현체는 게임 내용만 채우도록 분리했습니다.

---

## 설계 목표

- 미니게임 추가 시 작성해야 할 코드를 3개 메서드로 한정
- 개별 미니게임이 자기 `Update`·풀 반환·연출 순서를 관리하지 않도록
- 오브젝트 풀 재사용 시 이전 상태(구독·트윈·토큰)가 남지 않도록
- 라운드 강제 종료 시 연출 유무를 호출자가 선택 가능하도록

---

## 핵심 규약

### 1. `Update()` 금지 — `Tick(dt)` 주입

미니게임 구현체는 **자체 `Update()`를 갖지 않습니다.** 관리자가 활성 미니게임에만 `Tick(dt)`를 호출합니다.

```csharp
public void Tick(float dt)
{
    if (!IsActive) return;
    OnTick(dt);
}
```

> **설계 의도**: 미니게임이 각자 `Update`를 돌면 일시정지·라운드 종료 시점에 개별적으로 멈춰야 하고, 하나라도 빠뜨리면 종료된 미니게임이 계속 돕니다. 시간 흐름의 소유권을 호출자에게 몰아 이 문제를 제거했습니다. 활성 미니게임 수만큼만 순회하므로 비활성 인스턴스의 `Update` 오버헤드도 없습니다.

### 2. 구현체가 채우는 것은 3개뿐

```csharp
protected abstract void OnBegin();          // 문제 생성 · 초기 배치
protected abstract void OnTick(float dt);   // 진행 로직
protected abstract void OnEnd(bool success);// 정리
```

성공/실패 판정은 구현체가 `Succeed()` / `Fail()`을 호출하는 것으로 끝나고, 이후 연출·이벤트·풀 반환은 전부 베이스가 처리합니다.

### 3. 생명주기

```
MinigameRegistry.Instantiate()
      ↓
Initialize(data, stage, panelIndex)   스펙 주입
      ↓
Begin()                               등장 연출 + IsActive = true
      ↓
Tick(dt) × N                          호출자가 매 프레임 주입
      ↓
Succeed() / Fail()  또는  ForceEnd(success, silent)
      ↓
EndSequenceAsync                      결과 연출 → 퇴장 연출 → 이벤트 발행
      ↓
ReturnToPool()                        구독 해제 후 풀 반환
```

### 4. CancellationTokenSource 이중 관리

토큰을 **진행용과 종료용으로 분리**했습니다.

| 토큰 | 수명 |
|------|------|
| `_cts` | 게임 진행 중. 종료 시점에 취소 |
| `_endCts` | 종료 연출 전용. `destroyCancellationToken`과 링크 |

> **설계 의도**: 종료 연출은 `_cts`가 취소된 **이후에** 실행되어야 하므로 같은 토큰을 쓸 수 없습니다. 그렇다고 토큰 없이 돌리면 연출 도중 오브젝트가 파괴될 때 예외가 납니다. `destroyCancellationToken`과 링크해 파괴 시 자동으로 끊기도록 했습니다.

### 5. 풀 재사용 안전장치

```csharp
public override void ReturnToPool()
{
    OnSucceeded = null;   // 구독 해제 — 남으면 다음 사용자에게 이전 콜백이 발화
    OnFailed = null;
    base.ReturnToPool();
}
```

DOTween은 전부 `SetLink(gameObject)`로 걸어 오브젝트 수명과 트윈 수명을 묶었습니다.

> **설계 의도**: 풀링 오브젝트에서 가장 흔한 버그가 이전 사용자의 구독과 트윈이 남는 것입니다. 반환 시점 한 곳에서 강제로 끊도록 했습니다.

### 6. 강제 종료의 연출 분기 — `ForceEnd(success, silent)`

라운드 종료로 일괄 정리할 때는 `silent = true`로 즉시 정리하고, 개별 종료는 `silent = false`로 연출을 태웁니다. 호출자가 상황에 맞게 고를 수 있습니다.

---

## 수록 구현체

| 미니게임 | 입력 방식 |
|----------|-----------|
| `WireConnectMinigame` | 드래그로 같은 색 단자 연결 |
| `ColorSequenceMinigame` | 제시된 색 순서 기억 후 재입력 |

두 게임은 입력 방식도 진행 규칙도 다르지만, 베이스에 대해서는 동일한 3개 메서드만 구현합니다. 오답 시에는 게임을 끝내지 않고 `ShakeOnMistake()`로 시각 피드백만 주는 공통 도우미를 사용합니다.

원 프로젝트에는 이 외 3종(풍선 터트리기 · 색 선택 · 버튼 연타)이 더 있으며, 모두 같은 규약을 따릅니다.

---

## 파일 구성

| 파일 | 설명 |
|------|------|
| `BaseMinigame.cs` | 추상 베이스 — 생명주기 · 토큰 · 연출 · 풀 반환 |
| `MinigameRegistry.cs` | SO 조회 + 인스턴스 생성 전용 (런타임 상태 보유 금지) |
| `PanelFrame.cs` | 패널 공통 프레임 — 게이지 · 포커스 · 성공/실패 아웃트로 |
| `WireConnectMinigame.cs` / `WireEndPoint.cs` | 전선 연결 구현체 |
| `ColorSequenceMinigame.cs` | 색 순서 기억 구현체 |
