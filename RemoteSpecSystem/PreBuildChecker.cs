using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PreBuildChecker : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    // ── 활성화 상태여야 하는 오브젝트 이름 목록 ──────────────────────────────
    private static readonly string[] RequiredActiveObjects =
    {
        "Canvas_Loading"
    };

    // ── 비활성화 상태여야 하는 오브젝트 이름 목록 (디버그 오브젝트 등) ──────
    private static readonly string[] RequiredInactiveObjects =
    {
        // "DebugPanel",
        // "TestButton",
    };

    // ────────────────────────────────────────────────────────────────────────

    public void OnPreprocessBuild(BuildReport report)
    {
        CheckBundleVersion();
        CheckMainSceneObjects();
    }

    private static void CheckBundleVersion()
    {
        string version = PlayerSettings.bundleVersion;

        if (string.IsNullOrEmpty(version))
            throw new BuildFailedException("[PreBuild] 버전이 비어 있습니다.");

        // 시맨틱 버저닝 형식 검사 (예: 1.0.0 / 1.2.34)
        if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+$"))
            throw new BuildFailedException($"[PreBuild] 버전 형식이 올바르지 않습니다: '{version}'\n  → 올바른 형식: X.Y.Z (예: 1.0.0)");

        Debug.Log($"[PreBuild] ✓ 버전: {version}");
    }

    private static void CheckMainSceneObjects()
    {
        if (RequiredActiveObjects.Length == 0 && RequiredInactiveObjects.Length == 0)
            return;

        if (EditorBuildSettings.scenes.Length == 0)
            throw new BuildFailedException("[PreBuild] Build Settings에 씬이 등록되어 있지 않습니다.");

        string mainScenePath = EditorBuildSettings.scenes[0].path;
        Scene mainScene = SceneManager.GetSceneByPath(mainScenePath);
        bool wasLoaded = mainScene.isLoaded;

        if (!wasLoaded)
            mainScene = EditorSceneManager.OpenScene(mainScenePath, OpenSceneMode.Additive);

        try
        {
            CheckActiveState(mainScene, RequiredActiveObjects, mustBeActive: true);
            CheckActiveState(mainScene, RequiredInactiveObjects, mustBeActive: false);
        }
        finally
        {
            if (!wasLoaded)
                EditorSceneManager.CloseScene(mainScene, removeScene: true);
        }

        Debug.Log($"[PreBuild] ✓ 메인 씬 오브젝트 상태 확인 완료: {mainScenePath}");
    }

    private static void CheckActiveState(Scene scene, string[] objectNames, bool mustBeActive)
    {
        string stateLabel = mustBeActive ? "활성화" : "비활성화";

        foreach (string objName in objectNames)
        {
            GameObject target = FindInScene(scene, objName);

            if (target == null)
                throw new BuildFailedException($"[PreBuild] '{objName}' 오브젝트를 메인 씬에서 찾을 수 없습니다.");

            if (target.activeSelf != mustBeActive)
                throw new BuildFailedException($"[PreBuild] '{objName}'이 {stateLabel} 상태여야 합니다. (현재: {(target.activeSelf ? "활성화" : "비활성화")})");
        }
    }

    // inactive 오브젝트까지 포함하여 씬 전체에서 이름으로 검색
    private static GameObject FindInScene(Scene scene, string objName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == objName)
                return root;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (child.name == objName)
                    return child.gameObject;
            }
        }
        return null;
    }
}
