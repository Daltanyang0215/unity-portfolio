using UnityEditor;
using UnityEngine;
using NightView.Data;
using NightView.Gameplay;

namespace NightView.EditorTools.DamageLab
{
    /// <summary>
    /// Damage Lab — 데미지/밸런스 시각·인터랙티브 도구 (DAMAGE_LAB.md).
    /// M1: 분석 탭 골격(반응형 A/B 셸 + 전투조건/프리셋 입력 + 데이터 워크 → 텍스트 출력). 그래프 = M2.
    /// </summary>
    public sealed class DamageLabWindow : EditorWindow
    {
        private const float WideBreakpoint = 900f;
        private static readonly string[] Tabs = { "분석 / 비교", "시뮬레이션", "자동 (통계)" };

        private int _tab;

        // 전투 조건 (전역)
        private int _magazineCount = 4;
        private bool _useStageReq = true;
        private int _stageIndex = 1;
        private float _requiredManual = 250f;
        private float _multiTargetMult = 1f; // 다중표적 보정 — 관통/폭발 N표적 타격 (M2 세션3, 단일 = 1)
        private AmmoDataSO _defaultAmmo;

        // 프리셋 (M1 = A 단일, 비교 B는 M4)
        private readonly LabPreset _presetA = new LabPreset { Name = "A" };
        private readonly LabPreset _presetB = new LabPreset { Name = "B", Enabled = false };

        private Vector2 _inputScroll, _outputScroll;
        private LabSeries _seriesA, _seriesB;
        private bool _dirty = true;

        // 실험 델타 → SO write-back (M5)
        private ModDataSO _writeTarget;
        private float _writeValue;

        [MenuItem("GunRogue/Damage Lab")]
        public static void Open() => GetWindow<DamageLabWindow>("Damage Lab");

        private LabSim _sim;
        private Vector2 _simScroll, _simOutScroll;
        private int _simSlot = 1;
        private int _simSeed = 12345;                               // 보스 시드 셔플 (리셋 시 적용)
        private BossGimmick _simBossOverride = BossGimmick.None;    // 보스 강제 (None = 시드 랜덤)
        private GUIStyle _descStyle;

        // 자동 시뮬 (통계, 탭 2)
        private GunDataSO _autoGun;
        private ShopPoolSO _autoPool;
        private AmmoDataSO _autoAmmo;
        private int _autoMagCount = 4, _autoStartCurrency = 0, _autoMaxStage = 12, _autoRuns = 100, _autoSeed = 12345;
        private System.Collections.Generic.List<LabAuto.StyleResult> _autoResults;
        private string _autoError;
        private Vector2 _autoScroll;
        // 보스 오버라이드 (occurrence별, None=시드 랜덤) + 최종 적용 라인업 표시
        private readonly System.Collections.Generic.List<BossGimmick> _autoBossOverrides = new System.Collections.Generic.List<BossGimmick>();
        private System.Collections.Generic.List<LabAuto.BossLine> _autoBossLineup;

        private void OnEnable()
        {
            wantsMouseMove = true; // 그래프 호버 갱신
            if (_sim == null) _sim = new LabSim();
        }

        private void OnDisable() => _sim?.Dispose();

        private float Required => _useStageReq ? StageProgression.GetRequired(_stageIndex) : _requiredManual;

        private void OnGUI()
        {
            DrawTabs();
            if (_tab == 1) { DrawSim(); return; }
            if (_tab == 2) { DrawAuto(); return; }

            DrawCombatConditions();
            if (_dirty || _seriesA == null) Recompute();

            if (position.width >= WideBreakpoint)
            {
                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.BeginVertical(GUILayout.Width(340));
                _inputScroll = EditorGUILayout.BeginScrollView(_inputScroll);
                DrawInputs();
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical();
                _outputScroll = EditorGUILayout.BeginScrollView(_outputScroll);
                DrawOutput();
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();

                EditorGUILayout.EndHorizontal();
            }
            else
            {
                _outputScroll = EditorGUILayout.BeginScrollView(_outputScroll);
                DrawInputs();
                EditorGUILayout.Space(6);
                DrawOutput();
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawTabs()
        {
            EditorGUILayout.Space(2);
            _tab = GUILayout.Toolbar(_tab, Tabs);
            EditorGUILayout.Space(2);
        }

        private void DrawCombatConditions()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("전투 조건", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            _magazineCount = Mathf.Max(1, EditorGUILayout.IntField("탄창 수(턴)", _magazineCount));

            EditorGUILayout.BeginHorizontal();
            _useStageReq = EditorGUILayout.ToggleLeft("스테이지 요구치", _useStageReq, GUILayout.Width(120));
            if (_useStageReq) _stageIndex = Mathf.Max(1, EditorGUILayout.IntField("스테이지", _stageIndex));
            else _requiredManual = EditorGUILayout.FloatField("요구 데미지", _requiredManual);
            EditorGUILayout.EndHorizontal();
            if (_useStageReq) EditorGUILayout.LabelField("→ 요구치", Required.ToString("F0"));

            _defaultAmmo = (AmmoDataSO)EditorGUILayout.ObjectField("기본탄(시퀀스 채움)", _defaultAmmo, typeof(AmmoDataSO), false);

            // 다중표적 보정 — 관통(행)·폭발(인접)이 N표적 타격 시 누적·총합 ×배율 (단일 = 1, §8 / GDD §3.3)
            _multiTargetMult = Mathf.Max(1f, EditorGUILayout.FloatField("다중표적 보정 ×", _multiTargetMult));

            if (EditorGUI.EndChangeCheck()) _dirty = true;
            EditorGUILayout.EndVertical();
        }

        private void DrawInputs()
        {
            DrawPreset(_presetA);

            EditorGUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            _presetB.Enabled = EditorGUILayout.ToggleLeft("비교 모드 (프리셋 B)", _presetB.Enabled);
            if (EditorGUI.EndChangeCheck()) _dirty = true;

            if (_presetB.Enabled)
            {
                if (GUILayout.Button("A → B 복제")) { CopyAToB(); _dirty = true; }
                DrawPreset(_presetB);
            }

            DrawWriteBack();
        }

        private void DrawWriteBack()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("SO 값 반영 (write-back)", EditorStyles.boldLabel);
            _writeTarget = (ModDataSO)EditorGUILayout.ObjectField("대상 모드", _writeTarget, typeof(ModDataSO), false);
            if (_writeTarget != null)
            {
                EditorGUILayout.LabelField("현재 Value", _writeTarget.Value.ToString("F3"));
                _writeValue = EditorGUILayout.FloatField("새 Value", _writeValue);
                if (GUILayout.Button("적용 (Undo 가능)"))
                {
                    if (EditorUtility.DisplayDialog("SO 값 반영",
                        $"{_writeTarget.name}.Value = {_writeValue} 적용할까요?\n(Ctrl+Z 로 되돌리기 가능)", "적용", "취소"))
                    {
                        WriteModValue(_writeTarget, _writeValue);
                        _dirty = true;
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        private static void WriteModValue(ModDataSO target, float value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty("<Value>k__BackingField");
            if (p == null) { Debug.LogError("[DamageLab] ModDataSO.Value 프로퍼티를 찾지 못했습니다."); return; }
            p.floatValue = value;
            so.ApplyModifiedProperties();   // Undo 자동 등록
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();
            Debug.Log($"[DamageLab] {target.name}.Value = {value} 반영 완료");
        }

        private void CopyAToB()
        {
            _presetB.Gun = _presetA.Gun;
            _presetB.Sequence.Clear();
            _presetB.Sequence.AddRange(_presetA.Sequence);
            _presetB.Mods.Clear();
            _presetB.Mods.AddRange(_presetA.Mods);
        }

        private static void DrawSeriesSummary(string tag, LabSeries s)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"프리셋 {tag} — 탄당 {s.PerShotAvg:F1} / 턴당 {s.PerTurn:F1} / 예상총 {s.StageTotal:F0}", EditorStyles.boldLabel);
            if (s.Cleared)
                EditorGUILayout.LabelField("도달", $"클리어 @ T{s.ClearedTurn}  (요구 {s.Required:F0})");
            else
                EditorGUILayout.LabelField("도달", $"미달  ({s.StageTotal:F0} / {s.Required:F0})");
            EditorGUILayout.EndVertical();
        }

        private void DrawPreset(LabPreset p)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"프리셋 {p.Name}", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            p.Gun = (GunDataSO)EditorGUILayout.ObjectField("총기", p.Gun, typeof(GunDataSO), false);
            if (EditorGUI.EndChangeCheck()) _dirty = true;

            if (p.Gun == null)
            {
                EditorGUILayout.HelpBox("총기(GunDataSO)를 지정하세요.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            int magSize = p.Gun.BaseMagazineSize;
            EnsureSequence(p, magSize);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"탄창 시퀀스 (크기 {magSize})", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            for (int i = 0; i < magSize; i++)
                p.Sequence[i] = (AmmoDataSO)EditorGUILayout.ObjectField($"  {i + 1}", p.Sequence[i], typeof(AmmoDataSO), false);
            if (EditorGUI.EndChangeCheck()) _dirty = true;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"장착 모드 ({p.Mods.Count}/{p.Gun.SlotCount})", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            int removeAt = -1;
            for (int i = 0; i < p.Mods.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                p.Mods[i] = (ModDataSO)EditorGUILayout.ObjectField(p.Mods[i], typeof(ModDataSO), false);
                if (GUILayout.Button("−", GUILayout.Width(24))) removeAt = i;
                EditorGUILayout.EndHorizontal();
            }
            if (EditorGUI.EndChangeCheck()) _dirty = true;
            if (removeAt >= 0) { p.Mods.RemoveAt(removeAt); _dirty = true; }

            using (new EditorGUI.DisabledScope(p.Mods.Count >= p.Gun.SlotCount))
            {
                if (GUILayout.Button("+ 모드 추가")) { p.Mods.Add(null); _dirty = true; }
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("실험 델타 (임시 — SO 무수정)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            p.Delta.FlatAdd = EditorGUILayout.FloatField("가산 +", p.Delta.FlatAdd);
            p.Delta.IncreasedAdd = EditorGUILayout.FloatField("증가% + (0.2=20%)", p.Delta.IncreasedAdd);
            p.Delta.MoreAdd = EditorGUILayout.FloatField("승산% + (0.5=×1.5)", p.Delta.MoreAdd);
            p.Delta.CritChanceAdd = EditorGUILayout.FloatField("크리율 + (0.1=10%)", p.Delta.CritChanceAdd);
            p.Delta.CritMultAdd = EditorGUILayout.FloatField("크리배율 +", p.Delta.CritMultAdd);
            p.Delta.MagazineCountAdd = EditorGUILayout.IntField("탄창수(턴) +", p.Delta.MagazineCountAdd);
            p.Delta.MultiShotAdd = EditorGUILayout.IntField("멀티샷 펠릿 +", p.Delta.MultiShotAdd);
            p.Delta.StatusIntensityAdd = EditorGUILayout.FloatField("상태이상 강도% + (0.5=+50%)", p.Delta.StatusIntensityAdd);
            if (EditorGUI.EndChangeCheck()) _dirty = true;

            EditorGUILayout.EndVertical();
        }

        private void DrawOutput()
        {
            if (_presetA.Gun == null || _seriesA == null || !_seriesA.HasGun)
            {
                EditorGUILayout.HelpBox("총기를 지정하면 출력 결과가 표시됩니다.", MessageType.Info);
                return;
            }
            LabSeries s = _seriesA;

            DrawSeriesSummary("A", s);
            if (_seriesB != null) DrawSeriesSummary("B", _seriesB);

            // 그래프 (M2/M3/M4) — 막대(물리/속성/DoT) + 누적선(A/B) + 목표 점선 + 교차점 + 호버
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("그래프", EditorStyles.boldLabel);
            Rect graphRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(260));
            LabGraph.Draw(this, graphRect, s, _seriesB);

            if (_seriesB != null) { DrawTables("A", s); DrawTables("B", _seriesB); }
            else DrawTables("", s);
        }

        private static void DrawTables(string tag, LabSeries s)
        {
            string pre = string.IsNullOrEmpty(tag) ? "" : $"[{tag}] ";

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(pre + "턴별", EditorStyles.boldLabel);
            Row("턴", "즉발", "DoT", "누적", true);
            for (int i = 0; i < s.TurnRows.Count; i++)
            {
                LabTurnRow t = s.TurnRows[i];
                Row($"T{t.Turn}", t.Immediate.ToString("F0"), t.Dot > 0f ? t.Dot.ToString("F0") : "-", t.CumAtEnd.ToString("F0"), false);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(pre + "1턴 탄별 (이후 턴 동일 — DoT만 변동)", EditorStyles.boldLabel);
            Row5("#", "탄종", "물리", "속성", "보너스", true);
            for (int i = 0; i < s.Samples.Count; i++)
            {
                LabSample smp = s.Samples[i];
                if (smp.Turn != 1 || smp.Kind != LabSampleKind.Shot) continue;
                string name = smp.IsEmpty ? "(빈 칸)" : smp.AmmoName;
                if (!string.IsNullOrEmpty(smp.TriggerLabel)) name += $" [{smp.TriggerLabel}]";
                Row5($"{smp.ShotIndex + 1}", name,
                     smp.Physical.ToString("F0"), smp.ElementalHit.ToString("F0"),
                     smp.Bonus > 0.05f ? "+" + smp.Bonus.ToString("F0") : "-", false);
            }
        }

        private void DrawSim()
        {
            DrawSimSetup();

            if (!_sim.Ready)
            {
                EditorGUILayout.HelpBox(string.IsNullOrEmpty(_sim.Message) ? "총기 지정 후 [리셋]을 누르세요." : _sim.Message, MessageType.Info);
                return;
            }

            _sim.Boss = ResolveSimBoss(_sim.Stage); // 현재 스테이지 보스 — Required(가중요구치)·Walk(면역·경감)에 반영
            float req = _sim.Required;
            LabSeries series = LabEngine.Walk(_sim.Loadout.Build, req, boss: _sim.Boss,
                                              fieldCap: StageProgression.GetStage(_sim.Stage).FieldCap);

            if (position.width >= WideBreakpoint)
            {
                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.BeginVertical(GUILayout.Width(380));
                _simScroll = EditorGUILayout.BeginScrollView(_simScroll);
                DrawShopSection();
                DrawSequenceSection();
                DrawEquipSection();
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical();
                _simOutScroll = EditorGUILayout.BeginScrollView(_simOutScroll);
                DrawSimStatus(series);
                DrawSimGraph(series);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();

                EditorGUILayout.EndHorizontal();
            }
            else
            {
                _simScroll = EditorGUILayout.BeginScrollView(_simScroll);
                DrawSimStatus(series);
                DrawShopSection();
                DrawSequenceSection();
                DrawEquipSection();
                DrawSimGraph(series);
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawSimSetup()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("시뮬 설정", EditorStyles.boldLabel);
            _sim.Gun = (GunDataSO)EditorGUILayout.ObjectField("총기", _sim.Gun, typeof(GunDataSO), false);
            _sim.Pool = (ShopPoolSO)EditorGUILayout.ObjectField("상점 풀(비우면 자동)", _sim.Pool, typeof(ShopPoolSO), false);
            _sim.DefaultAmmo = (AmmoDataSO)EditorGUILayout.ObjectField("기본탄", _sim.DefaultAmmo, typeof(AmmoDataSO), false);
            _sim.BaseMagazineCount = Mathf.Max(1, EditorGUILayout.IntField("탄창 수(턴)", _sim.BaseMagazineCount));
            _sim.StartCurrency = Mathf.Max(0, EditorGUILayout.IntField("시작 재화", _sim.StartCurrency));
            _simSeed = EditorGUILayout.IntField("보스 시드", _simSeed);
            _simBossOverride = (BossGimmick)EditorGUILayout.EnumPopup("보스 강제 (None=시드)", _simBossOverride);
            if (GUILayout.Button("리셋 (런 시작)")) { BossLibrary.SetRunSeed(_simSeed); _sim.Reset(); }
            EditorGUILayout.EndVertical();
        }

        /// <summary>수동 시뮬 현재 스테이지 보스 — 강제 오버라이드 우선, 없으면 시드 셔플. 비보스 = None.</summary>
        private BossSpec ResolveSimBoss(int stage)
        {
            int cycle = GameConfig.BossStageCycle;
            if (cycle <= 0 || stage < 1 || stage % cycle != 0) return BossSpec.None;
            if (_simBossOverride != BossGimmick.None) return LabBoss.Make(_simBossOverride);
            return BossLibrary.GetBoss(stage);
        }

        private void DrawSimStatus(LabSeries series)
        {
            float req = _sim.Required;
            string bossTag = _sim.Boss.Gimmick != BossGimmick.None ? $"  ·  ★{BossLabel(_sim.Boss.Gimmick)}" : "";
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"스테이지 {_sim.Stage}  ·  요구 {req:F0}{bossTag}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"재화 {_sim.Run.Currency}", GUILayout.Width(90));
            using (new EditorGUI.DisabledScope(_sim.Run.Currency < _sim.Shop.RerollCost))
            {
                if (GUILayout.Button($"리롤 {_sim.Shop.RerollCost}", GUILayout.Width(80))) _sim.Shop.TryReroll();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField($"출력 — 탄당 {series.PerShotAvg:F1} / 턴당 {series.PerTurn:F1} / 예상총 {series.StageTotal:F0}");
            EditorGUILayout.LabelField(series.Cleared ? $"도달 ✔  클리어 @ T{series.ClearedTurn}" : $"미달 ✘  ({series.StageTotal:F0} / {req:F0})");
            using (new EditorGUI.DisabledScope(!series.Cleared))
            {
                if (GUILayout.Button("다음 ▶  (클리어 → 보상 → 다음 스테이지)")) _sim.Next(series);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSimGraph(LabSeries series)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("그래프 (현재 빌드 vs 현재 스테이지)", EditorStyles.boldLabel);
            Rect gr = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(240));
            LabGraph.Draw(this, gr, series);
            DrawTables("", series);
        }

        // ── 자동 (통계) 탭 ──

        private void DrawAuto()
        {
            _autoScroll = EditorGUILayout.BeginScrollView(_autoScroll);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("자동 시뮬 설정", EditorStyles.boldLabel);
            _autoGun = (GunDataSO)EditorGUILayout.ObjectField("총기", _autoGun, typeof(GunDataSO), false);
            _autoPool = (ShopPoolSO)EditorGUILayout.ObjectField("상점 풀(비우면 자동)", _autoPool, typeof(ShopPoolSO), false);
            _autoAmmo = (AmmoDataSO)EditorGUILayout.ObjectField("기본탄", _autoAmmo, typeof(AmmoDataSO), false);
            _autoMagCount = Mathf.Max(1, EditorGUILayout.IntField("탄창 수(턴)", _autoMagCount));
            _autoStartCurrency = Mathf.Max(0, EditorGUILayout.IntField("시작 재화", _autoStartCurrency));
            _autoMaxStage = Mathf.Clamp(EditorGUILayout.IntField("최대 스테이지", _autoMaxStage), 1, 40);
            _autoRuns = Mathf.Clamp(EditorGUILayout.IntField("유형별 런 수", _autoRuns), 1, 5000);
            _autoSeed = EditorGUILayout.IntField("시드", _autoSeed);
            DrawBossOverrides();
            if (GUILayout.Button("실행")) RunAuto();
            EditorGUILayout.EndVertical();

            // 최종 적용 보스 라인업 (시드 셔플 + 오버라이드 반영)
            if (_autoBossLineup != null && _autoBossLineup.Count > 0)
                EditorGUILayout.HelpBox("적용 보스 — " + BossLineupText(), MessageType.None);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("플레이 유형 (AI 성향)", EditorStyles.boldLabel);
            foreach (PlayStyle st in LabAuto.Styles)
                EditorGUILayout.LabelField($"· {LabAuto.Name(st)} — {LabAuto.Desc(st)}", DescStyle);
            EditorGUILayout.LabelField("※ 통계는 AI 정책의 거울입니다 — 절대치보다 유형 간 상대 비교로 보세요.", DescStyle);
            EditorGUILayout.EndVertical();

            if (!string.IsNullOrEmpty(_autoError))
                EditorGUILayout.HelpBox(_autoError, MessageType.Warning);
            else if (_autoResults != null && _autoResults.Count > 0)
                DrawAutoResults();

            EditorGUILayout.EndScrollView();
        }

        private void RunAuto()
        {
            var cfg = new LabAuto.AutoConfig
            {
                Gun = _autoGun, Pool = _autoPool, DefaultAmmo = _autoAmmo,
                BaseMagazineCount = _autoMagCount, StartCurrency = _autoStartCurrency,
                MaxStage = _autoMaxStage, Runs = _autoRuns, Seed = _autoSeed,
                BossOverrides = _autoBossOverrides.ToArray(),
            };
            try
            {
                EditorUtility.DisplayProgressBar("자동 시뮬", $"{_autoRuns}회 × {LabAuto.Styles.Length}유형 실행 중...", 0.5f);
                _autoResults = LabAuto.RunBatch(cfg, out _autoError, out _autoBossLineup);
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        /// <summary>보스 오버라이드 — 보스 스테이지(주기)마다 드롭다운(None=시드 랜덤). MaxStage에 맞춰 칸 수 동기화.</summary>
        private void DrawBossOverrides()
        {
            int cycle = GameConfig.BossStageCycle;
            if (cycle <= 0) return;
            int count = _autoMaxStage / cycle;
            while (_autoBossOverrides.Count < count) _autoBossOverrides.Add(BossGimmick.None);
            while (_autoBossOverrides.Count > count) _autoBossOverrides.RemoveAt(_autoBossOverrides.Count - 1);
            if (count == 0) return;

            EditorGUILayout.LabelField("보스 오버라이드 (None = 시드 랜덤)", EditorStyles.miniBoldLabel);
            for (int o = 0; o < count; o++)
            {
                int stage = (o + 1) * cycle;
                _autoBossOverrides[o] = (BossGimmick)EditorGUILayout.EnumPopup($"스테이지 {stage}", _autoBossOverrides[o]);
            }
        }

        private string BossLineupText()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _autoBossLineup.Count; i++)
            {
                if (i > 0) sb.Append("  ·  ");
                LabAuto.BossLine bl = _autoBossLineup[i];
                sb.Append('스').Append(bl.Stage).Append(' ').Append(BossLabel(bl.Gimmick));
            }
            return sb.ToString();
        }

        private static string BossLabel(BossGimmick g) => g switch
        {
            BossGimmick.PhysicalResist => "물리경감",
            BossGimmick.FireResist     => "화염저항",
            BossGimmick.PoisonResist   => "독저항",
            BossGimmick.ShockResist    => "전기저항",
            BossGimmick.FireWeakness   => "화염약점",
            BossGimmick.PoisonWeakness => "독약점",
            BossGimmick.ShockWeakness  => "전기약점",
            BossGimmick.StatusResist   => "상태이상저항",
            BossGimmick.HighDemand     => "체력증가",
            BossGimmick.CritResist     => "치명저항",
            BossGimmick.Armor          => "장갑",
            _ => "-",
        };

        private void DrawAutoResults()
        {
            int maxStage = _autoMaxStage;
            const float cellW = 36f, cellH = 18f, labelW = 96f;

            // 1) 클리어율 히트맵 + 마진
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("클리어율 히트맵 (유형 × 스테이지, %)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(labelW));
            for (int s = 1; s <= maxStage; s++) GUILayout.Label($"S{s}", EditorStyles.miniLabel, GUILayout.Width(cellW));
            EditorGUILayout.EndHorizontal();
            foreach (LabAuto.StyleResult r in _autoResults)
                HeatRow(LabAuto.Name(r.Style), r.ClearRate, maxStage, labelW, cellW, cellH);
            HeatRow("종합", AggregateClearRate, maxStage, labelW, cellW, cellH);
            MarginRow(maxStage, labelW, cellW, cellH);

            // 2) 출력 vs 요구치 곡선
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("출력 vs 요구치 (종합 평균 · 녹=출력 / 적=요구)", EditorStyles.boldLabel);
            Rect cr = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(160));
            DrawOutputCurve(cr, maxStage);

            // 3) 요약 (도달)
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("요약 (도달)", EditorStyles.boldLabel);
            Row("유형", "중앙 도달", "최대", "최종%", true);
            foreach (LabAuto.StyleResult r in _autoResults)
                Row(LabAuto.Name(r.Style), "T" + r.Median(), "T" + r.MaxStage, (r.ClearRate(maxStage) * 100f).ToString("F0") + "%", false);

            // 4) 경제 (런 평균)
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("경제 (런 평균)", EditorStyles.boldLabel);
            Row("유형", "획득", "소비", "잔여", true);
            foreach (LabAuto.StyleResult r in _autoResults)
                Row(LabAuto.Name(r.Style), r.AvgEarned.ToString("F0"), r.AvgSpent.ToString("F0"), r.AvgLeft.ToString("F0"), false);

            // 5) 콘텐츠 파워 감사 (탐욕)
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("콘텐츠 파워 감사 — 탐욕 채택", EditorStyles.boldLabel);
            DrawPowerAudit();
        }

        private void MarginRow(int maxStage, float labelW, float cellW, float cellH)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("마진×", GUILayout.Width(labelW));
            for (int s = 1; s <= maxStage; s++)
            {
                float req = StageProgression.GetRequired(s);
                float ratio = req > 0f ? AggregateAvgOutput(s) / req : 0f;
                Rect c = GUILayoutUtility.GetRect(cellW, cellH, GUILayout.Width(cellW), GUILayout.Height(cellH));
                EditorGUI.DrawRect(c, HeatColor(Mathf.Clamp01(ratio - 0.5f))); // 0.5→적 / 1.5→녹
                var st = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = Color.black;
                GUI.Label(c, ratio.ToString("F1"), st);
            }
            EditorGUILayout.EndHorizontal();
        }

        private float AggregateAvgOutput(int stage)
        {
            if (_autoResults == null) return 0f;
            double sum = 0; long cnt = 0;
            foreach (LabAuto.StyleResult r in _autoResults) { sum += r.OutSum[stage]; cnt += r.OutCount[stage]; }
            return cnt > 0 ? (float)(sum / cnt) : 0f;
        }

        private void DrawOutputCurve(Rect rect, int maxStage)
        {
            if (Event.current.type != EventType.Repaint) return;
            bool pro = EditorGUIUtility.isProSkin;
            EditorGUI.DrawRect(rect, pro ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.82f, 0.82f, 0.82f));
            var plot = new Rect(rect.x + 44, rect.y + 8, rect.width - 54, rect.height - 26);
            if (plot.width <= 10f || plot.height <= 10f || maxStage < 2) return;

            float maxV = 1e-3f;
            for (int s = 1; s <= maxStage; s++)
                maxV = Mathf.Max(maxV, Mathf.Max(AggregateAvgOutput(s), StageProgression.GetRequired(s)));
            maxV *= 1.1f;

            float X(int s) => plot.x + (s - 1) / (float)(maxStage - 1) * plot.width;
            float Y(float v) => plot.yMax - Mathf.Clamp01(v / maxV) * plot.height;

            var lab = new GUIStyle(EditorStyles.miniLabel);
            lab.normal.textColor = pro ? Color.gray : Color.black;
            GUI.Label(new Rect(rect.x, rect.y + 2, 42, 14), maxV.ToString("F0"), lab);
            GUI.Label(new Rect(rect.x, plot.yMax - 7, 42, 14), "0", lab);
            GUI.Label(new Rect(plot.x, plot.yMax + 4, 30, 14), "S1", lab);
            GUI.Label(new Rect(plot.xMax - 24, plot.yMax + 4, 30, 14), "S" + maxStage, lab);

            var req = new Vector3[maxStage];
            var outp = new Vector3[maxStage];
            for (int s = 1; s <= maxStage; s++)
            {
                req[s - 1] = new Vector3(X(s), Y(StageProgression.GetRequired(s)));
                outp[s - 1] = new Vector3(X(s), Y(AggregateAvgOutput(s)));
            }
            Handles.color = new Color(0.90f, 0.30f, 0.30f); Handles.DrawAAPolyLine(2.5f, req);
            Handles.color = new Color(0.35f, 0.75f, 0.40f); Handles.DrawAAPolyLine(2.5f, outp);
        }

        private void DrawPowerAudit()
        {
            LabAuto.StyleResult greedy = _autoResults.Find(r => r.Style == PlayStyle.Greedy);
            if (greedy == null || greedy.Picks.Count == 0)
            {
                EditorGUILayout.LabelField("(채택 데이터 없음)", DescStyle);
                return;
            }

            var sorted = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>>(greedy.Picks);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            int top = Mathf.Min(8, sorted.Count);
            EditorGUILayout.LabelField("자주 채택 (과대평가 후보):", DescStyle);
            for (int i = 0; i < top; i++)
                EditorGUILayout.LabelField($"   {sorted[i].Key} — {sorted[i].Value}회", DescStyle);

            ShopPoolSO pool = _autoPool != null ? _autoPool : ResolveAutoPool();
            if (pool != null)
            {
                var dead = new System.Collections.Generic.List<string>();
                CollectDead(pool.ModPool, greedy.Picks, dead);
                CollectDead(pool.AmmoPool, greedy.Picks, dead);
                CollectDead(pool.AccessoryPool, greedy.Picks, dead);
                EditorGUILayout.LabelField(dead.Count > 0
                    ? "미채택 (죽은 콘텐츠 후보): " + string.Join(", ", dead)
                    : "미채택 없음 — 모든 콘텐츠가 한 번은 채택됨", DescStyle);
            }
        }

        private static void CollectDead(UnityEngine.Object[] pool, System.Collections.Generic.Dictionary<string, int> picks, System.Collections.Generic.List<string> dead)
        {
            if (pool == null) return;
            foreach (UnityEngine.Object o in pool)
            {
                if (o == null) continue;
                string key = LabAuto.Key(DisplayNameOf(o), o);
                if (!picks.ContainsKey(key)) dead.Add(key);
            }
        }

        private static string DisplayNameOf(UnityEngine.Object o)
        {
            switch (o)
            {
                case ModDataSO m: return m.DisplayName;
                case AmmoDataSO a: return a.DisplayName;
                case AccessoryDataSO ac: return ac.DisplayName;
                default: return o != null ? o.name : null;
            }
        }

        private static ShopPoolSO ResolveAutoPool()
        {
            string[] g = AssetDatabase.FindAssets("t:ShopPoolSO");
            return g.Length > 0 ? AssetDatabase.LoadAssetAtPath<ShopPoolSO>(AssetDatabase.GUIDToAssetPath(g[0])) : null;
        }

        private void HeatRow(string label, System.Func<int, float> rate, int maxStage, float labelW, float cellW, float cellH)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(labelW));
            for (int s = 1; s <= maxStage; s++)
            {
                float v = rate(s);
                Rect c = GUILayoutUtility.GetRect(cellW, cellH, GUILayout.Width(cellW), GUILayout.Height(cellH));
                EditorGUI.DrawRect(c, HeatColor(v));
                var st = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = Color.black;
                GUI.Label(c, Mathf.RoundToInt(v * 100f).ToString(), st);
            }
            EditorGUILayout.EndHorizontal();
        }

        private float AggregateClearRate(int stage)
        {
            if (_autoResults == null || _autoResults.Count == 0) return 0f;
            float sum = 0f;
            foreach (LabAuto.StyleResult r in _autoResults) sum += r.ClearRate(stage);
            return sum / _autoResults.Count;
        }

        private static Color HeatColor(float rate)
        {
            Color lo = new Color(0.80f, 0.25f, 0.20f); // 낮음 = 적
            Color hi = new Color(0.30f, 0.70f, 0.35f); // 높음 = 녹
            return Color.Lerp(lo, hi, Mathf.Clamp01(rate));
        }

        private void DrawShopSection()
        {
            ShopManager shop = _sim.Shop;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("상점 — 총기 모드", EditorStyles.boldLabel);
            var gunMods = shop.GunModOffers;
            for (int i = 0; i < gunMods.Count; i++)
            {
                ModDataSO it = gunMods[i].Item;
                DrawOfferRow(Title(it != null ? it.DisplayName : null, it), it != null ? it.Description : "",
                             it != null ? it.Price : 0, gunMods[i].SoldOut, i, 0);
            }

            EditorGUILayout.LabelField("상점 — 탄창 모드", EditorStyles.boldLabel);
            var magMods = shop.MagModOffers;
            for (int i = 0; i < magMods.Count; i++)
            {
                ModDataSO it = magMods[i].Item;
                DrawOfferRow(Title(it != null ? it.DisplayName : null, it), it != null ? it.Description : "",
                             it != null ? it.Price : 0, magMods[i].SoldOut, i, 3);
            }

            EditorGUILayout.LabelField("상점 — 탄약 (선택 슬롯에 배치)", EditorStyles.boldLabel);
            int magSize = _sim.Loadout.Build.MagazineSize;
            _simSlot = Mathf.Clamp(EditorGUILayout.IntField($"배치 슬롯 (1~{magSize})", _simSlot), 1, Mathf.Max(1, magSize));
            var ammo = shop.AmmoOffers;
            for (int i = 0; i < ammo.Count; i++)
            {
                AmmoDataSO it = ammo[i].Item;
                bool isDefault = it == shop.DefaultAmmo;
                bool sold = ammo[i].SoldOut && !isDefault;
                int price = it != null ? it.Price : 0;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(Title(it != null ? it.DisplayName : null, it), EditorStyles.boldLabel);
                if (it != null && !string.IsNullOrEmpty(it.Description)) EditorGUILayout.LabelField(it.Description, DescStyle);
                EditorGUILayout.EndVertical();
                if (sold)
                {
                    EditorGUILayout.LabelField("품절", GUILayout.Width(130));
                }
                else
                {
                    using (new EditorGUI.DisabledScope(_sim.Run.Currency < price))
                    {
                        if (GUILayout.Button($"구매→슬롯{_simSlot} ({price})", GUILayout.Width(130)))
                            shop.TryBuyAmmoToSlot(i, _simSlot - 1);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.LabelField("상점 — 악세", EditorStyles.boldLabel);
            var acc = shop.AccessoryOffers;
            for (int i = 0; i < acc.Count; i++)
            {
                AccessoryDataSO it = acc[i].Item;
                DrawOfferRow(Title(it != null ? it.DisplayName : null, it), it != null ? it.Description : "",
                             it != null ? it.Price : 0, acc[i].SoldOut, i, 2);
            }
        }

        private void DrawOfferRow(string title, string desc, int price, bool soldOut, int index, int kind)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(desc)) EditorGUILayout.LabelField(desc, DescStyle);
            EditorGUILayout.EndVertical();
            if (soldOut)
            {
                EditorGUILayout.LabelField("품절", GUILayout.Width(90));
            }
            else
            {
                using (new EditorGUI.DisabledScope(_sim.Run.Currency < price))
                {
                    if (GUILayout.Button($"구매 {price}", GUILayout.Width(90)))
                    {
                        if (kind == 0) _sim.Shop.TryBuyGunMod(index);
                        else if (kind == 3) _sim.Shop.TryBuyMagMod(index);
                        else _sim.Shop.TryBuyAccessory(index);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSequenceSection()
        {
            var slots = _sim.Loadout.Build.Sequence.Slots;
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"탄창 시퀀스 ({slots.Count}칸)", EditorStyles.boldLabel);
            int clear = -1;
            for (int i = 0; i < slots.Count; i++)
            {
                AmmoEntry en = slots[i];
                string nm;
                if (en.IsEmpty) nm = "(빈 칸)";
                else if (en.Source is AmmoDataSO so && so != null) nm = Title(so.DisplayName, so);
                else nm = en.IsDefault ? "기본탄" : "탄";

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"슬롯 {i + 1}: {nm}");
                using (new EditorGUI.DisabledScope(en.IsEmpty))
                {
                    if (GUILayout.Button("비우기", GUILayout.Width(60))) clear = i;
                }
                EditorGUILayout.EndHorizontal();
            }
            if (clear >= 0) _sim.Shop.ClearAmmoSlot(clear);
        }

        private static string Title(string display, UnityEngine.Object o)
            => !string.IsNullOrEmpty(display) ? display : (o != null ? o.name : "-");

        private GUIStyle DescStyle => _descStyle != null
            ? _descStyle
            : (_descStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true });

        private void DrawEquipSection()
        {
            ShopManager shop = _sim.Shop;
            RunLoadout lo = _sim.Loadout;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"장착 총기 모드 ({lo.EquippedModCount}/{lo.Build.SlotCount})", EditorStyles.boldLabel);
            int sell = -1, store = -1;
            for (int i = 0; i < lo.Slots.Count; i++)
            {
                ModEntry en = lo.Slots[i];
                if (en.IsEmpty) continue; // 고정 그리드 — 빈 칸 스킵
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(Nm(en.Source as UnityEngine.Object));
                if (GUILayout.Button($"판매 {RunLoadout.GetRefund(en)}", GUILayout.Width(80))) sell = i;
                if (GUILayout.Button("보관", GUILayout.Width(50))) store = i;
                EditorGUILayout.EndHorizontal();
            }
            if (sell >= 0) shop.SellSlotMod(sell);
            else if (store >= 0) shop.MoveSlotToStash(store);

            EditorGUILayout.LabelField($"장착 탄창 모드 ({lo.EquippedMagModCount}/{lo.Build.MagSlotCount})", EditorStyles.boldLabel);
            int magSell = -1, magStore = -1;
            for (int i = 0; i < lo.MagSlots.Count; i++)
            {
                ModEntry en = lo.MagSlots[i];
                if (en.IsEmpty) continue;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(Nm(en.Source as UnityEngine.Object));
                if (GUILayout.Button($"판매 {RunLoadout.GetRefund(en)}", GUILayout.Width(80))) magSell = i;
                if (GUILayout.Button("보관", GUILayout.Width(50))) magStore = i;
                EditorGUILayout.EndHorizontal();
            }
            if (magSell >= 0) shop.SellMagSlotMod(magSell);
            else if (magStore >= 0) shop.MoveMagSlotToStash(magStore);

            EditorGUILayout.LabelField($"보관함 ({lo.Stash.Count}/{GameConfig.ModStashSize})", EditorStyles.boldLabel);
            int equip = -1, ssell = -1;
            for (int i = 0; i < lo.Stash.Count; i++)
            {
                ModEntry en = lo.Stash[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(Nm(en.Source as UnityEngine.Object));
                if (GUILayout.Button("장착", GUILayout.Width(50))) equip = i;
                if (GUILayout.Button($"판매 {RunLoadout.GetRefund(en)}", GUILayout.Width(80))) ssell = i;
                EditorGUILayout.EndHorizontal();
            }
            if (equip >= 0)
            {
                if (lo.Stash[equip].Spec.IsGunMod) shop.MoveStashToSlot(equip);
                else shop.MoveStashToMagSlot(equip);
            }
            else if (ssell >= 0) shop.SellStashMod(ssell);
        }

        private static string Nm(UnityEngine.Object o) => o != null ? o.name : "-";

        private void EnsureSequence(LabPreset p, int magSize)
        {
            while (p.Sequence.Count < magSize) p.Sequence.Add(_defaultAmmo);
            while (p.Sequence.Count > magSize) p.Sequence.RemoveAt(p.Sequence.Count - 1);
        }

        private void Recompute()
        {
            _seriesA = LabEngine.Build(_presetA, _magazineCount, Required, _defaultAmmo, _multiTargetMult);
            _seriesB = _presetB.Enabled ? LabEngine.Build(_presetB, _magazineCount, Required, _defaultAmmo, _multiTargetMult) : null;
            _dirty = false;
        }

        private static void Row(string a, string b, string c, string d, bool header)
        {
            GUIStyle st = header ? EditorStyles.boldLabel : EditorStyles.label;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(a, st, GUILayout.Width(60));
            EditorGUILayout.LabelField(b, st, GUILayout.Width(70));
            EditorGUILayout.LabelField(c, st, GUILayout.Width(70));
            EditorGUILayout.LabelField(d, st, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();
        }

        private static void Row5(string a, string b, string c, string d, string e, bool header)
        {
            GUIStyle st = header ? EditorStyles.boldLabel : EditorStyles.label;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(a, st, GUILayout.Width(30));
            EditorGUILayout.LabelField(b, st, GUILayout.Width(130));
            EditorGUILayout.LabelField(c, st, GUILayout.Width(60));
            EditorGUILayout.LabelField(d, st, GUILayout.Width(60));
            EditorGUILayout.LabelField(e, st, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();
        }
    }
}
