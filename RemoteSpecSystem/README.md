# Remote Spec & Live Ops

**프로젝트**: IdleMineForge (개인 프로젝트 — 방치형 채굴, Android 출시 준비)
**역할**: 설계 및 전체 구현

앱 업데이트 없이 밸런스를 조정하고, 지표를 수집하고, 빌드 실수를 사전에 차단하기 위한 **운영(Live Ops) 계층**입니다.

방치형 장르 특성상 출시 후 수치 조정이 잦을 것으로 보고, 스펙 데이터를 클라이언트에서 분리하는 것을 전제로 설계했습니다.

---

## 설계 목표

- 스토어 심사 없이 밸런스 수치 갱신
- 네트워크 실패·SDK 부재 상황에서도 게임이 정상 기동
- 지표 이벤트의 중복 발송 방지
- 사람이 반복해서 실수하는 빌드 체크를 자동화

---

## 전체 구조

```
Firebase Remote Config  ──버전 확인──┐
                                     ├─→ RemoteSpecLoader ─→ 로컬 캐시
Firebase Firestore      ──본문 fetch─┘         │
                                               ↓ (실패 시 폴백)
                                    Resources CSV (동봉본)
                                               ↓
                                       SpecDataManager
                                    Type → (Id → ISpecData)
```

---

## 핵심 설계

### 1. 원격 스펙 로더 — `RemoteSpecLoader`

Remote Config로 **버전만** 먼저 확인하고, 로컬 캐시가 최신이면 Firestore를 아예 읽지 않습니다.

| 특성 | 처리 |
|------|------|
| 버전 격리 | 앱 버전별로 독립된 문서·캐시 사용 → 버전 간 데이터 충돌 없음 |
| 트래픽 절감 | 캐시가 최신이면 Firestore 미조회 |
| 폴백 | 원격 실패 시 `Resources` 동봉 CSV로 기동 |
| 컴파일 격리 | `NIGHTVIEW_FIREBASE` 심볼 미정의 시 파일 전체가 컴파일 제외 |

> **설계 의도**: Firebase SDK가 없는 환경(다른 개발자, CI, 프로젝트 재사용)에서 컴파일이 깨지지 않도록 `#if` 로 파일 전체를 감쌌습니다. 파일 상단에 필요한 사전 작업과 게임별 수정 지점을 주석으로 명시해, 다른 프로젝트로 이식할 때 무엇을 고쳐야 하는지 바로 보이도록 했습니다. 실제로 이 모듈은 이후 다른 프로젝트(GunRogue)에 `Modules/IdleCommon` 패키지로 이식해 재사용했습니다.

### 2. 범용 스펙 테이블 — `SpecDataManager`

```
_tables            : Type → (Id → ISpecData)     범용 테이블
_drawRwdByGroup    : GroupId → 보상 리스트
_mergeRecipeTable  : (key1, key2) 정렬 → resultKey
```

스펙 종류가 늘어도 `ISpecData` 구현체 하나만 추가하면 되고, 조회는 타입 기반으로 통일됩니다. 합성 레시피는 키 쌍을 **정렬해서** 저장하므로 순서가 뒤바뀐 조합도 같은 결과를 찾습니다.

### 3. 지표 수집 — `AnalyticsManager`

Firebase Analytics 연동으로, 초기화를 3단계로 나눴습니다.

1. `Init()` — 의존성 비동기 체크 시작 (게임 로딩을 막지 않음)
2. 전 매니저 초기화 완료 후 → `SetupEventSubscriptions()`
3. 체크 완료 시 → `install_date`(최초 1회) · `first_session_start` 발송

1회성 이벤트는 `PlayerPrefs` 키로 중복 발송을 막습니다. 문자열 결합은 전부 **ZString**을 사용해 GC 할당을 피했습니다.

> **설계 의도**: Firebase 초기화는 수 초가 걸릴 수 있어 로딩 경로에 동기로 물리면 안 됩니다. 구독 등록을 매니저 초기화 이후로 미룬 것도 같은 이유입니다 — 아직 없는 매니저의 이벤트를 구독하면 조용히 누락됩니다.

### 4. 오프라인 보상 — `OfflineRwdManager`

```
totalDps      = Σ (채굴력 / 채굴 간격)      — 장착 슬롯 합산
oresDestroyed = totalDps × 경과초 / 광석 최대HP
coins         = 보상 배율 적용(oresDestroyed × 광석 보상 × 스테이지 배율)
```

실제 채굴 루프를 시뮬레이션하지 않고 **DPS 기반 수식으로 환산**합니다. 순수 C# 싱글톤(MonoBehaviour 아님)으로 작성했습니다.

### 5. 시간 축 분리 — `TimeManager`

`OnSecondTick` / `OnMinuteTick`을 분리해 우선도별로 구독하게 했습니다. 매 프레임 `Update`가 필요 없는 시스템(자동 저장, 통계 갱신 등)이 1분 틱만 구독하도록 해 부하를 줄였습니다.

오프라인 경과 시간은 캡(기본 4시간)을 적용해 반환합니다.

### 6. 빌드 사전 검증 — `PreBuildChecker`

`IPreprocessBuildWithReport`를 구현해, 빌드 시작 시 씬을 검사하고 조건 위반이면 **빌드를 중단**시킵니다.

- 활성 상태여야 하는 오브젝트 (예: 로딩 캔버스)
- 비활성 상태여야 하는 오브젝트 (디버그 패널·테스트 버튼)

> **설계 의도**: 디버그 오브젝트를 켠 채로 빌드해 스토어에 올리는 실수를 실제로 겪은 뒤 추가했습니다. 체크리스트로 관리하면 결국 빠뜨리므로, 빌드 파이프라인이 강제하도록 옮겼습니다.

---

## 파일 구성

| 파일 | 설명 |
|------|------|
| `RemoteSpecLoader.cs` | Remote Config + Firestore 로더 · 버전별 캐시 · 폴백 |
| `SpecDataManager.cs` | 범용 스펙 테이블 · 타입 기반 조회 |
| `AnalyticsManager.cs` | Firebase Analytics · 3단계 초기화 · 중복 방지 |
| `OfflineRwdManager.cs` | 오프라인 보상 환산 (순수 C# 싱글톤) |
| `TimeManager.cs` | 초/분 틱 분리 · 오프라인 경과 시간 캡 |
| `PreBuildChecker.cs` | 빌드 전 씬 검증 (에디터) |
