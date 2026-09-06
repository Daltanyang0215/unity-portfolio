using UnityEngine;

public static class MeshComparer
{
    /// <summary>
    /// 하이브리드 방식
    /// </summary>
    public static float HybridCompare(Mesh a, Mesh b, float quickThreshold = 0.7f, int sampleCount = 200)
    {
        // 1단계: 빠른 비교
        float quickScore = QuickCompare(a, b);
        if (quickScore < quickThreshold)
        {
            // 빠른 판정에서 이미 낮으면 굳이 정밀 비교 안 함
            return quickScore;
        }

        // 2단계: 정밀 비교
        float preciseScore = CompareMeshesBySampling(a, b, sampleCount);

        // 최종 점수 = 빠른 점수와 정밀 점수를 평균
        return (quickScore + preciseScore) * 0.5f;
    }

    /// <summary>
    /// 빠른 실시간 비교 (Vertex-to-Vertex)
    /// </summary>
    public static float QuickCompare(Mesh a, Mesh b)
    {
        Vector3[] va = a.vertices;
        Vector3[] vb = b.vertices;

        if (va.Length != vb.Length)
            return 0f; // 버텍스 수가 다르면 0점

        float totalDist = 0f;
        for (int i = 0; i < va.Length; i++)
        {
            totalDist += Vector3.Distance(va[i], vb[i]);
        }

        float avgDist = totalDist / va.Length;
        return Mathf.Clamp01(1f - avgDist);
    }

    /// <summary>
    /// 정밀 비교, 두 메쉬의 표면 샘플링 기반 일치도 계산
    /// </summary>
    /// <param name="meshA">현재 메쉬</param>
    /// <param name="meshB">목표 메쉬</param>
    /// <param name="sampleCount">샘플링 포인트 수</param>
    /// <returns>0~1 사이의 일치도</returns>
    public static float CompareMeshesBySampling(Mesh meshA, Mesh meshB, int sampleCount = 200)
    {
        // 메쉬를 동일한 크기(단위 박스)로 정규화
        Mesh normA = NormalizeMesh(meshA);
        Mesh normB = NormalizeMesh(meshB);

        float totalDistance = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            // 표면에 무작위 포인트 추출
            Vector3 samplePoint = GetRandomSurfacePoint(normA);

            // 다른 메쉬 표면까지의 거리 계산
            float distance = ClosestSurfaceDistance(samplePoint, normB);
            totalDistance += distance;
        }

        float avgDist = totalDistance / sampleCount;

        // 평균 거리를 0~1로 변환 (거리가 0이면 1, 거리가 클수록 0에 가까움)
        float score = Mathf.Clamp01(1f - avgDist);
        return score;
    }

    private static Mesh NormalizeMesh(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        Bounds bounds = mesh.bounds;
        Vector3 size = bounds.size;
        float maxSize = Mathf.Max(size.x, size.y, size.z);

        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = (vertices[i] - bounds.center) / maxSize; // 중심 이동 + 크기 정규화
        }

        Mesh newMesh = new Mesh();
        newMesh.vertices = vertices;
        newMesh.triangles = mesh.triangles;
        newMesh.RecalculateNormals();
        newMesh.RecalculateBounds();
        return newMesh;
    }

    private static Vector3 GetRandomSurfacePoint(Mesh mesh)
    {
        int[] tris = mesh.triangles;
        Vector3[] verts = mesh.vertices;

        // 랜덤 삼각형 선택
        int triIndex = Random.Range(0, tris.Length / 3) * 3;

        Vector3 a = verts[tris[triIndex]];
        Vector3 b = verts[tris[triIndex + 1]];
        Vector3 c = verts[tris[triIndex + 2]];

        // 삼각형 내부의 랜덤 좌표 (Barycentric 방식)
        float u = Random.value;
        float v = Random.value;
        if (u + v > 1f) { u = 1f - u; v = 1f - v; }
        return a + u * (b - a) + v * (c - a);
    }

    private static float ClosestSurfaceDistance(Vector3 point, Mesh mesh)
    {
        float minDist = float.MaxValue;
        int[] tris = mesh.triangles;
        Vector3[] verts = mesh.vertices;

        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 a = verts[tris[i]];
            Vector3 b = verts[tris[i + 1]];
            Vector3 c = verts[tris[i + 2]];

            // 점과 삼각형 간 최소 거리 계산
            float dist = DistancePointTriangle(point, a, b, c);
            if (dist < minDist) minDist = dist;
        }

        return minDist;
    }

    // 점과 삼각형 사이의 최단 거리 계산
    private static float DistancePointTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        // 참고: Ericson, "Real-Time Collision Detection"
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 ap = p - a;

        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return Vector3.Distance(p, a);

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return Vector3.Distance(p, b);

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float v = d1 / (d1 - d3);
            return Vector3.Distance(p, a + v * ab);
        }

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return Vector3.Distance(p, c);

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float w = d2 / (d2 - d6);
            return Vector3.Distance(p, a + w * ac);
        }

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return Vector3.Distance(p, b + w * (c - b));
        }

        // 삼각형 내부에서의 거리
        Vector3 n = Vector3.Cross(ab, ac).normalized;
        return Mathf.Abs(Vector3.Dot(p - a, n));
    }
}
