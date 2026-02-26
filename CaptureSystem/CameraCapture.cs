using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RenderTexture 기반 카메라 캡처 시스템
///
/// 플레이어가 화면의 특정 위치를 클릭하면 그 위치를 중심으로 사진을 촬영하고,
/// 촬영된 프레임 안에 포함된 오브젝트(MapActor)를 판별하여 CaptureData로 저장합니다.
/// </summary>
public class PlayerCameraCapture : MonoBehaviour
{
    #region Inspector

    [SerializeField] private byte _maxCaptureCount;

    // 원본: MapManager.Instance.Actors 직접 참조
    // 변경: Inspector 또는 외부 주입으로 교체 (싱글톤 의존 제거)
    [SerializeField] private List<MapActor> _actors;

    #endregion

    #region Properties

    public byte CurCaptureCount { get; private set; }
    public byte MaxCaptureCount => _maxCaptureCount;
    public bool IsGoalCaptureCount => CurCaptureCount >= _maxCaptureCount;
    public List<CaptureData> Captures { get; private set; } = new List<CaptureData>();

    #endregion

    #region Events

    public Action CaptureAction;
    public Action DayEndAction;

    #endregion

    private Camera _camera;

    // ─────────────────────────────────────────────
    #region Unity Events

    private void Start()
    {
        _camera = Camera.main;
        CaptureAction += CheckDailyFinish;
    }

    #endregion

    // ─────────────────────────────────────────────
    #region Public Methods

    public void CameraCapture(Vector2 mousePos)
    {
        if (CurCaptureCount >= _maxCaptureCount) return;
        CurCaptureCount++;

        // 캡처 프레임 크기 (전체 화면에서 잘라낼 영역)
        Rect angleRect = new Rect(0, 0, 720, 480);

        // ── RenderTexture로 현재 카메라 뷰를 텍스처에 기록 ─────────────────────
        // Camera.targetTexture를 교체한 뒤 Render()를 수동 호출하면
        // 기존 화면 출력에 영향 없이 해당 프레임만 RenderTexture로 분리하여 렌더링됩니다.
        RenderTexture render = new RenderTexture(Screen.width, Screen.height, 24);
        Texture2D texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);

        _camera.targetTexture = render;
        _camera.Render();

        // ReadPixels: RenderTexture.active로 지정된 GPU 버퍼에서 CPU 메모리(Texture2D)로 픽셀을 복사합니다.
        RenderTexture.active = render;
        texture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        texture.Apply();

        // 렌더 대상 원복 후 RenderTexture 즉시 해제 (GPU 메모리 반환)
        _camera.targetTexture = null;
        RenderTexture.active = null;
        Destroy(render);

        // 마우스 클릭 위치를 중심으로 angleRect 크기만큼 잘라낸 Sprite 생성
        Sprite sprite = Sprite.Create(texture,
            new Rect(Mathf.RoundToInt(mousePos.x) - angleRect.size.x * 0.5f,
                     Mathf.RoundToInt(mousePos.y) - angleRect.size.y * 0.5f,
                     720,
                     480), Vector2.zero);

        // ── 캡처 프레임 내 오브젝트 감지 ─────────────────────────────────────
        List<MapActor> mapActors = new List<MapActor>();
        List<OffenseType> mapOffense = new List<OffenseType>();

        // ScreenPointToRay: 화면의 2D 좌표를 3D Ray로 변환합니다.
        // ScreenToWorldPoint 대신 사용하는 이유:
        // Ray.origin에 카메라의 뷰 공간 오프셋이 포함되어 있어
        // 기울어진 Isometric 뷰에서도 정확한 월드 좌표를 계산할 수 있습니다.
        Ray cameraPosLB = Camera.main.ScreenPointToRay(mousePos - angleRect.size * .5f);
        Ray cameraPosRT = Camera.main.ScreenPointToRay(mousePos + angleRect.size * .5f);

        // Isometric 뷰 좌표 보정: 카메라가 대각선으로 기울어져 있으므로
        // 화면상 Y 오프셋이 월드 Z 좌표에 합산됩니다. (worldZ = origin.z + origin.y)
        Vector3 worldPosLB = new Vector3(cameraPosLB.origin.x, 0, cameraPosLB.origin.z + cameraPosLB.origin.y);
        Vector3 worldPosRT = new Vector3(cameraPosRT.origin.x, 0, cameraPosRT.origin.z + cameraPosRT.origin.y);

        foreach (MapActor mapActor in _actors)
        {
            if (mapActor.ModelTransform.position.x > worldPosLB.x &&
                mapActor.ModelTransform.position.x < worldPosRT.x &&
                mapActor.ModelTransform.position.z + mapActor.ModelTransform.position.y > worldPosLB.z &&
                mapActor.ModelTransform.position.z + mapActor.ModelTransform.position.y < worldPosRT.z)
            {
                mapActors.Add(mapActor);
                mapOffense.Add(mapActor.Offense);
            }
        }

        Captures.Add(new CaptureData(sprite, mapActors, mapOffense));

        CaptureAction?.Invoke();
    }

    /// <summary>
    /// 캡처 목록 초기화 및 메모리 해제
    /// Sprite / Texture2D는 UnityEngine.Object를 상속하므로 C# GC 대상이 아닙니다.
    /// GPU/네이티브 메모리는 Destroy()를 명시적으로 호출해야 해제됩니다.
    /// </summary>
    public void DailyStart()
    {
        CurCaptureCount = 0;

        foreach (var capture in Captures)
        {
            if (capture.CaptureSprite != null)
            {
                if (capture.CaptureSprite.texture != null)
                    Destroy(capture.CaptureSprite.texture);
                Destroy(capture.CaptureSprite);
            }
        }

        Captures.Clear();
    }

    #endregion

    // ─────────────────────────────────────────────
    #region Private Methods

    private void CheckDailyFinish()
    {
        if (CurCaptureCount != _maxCaptureCount) return;
        DayEndAction?.Invoke();
    }

    #endregion
}

// ─────────────────────────────────────────────────────────────

/// <summary>
/// 한 장의 캡처에 대한 결과 데이터
/// </summary>
[Serializable]
public class CaptureData
{
    public CaptureData(Sprite captureSprite, List<MapActor> captureActors, List<OffenseType> offenseTypes)
    {
        CaptureSprite = captureSprite;
        CaptureActors = captureActors;
        CaptureOffenseTypes = offenseTypes;

        // [Flags] 열거형으로 프레임 내 전체 위반 유형을 비트 OR로 누적합니다.
        // 나중에 "이 사진에 특정 위반이 포함되어 있는가?"를 단일 비교로 확인하기 위함입니다.
        foreach (OffenseType offense in offenseTypes)
            CaptureAllOffense |= offense;
    }

    [field: SerializeField] public Sprite CaptureSprite { get; private set; }
    [field: SerializeField] public List<MapActor> CaptureActors { get; private set; }
    [field: SerializeField] public List<OffenseType> CaptureOffenseTypes { get; private set; }

    // 이 사진에 포함된 모든 위반 유형 (비트 플래그)
    public OffenseType CaptureAllOffense { get; private set; }
}
