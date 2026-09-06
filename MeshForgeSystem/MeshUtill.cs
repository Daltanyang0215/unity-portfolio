using System;
using System.Collections.Generic;
using UnityEngine;

public static class MeshUtill
{
    /// <summary>
    /// 하드 엣지를 반영하여 노멀을 재계산합니다.
    /// - 각도 차이가 threshold 이상이면 버텍스를 분리해서 노멀을 독립적으로 유지합니다.
    /// </summary>
    /// <param name="mesh">대상 Mesh</param>
    /// <param name="threshold">하드엣지 판정 각도 (기본: 60도)</param>
    public static void RecalculateHardNormals(this Mesh mesh, float threshold = 0f)
    {
        float cosineThreshold = Mathf.Cos(threshold * Mathf.Deg2Rad);

        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;

        // 결과 저장소
        List<Vector3> newVertices = new List<Vector3>();
        List<Vector3> newNormals = new List<Vector3>();
        List<int> newTriangles = new List<int>();

        // 삼각형별 노멀 계산
        Vector3[] faceNormals = new Vector3[triangles.Length / 3];
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 v0 = vertices[triangles[i]];
            Vector3 v1 = vertices[triangles[i + 1]];
            Vector3 v2 = vertices[triangles[i + 2]];

            Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
            faceNormals[i / 3] = normal;
        }

        // 각 삼각형의 버텍스를 새로운 리스트에 채워 넣음
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];

            Vector3 fn = faceNormals[i / 3];

            // 각 버텍스를 검사하고, 하드엣지 기준으로 분리
            int n0 = AddOrSplitVertex(newVertices, newNormals, vertices[i0], fn, cosineThreshold);
            int n1 = AddOrSplitVertex(newVertices, newNormals, vertices[i1], fn, cosineThreshold);
            int n2 = AddOrSplitVertex(newVertices, newNormals, vertices[i2], fn, cosineThreshold);

            newTriangles.Add(n0);
            newTriangles.Add(n1);
            newTriangles.Add(n2);
        }

        // Mesh 갱신
        mesh.Clear();
        mesh.vertices = newVertices.ToArray();
        mesh.normals = newNormals.ToArray();
        mesh.triangles = newTriangles.ToArray();
        mesh.RecalculateBounds();
    }

    /// <summary>
    /// 하드엣지 판정에 따라 버텍스를 새로 추가하거나 재사용
    /// </summary>
    private static int AddOrSplitVertex(List<Vector3> verts, List<Vector3> norms, Vector3 v, Vector3 normal, float cosineThreshold)
    {
        for (int i = 0; i < verts.Count; i++)
        {
            if (verts[i] == v)
            {
                // 기존 노멀과 비교 → 각도 차이가 작으면 같은 버텍스 사용
                if (Vector3.Dot(norms[i], normal) >= cosineThreshold)
                {
                    norms[i] = (norms[i] + normal).normalized;
                    return i;
                }
            }
        }

        // 새로운 버텍스로 추가
        verts.Add(v);
        norms.Add(normal);
        return verts.Count - 1;
    }
}
