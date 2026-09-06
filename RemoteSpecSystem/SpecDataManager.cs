using System;
using System.Collections.Generic;
using IdleMineForge;
using IdleMineForge.Core;
using UnityEngine;

namespace IdleMineForge.Data
{
    /// <summary>
    /// 정적 게임 스펙 데이터 관리자 (MonoBehaviour 싱글톤)
    /// GameManager가 초기화 순서 1번으로 Init() 호출
    ///
    /// 내부 구조:
    ///   _tables         : Type → (Id → ISpecData)       범용 테이블
    ///   _drawRwdByGroup : GroupId → DrawRwdSpecData 리스트
    ///   _mergeRecipeTable : (key1, key2) 정렬 → resultKey
    ///
    /// 외부 접근:
    ///   GetUpgradeSpec(id), GetToolSpec(id), GetStageSpec(id)
    ///   GetDrawSpec(id), GetDrawRwds(groupId)
    ///   TryGetMergeResult(key1, key2, out resultKey)
    /// </summary>
    public class SpecDataManager : SingletonBase<SpecDataManager>
    {
        #region 내부 테이블

        private readonly Dictionary<Type, Dictionary<int, ISpecData>> _tables = new();
        private readonly Dictionary<int, List<DrawRwdSpecData>> _drawRwdByGroup = new();
        private readonly Dictionary<(int, int), int> _mergeRecipeTable = new();
        private readonly Dictionary<int, StageSpecData> _scaledStageCache = new();
        private readonly Dictionary<ContentUnlockType, ContentUnlockTableSpecData> _unlockTableByType = new();
        private readonly Dictionary<ContentUnlockType, List<ContentUnlockListSpecData>> _unlockListByType = new();
        private readonly Dictionary<PanelType, PanelUnlockSpecData> _panelUnlockByType = new();
        private readonly Dictionary<DrawType, List<DrawSpecData>> _drawSpecsByType = new();

        #endregion

        #region 초기화

        private Dictionary<string, string> _remoteTexts;

        /// <summary>
        /// GameManager에서 호출. 모든 스펙 CSV를 로드하고 특수 인덱스를 구성합니다.
        /// remoteTexts가 있으면 해당 텍스트를 우선 사용하고, 없으면 Resources 폴백.
        /// </summary>
        public void Init(Dictionary<string, string> remoteTexts = null)
        {
            _remoteTexts = remoteTexts;
            try { LoadAll(); }
            finally { _remoteTexts = null; }
        }

        private void LoadAll()
        {
            Load<UpgradeSpecData>("Data/upgrade_spec");
            Load<ToolSpecData>("Data/tool_spec");
            Load<OreSpecData>("Data/ore_spec");
            Load<StageSpecData>("Data/stage_spec");
            Load<DrawSpecData>("Data/draw_spec");
            Load<DrawRwdSpecData>("Data/draw_rwd_spec");
            Load<MergeRecipeSpecData>("Data/merge_recipe");
            Load<ContentUnlockTableSpecData>("Data/content_unlock_table");
            Load<ContentUnlockListSpecData>("Data/content_unlock_list");
            Load<PanelUnlockSpecData>("Data/panel_unlock_spec");
            Load<DestroyBonusSpecData>("Data/destroy_bonus_spec");

            BuildDrawRwdGroupTable();
            BuildDrawTypeIndex();
            BuildMergeTable();
            BuildUnlockTables();
            BuildPanelUnlockTable();
        }

        // ── 범용 CSV 로드 ──────────────────────────────────────────────────
        private void Load<T>(string resourcePath) where T : class, ISpecData, new()
        {
            string csvText = null;

            if (_remoteTexts != null)
            {
                // "Data/tool_spec" → "tool_spec" 로 키 변환
                var key = System.IO.Path.GetFileName(resourcePath);
                _remoteTexts.TryGetValue(key, out csvText);
            }

            if (csvText == null)
            {
                var asset = Resources.Load<TextAsset>(resourcePath);
                if (asset == null)
                {
                    Debug.LogWarning($"[SpecDataManager] CSV 없음 (스킵): Resources/{resourcePath}");
                    return;
                }
                csvText = asset.text;
            }

            var type = typeof(T);
            if (!_tables.ContainsKey(type))
                _tables[type] = new Dictionary<int, ISpecData>();

            var table = _tables[type];
            var rows = CsvParser.Parse(csvText);

            foreach (var row in rows)
            {
                var entry = new T();
                entry.ParseFromRow(row);

                if (table.ContainsKey(entry.Id))
                    Debug.LogWarning($"[SpecDataManager] 중복 ID {entry.Id} ({type.Name}) - 덮어씀");

                table[entry.Id] = entry;
            }

            Debug.Log($"[SpecDataManager] {type.Name} {table.Count}개 로드 완료");
        }

        // ── DrawRwdSpecData: GroupId별 리스트 인덱스 구성 ─────────────────
        private void BuildDrawRwdGroupTable()
        {
            _drawRwdByGroup.Clear();

            foreach (var rwd in GetAll<DrawRwdSpecData>())
            {
                if (!_drawRwdByGroup.ContainsKey(rwd.GroupId))
                    _drawRwdByGroup[rwd.GroupId] = new List<DrawRwdSpecData>();

                _drawRwdByGroup[rwd.GroupId].Add(rwd);
            }

            Debug.Log($"[SpecDataManager] DrawRwd 그룹 {_drawRwdByGroup.Count}개 인덱스 구성 완료");
        }

        // ── DrawSpecData: DrawType별 DrawLevel 오름차순 리스트 구성 ────────
        private void BuildDrawTypeIndex()
        {
            _drawSpecsByType.Clear();

            foreach (var spec in GetAll<DrawSpecData>())
            {
                if (!_drawSpecsByType.ContainsKey(spec.DrawType))
                    _drawSpecsByType[spec.DrawType] = new List<DrawSpecData>();

                _drawSpecsByType[spec.DrawType].Add(spec);
            }

            foreach (var list in _drawSpecsByType.Values)
                list.Sort((a, b) => a.DrawLevel.CompareTo(b.DrawLevel));

            Debug.Log($"[SpecDataManager] DrawType 인덱스 {_drawSpecsByType.Count}개 타입 구성 완료");
        }

        // ── MergeRecipeData: (key1, key2) 정렬 조합 인덱스 구성 ──────────
        private void BuildMergeTable()
        {
            _mergeRecipeTable.Clear();

            foreach (var recipe in GetAll<MergeRecipeSpecData>())
            {
                var sortedKey = SortedMergeKey(recipe.Key1, recipe.Key2);
                if (_mergeRecipeTable.ContainsKey(sortedKey))
                    Debug.LogWarning($"[SpecDataManager] 중복 머지 조합: {recipe.Key1} + {recipe.Key2}");

                _mergeRecipeTable[sortedKey] = recipe.ResultKey;
            }

            Debug.Log($"[SpecDataManager] 머지 레시피 {_mergeRecipeTable.Count}개 인덱스 구성 완료");
        }

        // key1, key2를 정렬하여 순서 무관 튜플 생성
        private static (int, int) SortedMergeKey(int a, int b) => a <= b ? (a, b) : (b, a);

        // ── ContentUnlock: 타입별 인덱스 구성 ────────────────────────────
        private void BuildUnlockTables()
        {
            _unlockTableByType.Clear();
            foreach (var spec in GetAll<ContentUnlockTableSpecData>())
                _unlockTableByType[spec.ContentUnlockType] = spec;

            _unlockListByType.Clear();
            foreach (var spec in GetAll<ContentUnlockListSpecData>())
            {
                if (!_unlockListByType.ContainsKey(spec.ContentUnlockType))
                    _unlockListByType[spec.ContentUnlockType] = new List<ContentUnlockListSpecData>();
                _unlockListByType[spec.ContentUnlockType].Add(spec);
            }

            Debug.Log($"[SpecDataManager] ContentUnlock 테이블 {_unlockTableByType.Count}개 / 리스트 {_unlockListByType.Count}개 타입 구성 완료");
        }

        // ── PanelUnlockSpecData: PanelType별 인덱스 구성 ─────────────────
        private void BuildPanelUnlockTable()
        {
            _panelUnlockByType.Clear();
            foreach (var spec in GetAll<PanelUnlockSpecData>())
                _panelUnlockByType[spec.PanelType] = spec;

            Debug.Log($"[SpecDataManager] PanelUnlock {_panelUnlockByType.Count}개 타입 구성 완료");
        }

        #endregion

        #region Public Get API

        public IEnumerable<T> GetSpecDataList<T>() where T : class, ISpecData
        {
            return GetAll<T>();
        }

        // ── 강화 스펙 ──────────────────────────────────────────────────────
        public UpgradeSpecData GetUpgradeSpec(int id) => Get<UpgradeSpecData>(id);

        // ── 도구 스펙 ──────────────────────────────────────────────────────
        public ToolSpecData GetToolSpec(int id) => Get<ToolSpecData>(id);

        // ── 도구 파괴 보너스 스펙 ──────────────────────────────────────────
        public DestroyBonusSpecData GetDestroyBonusSpec(int toolId) => Get<DestroyBonusSpecData>(toolId);

        // ── 원석 스펙 ──────────────────────────────────────────────────────
        public OreSpecData GetOreSpec(int id) => Get<OreSpecData>(id);

        // ── 스테이지 스펙 ─────────────────────────────────────────────────
        // CSV 범위(50) 초과 시 마지막 스테이지 기준 1.1^(stageLevel-50) 스케일 적용
        private const int MAX_CSV_STAGE = 80;
        private const float STAGE_FALLBACK_SCALE = 1.1f;

        public StageSpecData GetStageSpec(int stageLevel)
        {
            var spec = Get<StageSpecData>(stageLevel);
            if (spec != null) return spec;

            if (_scaledStageCache.TryGetValue(stageLevel, out var cached)) return cached;

            var lastSpec = Get<StageSpecData>(MAX_CSV_STAGE);
            if (lastSpec == null) return null;

            float scale = (float)Math.Pow(STAGE_FALLBACK_SCALE, stageLevel - MAX_CSV_STAGE);
            var scaled = StageSpecData.CreateScaled(stageLevel, lastSpec, scale);
            _scaledStageCache[stageLevel] = scaled;
            return scaled;
        }

        // ── 뽑기 스펙 ──────────────────────────────────────────────────────
        public DrawSpecData GetDrawSpec(int id) => Get<DrawSpecData>(id);

        // ── 뽑기 등급 보정 ─────────────────────────────────────────────────
        /// <summary>
        /// tierBonus만큼 높은 등급의 DrawSpecData 반환 (같은 DrawType 내).
        /// 최대 등급 초과 시 마지막 등급으로 클램프. tierBonus가 0이면 baseSpec 그대로 반환.
        /// </summary>
        public DrawSpecData GetBoostedDrawSpec(DrawSpecData baseSpec, int tierBonus)
        {
            if (tierBonus <= 0 || baseSpec == null) return baseSpec;
            if (!_drawSpecsByType.TryGetValue(baseSpec.DrawType, out var list)) return baseSpec;

            int idx = list.IndexOf(baseSpec);
            if (idx < 0) return baseSpec;

            return list[Math.Min(idx + tierBonus, list.Count - 1)];
        }

        // ── 뽑기 보상 그룹 리스트 ─────────────────────────────────────────
        /// <summary>GroupId에 해당하는 보상 목록. 없으면 null.</summary>
        public IReadOnlyList<DrawRwdSpecData> GetDrawRwds(int groupId)
        {
            return _drawRwdByGroup.TryGetValue(groupId, out var list) ? list : null;
        }

        // ── 머지 레시피 ────────────────────────────────────────────────────
        /// <summary>
        /// key1 + key2 조합의 결과를 조회합니다. 순서 무관.
        /// 존재하면 true, resultKey에 결과값 반환. 없으면 false.
        /// </summary>
        public bool TryGetMergeResult(int key1, int key2, out int resultKey)
        {
            return _mergeRecipeTable.TryGetValue(SortedMergeKey(key1, key2), out resultKey);
        }

        // ── 컨텐츠 언락 ───────────────────────────────────────────────────
        /// <summary>ContentUnlockType에 해당하는 비교 방식 스펙. 없으면 null.</summary>
        public ContentUnlockTableSpecData GetContentUnlockTable(ContentUnlockType type)
            => _unlockTableByType.TryGetValue(type, out var spec) ? spec : null;

        /// <summary>ContentUnlockType에 해당하는 해금 조건 목록. 없으면 null.</summary>
        public IReadOnlyList<ContentUnlockListSpecData> GetContentUnlockList(ContentUnlockType type)
            => _unlockListByType.TryGetValue(type, out var list) ? list : null;

        /// <summary>contentId로 단일 언락 리스트 항목 조회. 없으면 null.</summary>
        public ContentUnlockListSpecData GetContentUnlockListById(int contentId)
            => Get<ContentUnlockListSpecData>(contentId);

        // ── 패널 언락 스펙 ─────────────────────────────────────────────────
        /// <summary>PanelType에 해당하는 패널 언락 스펙. 없으면 null.</summary>
        public PanelUnlockSpecData GetPanelUnlockSpec(PanelType type)
            => _panelUnlockByType.TryGetValue(type, out var spec) ? spec : null;

        // ── 범용 조회 (내부 전용) ─────────────────────────────────────────
        /// <summary>ID로 단일 스펙 데이터 조회. 테이블 없거나 ID 없으면 null.</summary>
        private T Get<T>(int id) where T : class, ISpecData
        {
            var type = typeof(T);
            if (!_tables.TryGetValue(type, out var table))
            {
                Debug.LogWarning($"[SpecDataManager] {type.Name} 테이블이 로드되지 않음");
                return null;
            }

            return table.TryGetValue(id, out var data) ? data as T : null;
        }

        /// <summary>해당 타입의 모든 스펙 데이터 반환. (내부 전용)</summary>
        private IEnumerable<T> GetAll<T>() where T : class, ISpecData
        {
            var type = typeof(T);
            if (!_tables.TryGetValue(type, out var table))
                yield break;

            foreach (var entry in table.Values)
                yield return entry as T;
        }

        #endregion
    }
}
