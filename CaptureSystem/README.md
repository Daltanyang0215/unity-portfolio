# Camera Capture System

**프로젝트**: 파파라치 (개인 프로젝트)
**역할**: 설계 및 전체 구현

플레이어가 특정 화면 영역을 촬영하고, 촬영된 프레임 내부에 포함된 오브젝트를 판별하여 게임 플레이 결과 데이터로 변환하기 위한 캡처 시스템입니다.

단순 화면 저장이 아닌, 촬영 범위 내 월드 오브젝트를 분석하여 게임 판정에 사용되는 CaptureData를 생성하도록 설계되었습니다.

---

## 설계 목표

- 화면 캡처와 게임 판정 로직 통합
- 카메라 출력과 실제 게임 상태 분리
- Isometric 카메라 환경에서 정확한 영역 판별
- 캡처 결과 데이터 구조화
- GPU 메모리 명시적 관리

---

## 전체 동작 흐름

```
플레이어 입력
      ↓
카메라 RenderTexture 캡처
      ↓
캡처 영역 월드 좌표 변환
      ↓
프레임 내부 MapActor 판별
      ↓
CaptureData 생성 및 저장
      ↓
일일 촬영 진행 상태 갱신
```

---

## 핵심 구성 요소

### 1. PlayerCameraCapture

캡처 전체 흐름을 담당하는 시스템 컴포넌트입니다.

- 플레이어 입력 기반 촬영 수행
- RenderTexture를 이용한 프레임 캡처
- 촬영 영역 월드 좌표 변환
- 포함 오브젝트 판별
- CaptureData 생성 및 관리
- 일일 촬영 횟수 관리

### 2. CaptureData

한 장의 사진 결과를 저장하는 데이터 구조입니다.

| 필드 | 설명 |
|------|------|
| `CaptureSprite` | 촬영 이미지 |
| `CaptureActors` | 포함된 오브젝트 목록 |
| `CaptureOffenseTypes` | 개별 위반 정보 |
| `CaptureAllOffense` | 통합 위반 플래그 (`[Flags]` 열거형) |

여러 위반 유형을 `[Flags]` 열거형으로 누적하여 특정 위반 포함 여부를 단일 비교로 확인할 수 있도록 구성했습니다.

---

## RenderTexture를 사용한 이유

일반 Screen Capture 대신 RenderTexture를 사용했습니다.

- 실제 화면 출력에 영향을 주지 않고 특정 프레임만 캡처
- 카메라 렌더링을 수동 제어
- UI 포함 여부를 독립적으로 제어 가능

`Camera.targetTexture`를 교체 후 `Render()`를 호출하여 현재 프레임을 별도 텍스처로 기록하는 방식으로 구현했습니다.

---

## Isometric 좌표 보정

카메라가 기울어진 Isometric 환경에서는 `ScreenToWorldPoint`만으로 정확한 영역 계산이 어렵습니다.

이를 해결하기 위해:
- `ScreenPointToRay`를 사용하여 Ray 생성
- Ray origin 기반 월드 좌표 계산
- `worldZ = origin.z + origin.y` 보정 적용

화면 영역을 월드 공간 사각 영역으로 변환하여 정확한 오브젝트 포함 여부를 판별하도록 구성했습니다.

---

## 오브젝트 판별 방식

캡처 영역을 월드 좌표 사각형으로 변환한 후, 등록된 MapActor 목록을 순회하며 위치 기반 포함 여부를 검사합니다.

- Physics 의존 없이 판별 가능
- Layer 설정 영향 제거
- 판정 로직 단순화

---

## 메모리 관리

`Texture2D` 및 `Sprite`는 Unity 네이티브 리소스이므로 GC 대상이 아닙니다.
`DailyStart` 시점에 `Sprite.texture`와 `Sprite`를 명시적으로 `Destroy` 호출하여 GPU 메모리 누수를 방지하도록 구성했습니다.
