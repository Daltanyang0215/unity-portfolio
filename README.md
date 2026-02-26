# Unity Portfolio Code

이정현 | Unity 클라이언트 프로그래머

실무 및 개인 프로젝트에서 작성한 코드 중, 설계 의도를 잘 담고 있는 시스템을 선별하여 정리한 포트폴리오입니다.

- **GitHub**: [Daltanyang0215](https://github.com/Daltanyang0215)
- **Portfolio**: [Notion](https://satisfying-hill-813.notion.site/3658af61fa204514b09d4c0b9f8a8452)

---

## 수록 코드

### [RedDotSystem](./RedDotSystem/)

**출처**: 픽셀소녀키우기 (ZTO Games)

트리 구조 기반 알림 UI 관리 시스템입니다.
상태 관리와 UI 표시를 분리하고, Leaf 노드 변경만으로 상위 메뉴 알림까지 자동 전파되는 구조로 설계했습니다.
콘텐츠 Unlock 상태와 연동된 알림 지연 처리도 포함합니다.

| 파일 | 설명 |
|------|------|
| `RedDotManager.cs` | 트리 구성, 상태 변경, 이벤트 발행 |
| `RedDot.cs` | UI 컴포넌트, 이벤트 구독 및 표시 갱신 |

---

### [SaveSystem](./SaveSystem/)

**출처**: 파파라치 (개인 프로젝트)

3슬롯 독립 세이브 구조와 정산 시점 한정 저장 방식을 적용한 세이브 시스템입니다.
게임 진행 구조에 맞춰 저장 범위를 최소화하고, 분기별 진행 상태를 유지할 수 있도록 설계했습니다.

| 파일 | 설명 |
|------|------|
| `SaveSystem.cs` | 슬롯 관리, 저장/불러오기 처리 |
| `SaveData.cs` | 세이브 데이터 구조체 정의 |

---

### [CaptureSystem](./CaptureSystem/)

**출처**: 파파라치 (개인 프로젝트)

플레이어 촬영 입력을 게임 판정 데이터로 변환하는 캡처 시스템입니다.
RenderTexture 기반 캡처, Isometric 좌표 보정, Physics 미의존 오브젝트 판별, GPU 메모리 명시적 관리를 포함합니다.

| 파일 | 설명 |
|------|------|
| `CameraCapture.cs` | 캡처 흐름 전체 담당, CaptureData 생성 |
