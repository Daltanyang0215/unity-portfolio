using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using Firebase.RemoteConfig;
using UnityEngine;

namespace IdleMineForge.Data
{
    /// <summary>
    /// Firebase Remote Config로 버전 확인 후 Firestore에서 스펙 번들을 받아옵니다.
    /// 앱 버전별로 독립된 문서/캐시를 사용하므로 버전 간 데이터 충돌이 없습니다.
    /// 로컬 캐시가 최신이면 Firestore를 읽지 않습니다.
    /// </summary>
    public static class RemoteSpecLoader
    {
        private const string FIRESTORE_COLLECTION = "spec_data";
        private const int BUNDLE_FIELD_COUNT = 11;

        private static readonly TimeSpan RemoteConfigTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan FirestoreTimeout = TimeSpan.FromSeconds(10);

        // "1.0.5" → "1_0_5"
        private static string AppVersionKey     => Application.version.Replace('.', '_');
        private static string RemoteVersionKey  => $"spec_{AppVersionKey}_version";
        private static string FirestoreDocument => $"bundle_{AppVersionKey}";
        private static string CachePath         =>
            Path.Combine(Application.persistentDataPath, "spec_cache", $"bundle_{AppVersionKey}.json");

        /// <summary>
        /// 원격 스펙 번들을 로드합니다.
        /// 성공 시 csv파일명 → 텍스트 Dictionary 반환.
        /// 실패(네트워크 없음, 타임아웃 등) 시 null 반환 → Resources 폴백.
        /// </summary>
        // ── 에디터 전용 ───────────────────────────────────────────────────────
        // 에디터에서는 기본적으로 Resources를 직접 사용합니다.
        // "Tools/Spec Data/원격 다운로드 테스트" 메뉴로 Firestore 경로 테스트 가능.
#if UNITY_EDITOR
        public static bool UseRemoteInEditor
        {
            get => UnityEditor.EditorPrefs.GetBool("RemoteSpecLoader.UseRemoteInEditor", false);
            set => UnityEditor.EditorPrefs.SetBool("RemoteSpecLoader.UseRemoteInEditor", value);
        }
#endif

        public static async UniTask<Dictionary<string, string>> LoadAsync(CancellationToken ct = default)
        {
#if UNITY_EDITOR
            if (!UseRemoteInEditor)
            {
                Debug.Log("[RemoteSpecLoader] 에디터 로컬 모드 → Resources 사용 (원격 테스트: Tools/Spec Data/원격 다운로드 테스트)");
                return null;
            }
            Debug.Log("[RemoteSpecLoader] 에디터 원격 테스트 모드 → Firestore 다운로드");
#endif
            CleanupOldVersionCaches();
            try
            {
                var serverVersion = await FetchRemoteVersionAsync(ct);
                if (serverVersion == null)
                {
                    Debug.Log("[RemoteSpecLoader] RC 버전 없음 (미설정 또는 조회 실패) → 번들 CSV 사용");
                    return await LoadFromCacheAsync(default);
                }

                var cachedBundle = await TryLoadCacheAsync(ct);
                if (cachedBundle != null && cachedBundle.version == serverVersion)
                {
                    Debug.Log($"[RemoteSpecLoader] 캐시 버전 일치 ({serverVersion}) → 캐시 사용");
                    return BundleToDict(cachedBundle);
                }

                Debug.Log($"[RemoteSpecLoader] 버전 불일치 (캐시: {cachedBundle?.version ?? "없음"} / 서버: {serverVersion}) → Firestore 조회");
                var bundle = await FetchFromFirestoreAsync(serverVersion, ct);
                if (bundle == null)
                {
                    Debug.LogWarning("[RemoteSpecLoader] Firestore 조회 실패 → 캐시/폴백 사용");
                    return await LoadFromCacheAsync(default);
                }

                SaveCacheAsync(bundle).Forget();
                return BundleToDict(bundle);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RemoteSpecLoader] 예외 발생 → 캐시/폴백 사용: {e.Message}");
                return await LoadFromCacheAsync(default);
            }
        }

        // ── Remote Config ────────────────────────────────────────────────────

        private static async UniTask<string> FetchRemoteVersionAsync(CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(RemoteConfigTimeout);
            try
            {
                var remoteConfig = FirebaseRemoteConfig.DefaultInstance;
                await remoteConfig.FetchAndActivateAsync()
                    .AsUniTask()
                    .AttachExternalCancellation(cts.Token);

                var version = remoteConfig.GetValue(RemoteVersionKey).StringValue;
                return string.IsNullOrEmpty(version) ? null : version;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // cts.CancelAfter에 의한 타임아웃 (상위 ct 취소가 아님)
                Debug.LogWarning("[RemoteSpecLoader] Remote Config 타임아웃");
                return null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RemoteSpecLoader] Remote Config 예외: {e.Message}");
                return null;
            }
        }

        // ── Firestore ────────────────────────────────────────────────────────

        private static async UniTask<SpecBundleData> FetchFromFirestoreAsync(string serverVersion, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(FirestoreTimeout);
            try
            {
                var db = FirebaseFirestore.DefaultInstance;
                var snapshot = await db.Collection(FIRESTORE_COLLECTION)
                    .Document(FirestoreDocument)
                    .GetSnapshotAsync()
                    .AsUniTask()
                    .AttachExternalCancellation(cts.Token);

                if (!snapshot.Exists)
                {
                    Debug.LogWarning("[RemoteSpecLoader] Firestore 문서 없음");
                    return null;
                }

                return new SpecBundleData
                {
                    version              = serverVersion,
                    tool_spec            = snapshot.GetValue<string>("tool_spec"),
                    draw_spec            = snapshot.GetValue<string>("draw_spec"),
                    stage_spec           = snapshot.GetValue<string>("stage_spec"),
                    ore_spec             = snapshot.GetValue<string>("ore_spec"),
                    upgrade_spec         = snapshot.GetValue<string>("upgrade_spec"),
                    draw_rwd_spec        = snapshot.GetValue<string>("draw_rwd_spec"),
                    merge_recipe         = snapshot.GetValue<string>("merge_recipe"),
                    content_unlock_table = snapshot.GetValue<string>("content_unlock_table"),
                    content_unlock_list  = snapshot.GetValue<string>("content_unlock_list"),
                    panel_unlock_spec    = snapshot.GetValue<string>("panel_unlock_spec"),
                    destroy_bonus_spec   = snapshot.GetValue<string>("destroy_bonus_spec"),
                };
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // cts.CancelAfter에 의한 타임아웃 (상위 ct 취소가 아님)
                Debug.LogWarning("[RemoteSpecLoader] Firestore 타임아웃");
                return null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RemoteSpecLoader] Firestore 예외: {e.Message}");
                return null;
            }
        }

        // ── 로컬 캐시 ────────────────────────────────────────────────────────

        private static async UniTask<SpecBundleData> TryLoadCacheAsync(CancellationToken ct)
        {
            if (!File.Exists(CachePath)) return null;
            try
            {
                // 파일 읽기는 스레드풀에서, JsonUtility는 메인스레드에서 실행
                var json = await UniTask.RunOnThreadPool(() => File.ReadAllText(CachePath), cancellationToken: ct);
                return JsonUtility.FromJson<SpecBundleData>(json);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RemoteSpecLoader] 캐시 읽기 실패: {e.Message}");
                return null;
            }
        }

        private static async UniTask<Dictionary<string, string>> LoadFromCacheAsync(CancellationToken ct)
        {
            var bundle = await TryLoadCacheAsync(ct);
            return bundle != null ? BundleToDict(bundle) : null;
        }

        private static async UniTask SaveCacheAsync(SpecBundleData bundle)
        {
            try
            {
                var json = JsonUtility.ToJson(bundle); // 메인스레드에서 먼저 직렬화
                await UniTask.RunOnThreadPool(() =>
                {
                    var dir = Path.GetDirectoryName(CachePath);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var tmpPath = CachePath + ".tmp";
                    File.WriteAllText(tmpPath, json);

                    // Android는 File.Move가 대상 파일 존재 시 예외 → 먼저 삭제 후 이동
                    if (File.Exists(CachePath))
                        File.Delete(CachePath);
                    File.Move(tmpPath, CachePath);
                });
                Debug.Log($"[RemoteSpecLoader] 캐시 저장 완료: {bundle.version}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RemoteSpecLoader] 캐시 저장 실패 (동작에는 영향 없음): {e.Message}");
            }
        }

        // ── 캐시 관리 ─────────────────────────────────────────────────────────

        // 앱 버전이 바뀌면 이전 버전 캐시 파일(bundle_1_0_5.json 등)을 자동 삭제
        private static void CleanupOldVersionCaches()
        {
            var dir = Path.Combine(Application.persistentDataPath, "spec_cache");
            if (!Directory.Exists(dir)) return;

            var currentFile = $"bundle_{AppVersionKey}.json";
            foreach (var file in Directory.GetFiles(dir, "bundle_*.json"))
            {
                if (Path.GetFileName(file) == currentFile) continue;
                try { File.Delete(file); }
                catch { /* 삭제 실패해도 동작에 영향 없음 */ }
            }
        }

        public static void ClearCache()
        {
            if (File.Exists(CachePath))
            {
                File.Delete(CachePath);
                Debug.Log($"[RemoteSpecLoader] 캐시 삭제: bundle_{AppVersionKey}.json");
            }
            else
            {
                Debug.Log("[RemoteSpecLoader] 캐시 파일 없음");
            }
        }

        // ── 변환 ─────────────────────────────────────────────────────────────

        private static Dictionary<string, string> BundleToDict(SpecBundleData b)
        {
            var dict = new Dictionary<string, string>(BUNDLE_FIELD_COUNT);
            if (b.tool_spec            != null) dict["tool_spec"]            = b.tool_spec;
            if (b.draw_spec            != null) dict["draw_spec"]            = b.draw_spec;
            if (b.stage_spec           != null) dict["stage_spec"]           = b.stage_spec;
            if (b.ore_spec             != null) dict["ore_spec"]             = b.ore_spec;
            if (b.upgrade_spec         != null) dict["upgrade_spec"]         = b.upgrade_spec;
            if (b.draw_rwd_spec        != null) dict["draw_rwd_spec"]        = b.draw_rwd_spec;
            if (b.merge_recipe         != null) dict["merge_recipe"]         = b.merge_recipe;
            if (b.content_unlock_table != null) dict["content_unlock_table"] = b.content_unlock_table;
            if (b.content_unlock_list  != null) dict["content_unlock_list"]  = b.content_unlock_list;
            if (b.panel_unlock_spec    != null) dict["panel_unlock_spec"]    = b.panel_unlock_spec;
            if (b.destroy_bonus_spec   != null) dict["destroy_bonus_spec"]   = b.destroy_bonus_spec;
            return dict;
        }
    }
}
