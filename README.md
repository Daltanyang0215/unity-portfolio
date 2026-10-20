# Unity Portfolio Code

이정현 | Unity 클라이언트 프로그래머

실무 및 개인 프로젝트에서 작성한 코드 중, 설계 의도를 잘 담고 있는 시스템을 선별하여 정리한 포트폴리오입니다.

- **GitHub**: [Daltanyang0215](https://github.com/Daltanyang0215)
- **Portfolio**: [daltanyang0215.github.io](https://daltanyang0215.github.io)

---

## 수록 코드

| 분류 | 시스템 | 출처 |
|------|--------|------|
| 아키텍처 | [MinigameFramework](./MinigameFramework/) | Mr.Kim |
| 검증 · 툴링 | [DamageEngine](./DamageEngine/) | GunRogue |
| 검증 · 툴링 | [DamageLabEditor](./DamageLabEditor/) | GunRogue |
| 서비스 운영 | [RemoteSpecSystem](./RemoteSpecSystem/) | IdleMineForge |
| UI · 데이터 | [RedDotSystem](./RedDotSystem/) | 픽셀소녀키우기 (ZTO Games) |
| UI · 데이터 | [SaveSystem](./SaveSystem/) | 파파라치 |
| 기술 구현 | [CaptureSystem](./CaptureSystem/) | 파파라치 |
| 기술 구현 | [MeshForgeSystem](./MeshForgeSystem/) | Forest-Blacksmith |

---

## 아키텍처

### [MinigameFramework](./MinigameFramework/)

**출처**: Mr.Kim (미니게임 기반 근무 시뮬레이션)

성격이 다른 미니게임들을 하나의 규약으로 붙이기 위한 확장 프레임워크입니다.
구현체의 자체 `Update()`를 금지하고 관리자가 `Tick(dt)`를 주입하는 구조로, 시간 흐름과 수명의 소유권을 베이스로 올렸습니다. CancellationTokenSource를 진행용/종료 연출용으로 이중 관리하고, 풀 반환 시점에 구독과 트윈을 강제로 끊습니다.

| 파일 | 설명 |
|------|------|
| `BaseMinigame.cs` | 추상 베이스 — 생명주기 · 토큰 · 연출 · 풀 반환 |
| `MinigameRegistry.cs` | SO 조회 + 인스턴스 생성 전용 |
| `WireConnectMinigame.cs` / `ColorSequenceMinigame.cs` | 구현체 2종 |

---

## 검증 · 툴링

### [DamageEngine](./DamageEngine/)

**출처**: GunRogue (총기 조합 로그라이크)

데미지 공식 엔진과, 그 수치를 Unity 없이 자동 검증하는 콘솔 도구 2종입니다.
엔진을 `UnityEngine` 비의존으로 작성해 런타임·에디터 툴·콘솔 하네스가 같은 코드를 공유합니다. 회귀 하네스는 데미지·경제·상점·보스를 검사하며(현재 약 510건 · 수록본은 395건 시점의 사본), 기대값을 밸런스 상수에서 파생시켜 수치 변경이 자동 반영됩니다. 조합 탐색 랩은 전수 불가능한 조합 공간을 빔 서치로 훑어 밸런스 사각을 찾습니다.

| 파일 | 설명 |
|------|------|
| `DamageCalculator.cs` | 데미지 공식 엔진 (순수 로직) |
| `DamageHarness.Program.cs` | 회귀 검증 하네스 (`dotnet run`) |
| `ComboLab.Program.cs` | 빔 서치 조합 탐색 · `.asset` 직접 파싱 |

### [DamageLabEditor](./DamageLabEditor/)

**출처**: GunRogue

밸런스 수치를 눈으로 보고 조정하기 위한 `EditorWindow` 확장 도구입니다.
게임의 순수 코어를 그대로 호출해 게임과 동일한 수식으로 그리며, 코어는 읽기 전용으로만 사용합니다. MonoBehaviour인 상점 로직은 숨김 GameObject + `SerializedObject` 주입으로 **런타임 코드 수정 없이** 에디터에서 재사용했습니다.

| 파일 | 설명 |
|------|------|
| `DamageLabWindow.cs` | 윈도우 셸 · 반응형 레이아웃 · 3탭 |
| `LabGraph.cs` | 그래프 렌더 (IMGUI + Handles, 이중 Y축) |
| `LabAuto.cs` | 플레이 성향 5종 반복 시뮬 통계 |

---

## 서비스 운영

### [RemoteSpecSystem](./RemoteSpecSystem/)

**출처**: IdleMineForge (방치형 채굴, Android 출시·운영)

앱 업데이트 없이 밸런스를 조정하고 지표를 수집하기 위한 운영 계층입니다.
Remote Config로 버전만 확인해 캐시가 최신이면 Firestore를 조회하지 않고, 원격 실패 시 동봉 CSV로 폴백합니다. Firebase SDK 부재 환경에서도 컴파일이 깨지지 않도록 심볼로 격리했습니다.

| 파일 | 설명 |
|------|------|
| `RemoteSpecLoader.cs` | Remote Config + Firestore · 버전별 캐시 · 폴백 |
| `AnalyticsManager.cs` | Firebase Analytics · 3단계 초기화 · 중복 방지 |
| `PreBuildChecker.cs` | 빌드 전 씬 검증 — 위반 시 빌드 중단 |

---

## UI · 데이터

### [RedDotSystem](./RedDotSystem/)

**출처**: 픽셀소녀키우기 (ZTO Games)

트리 구조 기반 알림 UI 관리 시스템입니다.
상태 관리와 UI 표시를 분리하고, Leaf 노드 변경만으로 상위 메뉴 알림까지 자동 전파되는 구조로 설계했습니다. 콘텐츠 Unlock 상태와 연동된 알림 지연 처리도 포함합니다.

| 파일 | 설명 |
|------|------|
| `RedDotManager.cs` | 트리 구성, 상태 변경, 이벤트 발행 |
| `RedDot.cs` | UI 컴포넌트, 이벤트 구독 및 표시 갱신 |

### [SaveSystem](./SaveSystem/)

**출처**: 파파라치 (개인 프로젝트)

3슬롯 독립 세이브 구조와 정산 시점 한정 저장 방식을 적용한 세이브 시스템입니다.
게임 진행 구조에 맞춰 저장 범위를 최소화하고, 분기별 진행 상태를 유지할 수 있도록 설계했습니다.

| 파일 | 설명 |
|------|------|
| `SaveSystem.cs` | 슬롯 관리, 저장/불러오기 처리 |
| `SaveData.cs` | 세이브 데이터 구조체 정의 |

---

## 기술 구현

### [CaptureSystem](./CaptureSystem/)

**출처**: 파파라치 (개인 프로젝트)

플레이어 촬영 입력을 게임 판정 데이터로 변환하는 캡처 시스템입니다.
RenderTexture 기반 캡처, Isometric 좌표 보정, Physics 미의존 오브젝트 판별, GPU 메모리 명시적 관리를 포함합니다.

| 파일 | 설명 |
|------|------|
| `CameraCapture.cs` | 캡처 흐름 전체 담당, CaptureData 생성 |

> 2D로 계획했으나 애니메이션 작업량 때문에 3D로 전환했고(Paparazzi3D), 캡처 판정도 3D 환경에 맞춰 다시 구현했습니다.

### [MeshForgeSystem](./MeshForgeSystem/)

**출처**: Forest-Blacksmith (개인 프로젝트)

망치질로 금속 주괴의 메시를 실시간 변형하고 목표 형태와의 일치도를 판정하는 시스템입니다.
형상 비교 비용을 낮추기 위해 빠른 버텍스 비교 → 임계값 통과 시에만 표면 샘플링 정밀 비교로 넘어가는 2단계 조기 반환 구조를 적용했습니다. 진행률은 평균이 아닌 최저 진행 버텍스 기준으로 계산해, 모든 면을 고르게 가공해야 완성되도록 했습니다.

| 파일 | 설명 |
|------|------|
| `IngotMeshItem.cs` | 주괴 — 2단계 가공 · 완성 판정 |
| `MeshComparer.cs` | 하이브리드 형상 비교 |
| `MeshUtill.cs` | 하드 엣지 보존 노멀 재계산 |
