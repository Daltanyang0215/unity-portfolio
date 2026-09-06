using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class IngotMeshItem : InteractionItemBace
{
    [Header("주괴 속성")]
    public float temperature = 20f;
    public float minWorkTemp = 500f;
    public float maxWorkTemp = 1200f;
    public float durability = 100f;

    [Header("temp")]
    public float furnaceAddTemp = 20f;
    public float waterSubTemp = -10f;
    public float airSubTemp = -1;

    [Header("가공 단계")]
    public bool isShapeDefined = false; // false: 스케일 가공, true: 세부 가공
    public Mesh targetMesh;             // 최종 목표 메쉬
    private Mesh workingMesh;           // 현재 주괴 메쉬
    [SerializeField] private Vector3 minScale = new Vector3(0.1f, 0.1f, 0.1f);// 최소 스케일 값 (축별로 다르게 적용 가능)

    [Header("가공 파라미터")]
    public float deformationSpeed = 0.05f;   // 버텍스 이동 속도
    public float matchThreshold = 0.8f;      // 완성 판정 (80%)
    private MaterialPropertyBlock _materialPropertyBlock;

    private Vector3 baseScale; // 초기 크기 기억
    private MeshFilter _meshFilter; 
    private MeshRenderer _meshRenderer;
    private Vector3[] baseVertices;   // 원본 버텍스
    private int[] baseTriangle;
    private Vector3[] workingVertices;// 가공 중 버텍스
    private Vector3[] targetVertices; // 목표 버텍스 (정규화된 상태)

    private void Start()
    {
        _meshFilter = GetComponentInChildren<MeshFilter>();
        _meshRenderer = GetComponentInChildren<MeshRenderer>();
        workingMesh = Instantiate(_meshFilter.mesh);
        _meshFilter.mesh = workingMesh;
        _materialPropertyBlock = new MaterialPropertyBlock();

        baseScale = transform.localScale;
        baseVertices = workingMesh.vertices;
        baseTriangle = workingMesh.triangles;
        workingVertices = workingMesh.vertices;

        workingMesh.RecalculateHardNormals();
    }

    private void Update()
    {
        CoolDown();
    }

    public void SetTargetMesh(Mesh mesh)
    {
        if (mesh != null)
        {
            targetMesh = Instantiate(mesh); // 최적화를 위해 코드 수정 해야 됨
            targetVertices = NormalizeTargetMesh(targetMesh.vertices, baseVertices);

            workingVertices = baseVertices.ToArray();
        }
        else
        {
            targetMesh = null;
            targetVertices = null;
        }
    }

    // ===== 회전 (주사위 돌리듯 90도씩) =====
    public void RotateIngot(Vector3 axis)
    {
        float bottomY = GetBottomY();

        transform.Rotate(axis, 90f, Space.World);

        float newBottomY = GetBottomY();
        float offset = bottomY - newBottomY;
        transform.position += Vector3.up * offset;

        // 🎬 회전 연출 (돌리는 사운드 / 카메라 움직임)
    }

    // ===== 망치질 =====
    public void ApplyHit(Vector3 hitPoint, float force)
    {
        //if (_environment != EnvironmentState.Anvil) return;

        //if (temperature < minWorkTemp)
        //{
        //    Debug.Log("온도가 낮아 변형 불가, 내구도 감소!");
        //    durability -= force * 0.5f;
        //    // ❄️ 금속이 차가울 때 금속성 '땅' 소리
        //    return;
        //}

        //if (temperature > maxWorkTemp)
        //{
        //    Debug.Log("온도가 너무 높아 내구도 급격히 감소!");
        //    durability -= force * 2f;
        //    // ♨️ 과열 스파크, 붉게 달궈지는 연출
        //}

        if (!isShapeDefined)
        {
            // 1단계: 스케일 가공
            ApplyScaleForging(force);
        }
        else
        {
            // 2단계: 세부 가공
            ApplyDetailForging(force, hitPoint);
        }
    }

    // ===== 1단계: 스케일 가공 =====
    private void ApplyScaleForging(float force)
    {
        float bottomY = GetBottomY();
        Vector3 newScale = transform.localScale;

        // Up 벡터와 가장 가까운 축 찾기
        float dotForward = Mathf.Abs(Vector3.Dot(transform.forward, Vector3.up));
        float dotUp = Mathf.Abs(Vector3.Dot(transform.up, Vector3.up));
        float dotRight = Mathf.Abs(Vector3.Dot(transform.right, Vector3.up));

        // 가장 큰 점곱 찾기
        float maxDot = Mathf.Max(dotForward, dotUp, dotRight);
        int shrinkAxis = maxDot == dotForward ? 2 : maxDot == dotUp ? 1 : 0;

        // 최소 스케일 체크
        if (newScale[shrinkAxis] <= minScale[shrinkAxis] + 0.001f)
        {
            Debug.Log("⚠️ 최소 스케일 도달! 내구도만 감소");
            durability -= force * 0.5f;
            return;
        }

        // 줄이는 축
        newScale[shrinkAxis] = Mathf.Max(newScale[shrinkAxis] - force * 0.01f, minScale[shrinkAxis]);

        // 나머지 두 축 비율대로 늘리기
        float totalOtherScale = 0f;
        for (int i = 0; i < 3; i++)
            if (i != shrinkAxis) totalOtherScale += newScale[i];

        for (int i = 0; i < 3; i++)
        {
            if (i == shrinkAxis) continue;
            float ratio = newScale[i] / totalOtherScale; // 현재 비율
            newScale[i] += force * 0.01f * ratio * 2f; // 두 축 합쳐서 분배
        }

        // 최종 적용
        transform.localScale = newScale;

        // 바닥 보정
        float newBottomY = GetBottomY();
        float offset = bottomY - newBottomY;
        transform.position += Vector3.up * offset;

        // 🔨 타격 이펙트/사운드 포인트
    }

    // 주괴의 바닥 y 좌표 계산 (로컬 버텍스 → 월드 좌표 변환)
    private float GetBottomY()
    {
        Mesh mesh = _meshFilter.sharedMesh;
        float minY = float.MaxValue;

        foreach (var v in mesh.vertices)
        {
            Vector3 worldV = transform.TransformPoint(v);
            if (worldV.y < minY) minY = worldV.y;
        }
        return minY;
    }

    // ===== 2단계: 세부 가공 =====
    private void ApplyDetailForging(float force, Vector3 hitPoint)
    {
        if (targetVertices == null) return;

        Vector3 localHitPoint = transform.InverseTransformPoint(hitPoint);

        for (int i = 0; i < workingVertices.Length; i++)
        {
            float dist = Vector3.Distance(workingVertices[i], localHitPoint);
            if (dist < force * 0.2f) // 타격 범위
            {
                Vector3 newVerticePos = Vector3.Lerp(workingVertices[i], targetVertices[i], 1);
                workingVertices[i] = Vector3.Lerp(
                    workingVertices[i],
                    targetVertices[i],
                    //1
                    deformationSpeed * force
                );
            }
        }
        workingMesh.Clear();
        workingMesh.vertices = workingVertices;
        workingMesh.triangles = baseTriangle;
        workingMesh.RecalculateHardNormals();
        // 모든 노말을 위쪽으로 강제 (예시)
        //Vector3[] normals = new Vector3[workingVertices.Length];
        //for (int i = 0; i < normals.Length; i++)
        //{
        //    normals[i] = Vector3.up;
        //}
        //workingMesh.normals = normals;

        // 🎬 세부 가공 시 금속 갈리는 소리, 작은 스파크
        CheckCompletion();
    }

    // ===== 완성 판정 =====
    private void CheckCompletion()
    {
        if (targetVertices == null) return;

        float match = CalculateMatch(baseVertices, workingVertices, targetVertices);
        Debug.Log($"현재 일치도: {match * 100f:F1}%");

        if (match >= matchThreshold)
        {
            Debug.Log("가공 완료!");
            isShapeDefined = false;

            // 🎬 완성 이펙트 (번쩍임, 카메라 흔들림, '땅' 소리)

            // 최종 아이템으로 교체
            _meshFilter.mesh = targetMesh;
        }
    }

    // ===== 메쉬 일치도 계산 =====
    /// <summary>
    /// 변형된 메쉬의 진행률 중 가장 낮은 비율을 계산합니다.
    /// </summary>
    /// <param name="original">변형 전 원본 버텍스 배열</param>
    /// <param name="working">현재 변형 중인 버텍스 배열</param>
    /// <param name="target">목표 버텍스 배열</param>
    /// <returns>가장 낮은 진행 비율 (0~1)</returns>
    private float CalculateMatch(Vector3[] original, Vector3[] working, Vector3[] target)
    {
        if (working.Length != target.Length || original.Length != target.Length)
            return 0f;

        float minProgressRatio = 1f; // 제일 낮은 진행률을 추적

        for (int i = 0; i < working.Length; i++)
        {
            Vector3 targetDelta = target[i] - original[i];   // 목표까지의 전체 변화량
            Vector3 currentDelta = working[i] - original[i]; // 현재까지의 변화량

            float denom = targetDelta.sqrMagnitude;
            if (denom < Mathf.Epsilon) continue; // 목표 변화가 없는 점은 무시

            // 현재 진행 비율 (0 ~ 1)
            float progressRatio = Vector3.Dot(currentDelta, targetDelta) / denom;
            progressRatio = Mathf.Clamp01(progressRatio);
            Debug.Log(progressRatio);
            if (progressRatio < minProgressRatio)
            {

                minProgressRatio = progressRatio;
            }
        }

        return minProgressRatio; // 모든 버텍스 중 가장 낮은 진행률 반환
    }

    // ===== 목표 메쉬 정규화 (현재 주괴 크기에 맞게) =====
    private Vector3[] NormalizeTargetMesh(Vector3[] target, Vector3[] baseMesh)
    {
        Bounds baseBounds = new Bounds(Vector3.zero, Vector3.zero);
        foreach (var v in baseMesh) baseBounds.Encapsulate(v);

        Bounds targetBounds = new Bounds(Vector3.zero, Vector3.zero);
        foreach (var v in target) targetBounds.Encapsulate(v);

        float scaleRatio = baseBounds.size.magnitude / targetBounds.size.magnitude;

        Vector3[] normalized = new Vector3[target.Length];
        for (int i = 0; i < target.Length; i++)
        {
            normalized[i] = target[i] * scaleRatio;
        }
        return normalized;
    }

    // ===== 냉각 =====
    private void CoolDown()
    {
        float coolRate = 1f;
        switch (_environment)
        {
            case EnvironmentState.Furnace:
                coolRate = furnaceAddTemp; // 가열
                break;
            case EnvironmentState.Water:
                coolRate = waterSubTemp;
                break;
            case EnvironmentState.Anvil:
                coolRate = airSubTemp;
                break;
            default:
                coolRate = airSubTemp;
                break;
        }

        temperature = Mathf.Clamp(temperature + coolRate * Time.deltaTime, minWorkTemp, maxWorkTemp);

        // 🎬 온도에 따라 쉐이더 색 변화 (붉음 ↔ 어두움)
        _materialPropertyBlock.SetFloat("_TempRate", (temperature - minWorkTemp) / (maxWorkTemp - minWorkTemp));
        _meshRenderer.SetPropertyBlock(_materialPropertyBlock);
    }
}
