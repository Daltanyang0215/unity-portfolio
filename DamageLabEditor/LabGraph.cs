using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NightView.EditorTools.DamageLab
{
    /// <summary>
    /// 분석 그래프 렌더 (DAMAGE_LAB §5) — 막대(물리/속성 스택 + DoT칸) + 누적선 + 목표 점선 + 교차점.
    /// 이중 Y축(좌=순간뎀 / 우=누적). 보너스 베이스라인 틱 + 호버 툴팁.
    /// 비교 모드: 막대 A/B 닷지(소속 색 띠) + 누적선 B 오버레이. IMGUI + Handles, 코어 무수정.
    /// </summary>
    public static class LabGraph
    {
        private const float MarginL = 46f, MarginR = 54f, MarginT = 16f, MarginB = 22f;
        private const float TurnGap = 12f;
        private const float BarFill = 0.71f; // 막대 폭 비율 (단일 모드) — 나머지는 탄 간 간격

        private static bool Pro => EditorGUIUtility.isProSkin;
        private static Color ColBg => Pro ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.82f, 0.82f, 0.82f);
        private static Color ColGrid => Pro ? new Color(1, 1, 1, 0.08f) : new Color(0, 0, 0, 0.10f);
        private static Color ColPhys => Pro ? new Color(0.36f, 0.62f, 0.96f) : new Color(0.20f, 0.45f, 0.85f);
        private static Color ColElem => new Color(0.95f, 0.55f, 0.20f);
        private static Color ColDot => new Color(0.80f, 0.30f, 0.12f);
        private static Color ColCum => Pro ? new Color(0.95f, 0.95f, 0.95f) : new Color(0.10f, 0.10f, 0.10f);
        private static Color ColLineA => Pro ? new Color(0.40f, 0.80f, 1f) : new Color(0.10f, 0.45f, 0.85f);
        private static Color ColLineB => Pro ? new Color(0.55f, 0.90f, 0.45f) : new Color(0.20f, 0.60f, 0.25f);
        private static Color ColTarget => new Color(0.90f, 0.30f, 0.30f);
        private static Color ColBonus => new Color(0.97f, 0.85f, 0.20f);
        private static Color ColEmpty => Pro ? new Color(1, 1, 1, 0.20f) : new Color(0, 0, 0, 0.20f);
        private static Color ColText => Pro ? new Color(0.78f, 0.78f, 0.78f) : new Color(0.20f, 0.20f, 0.20f);

        public static void Draw(EditorWindow win, Rect rect, LabSeries a, LabSeries b = null)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseMove && rect.Contains(e.mousePosition)) { win.Repaint(); return; }
            if (e.type != EventType.Repaint) return;

            EditorGUI.DrawRect(rect, ColBg);
            if (a == null || !a.HasGun || a.Samples.Count == 0)
            {
                Label(rect, "  데이터 없음 (총기를 지정하세요)", TextAnchor.MiddleCenter);
                return;
            }

            bool hasB = b != null && b.HasGun && b.Samples.Count > 0;
            bool dodge = hasB && b.Samples.Count == a.Samples.Count && b.MagazineSize == a.MagazineSize && b.Turns == a.Turns;

            var plot = new Rect(rect.x + MarginL, rect.y + MarginT,
                                rect.width - MarginL - MarginR, rect.height - MarginT - MarginB);
            if (plot.width <= 10f || plot.height <= 10f) return;

            float maxBar = Mathf.Max(1e-3f, Mathf.Max(a.MaxBar, hasB ? b.MaxBar : 0f)) * 1.15f;
            float maxCum = Mathf.Max(Mathf.Max(a.StageTotal, hasB ? b.StageTotal : 0f), Mathf.Max(a.Required, 1e-3f)) * 1.10f;
            float YR(float v) => plot.yMax - Mathf.Clamp01(v / maxCum) * plot.height;

            // 격자 + Y축 라벨 (좌=막대 / 우=누적)
            Handles.color = ColGrid;
            for (int g = 0; g <= 4; g++)
            {
                float fy = plot.yMax - g / 4f * plot.height;
                Handles.DrawLine(new Vector3(plot.x, fy), new Vector3(plot.xMax, fy));
                Label(new Rect(rect.x, fy - 8, MarginL - 4, 16), (maxBar * g / 4f).ToString("F0"), TextAnchor.MiddleRight);
                Label(new Rect(plot.xMax + 2, fy - 8, MarginR - 4, 16), (maxCum * g / 4f).ToString("F0"), TextAnchor.MiddleLeft);
            }

            // 목표 점선 (우축)
            float yReq = YR(a.Required);
            Handles.color = ColTarget;
            Handles.DrawDottedLine(new Vector3(plot.x, yReq), new Vector3(plot.xMax, yReq), 0.4f);
            Label(new Rect(plot.xMax + 2, yReq - 8, MarginR - 2, 16), "요구", TextAnchor.MiddleLeft, ColTarget);

            // 막대 + 누적점
            int magSize = a.MagazineSize;
            int totalCols = a.Turns * (magSize + 1);
            float colW = (plot.width - TurnGap * Mathf.Max(0, a.Turns - 1)) / Mathf.Max(1, totalCols);
            float bw = Mathf.Max(2f, colW * BarFill);

            var cumPts = new List<Vector3>(a.Samples.Count + 1) { new Vector3(plot.x, plot.yMax) };
            float x = plot.x;
            int curTurn = a.Samples[0].Turn;
            float turnStartX = x;
            int hoverIdx = -1;
            Vector2 mp = e.mousePosition;

            for (int k = 0; k < a.Samples.Count; k++)
            {
                LabSample smp = a.Samples[k];
                if (smp.Turn != curTurn)
                {
                    DrawTurnLabel(plot, turnStartX, x, curTurn);
                    x += TurnGap;
                    turnStartX = x;
                    curTurn = smp.Turn;
                }

                if (dodge)
                {
                    float half = Mathf.Max(2f, colW * 0.40f);
                    float mid = colW * 0.06f;
                    DrawBar(smp, x + colW * 0.5f - mid * 0.5f - half, half, plot, maxBar, ColLineA);
                    DrawBar(b.Samples[k], x + colW * 0.5f + mid * 0.5f, half, plot, maxBar, ColLineB);
                }
                else
                {
                    DrawBar(smp, x + (colW - bw) * 0.5f, bw, plot, maxBar, null);
                }

                cumPts.Add(new Vector3(x + colW * 0.5f, YR(a.Cumulative[k])));
                if (new Rect(x, plot.y, colW, plot.height).Contains(mp)) hoverIdx = k;
                x += colW;
            }
            DrawTurnLabel(plot, turnStartX, x, curTurn);

            // 누적선 — A (비교 시 색 구분)
            Handles.color = hasB ? ColLineA : ColCum;
            Handles.DrawAAPolyLine(2.5f, cumPts.ToArray());

            // 누적선 — B 오버레이
            if (hasB)
            {
                Handles.color = ColLineB;
                Handles.DrawAAPolyLine(2.5f, BuildCumPoints(b, plot, maxCum));
            }

            // 교차점 (A)
            if (a.Cleared)
            {
                int ci = FirstClearIndex(a);
                if (ci >= 0)
                {
                    float px = cumPts[ci + 1].x;
                    Handles.color = ColTarget;
                    Handles.DrawSolidDisc(new Vector3(px, yReq, 0f), Vector3.forward, 3.5f);
                    Handles.DrawLine(new Vector3(px, plot.y), new Vector3(px, plot.yMax));
                    Label(new Rect(px + 4, plot.y + 2, 150, 16), $"클리어 @ T{a.ClearedTurn}", TextAnchor.MiddleLeft, ColTarget);
                }
            }

            DrawLegend(new Rect(plot.x + 4, plot.y + 1, plot.width, 16), hasB);
            if (hoverIdx >= 0) DrawTooltip(rect, mp, a, hoverIdx);
        }

        private static void DrawBar(in LabSample smp, float bx, float bw, Rect plot, float maxBar, Color? ownerStrip)
        {
            float YL(float v) => plot.yMax - Mathf.Clamp01(v / maxBar) * plot.height;

            if (smp.Kind == LabSampleKind.Dot)
            {
                if (smp.Dot > 0f) EditorGUI.DrawRect(new Rect(bx, YL(smp.Dot), bw, plot.yMax - YL(smp.Dot)), ColDot);
            }
            else if (smp.IsEmpty)
            {
                EditorGUI.DrawRect(new Rect(bx, plot.yMax - 2f, bw, 2f), ColEmpty);
            }
            else
            {
                float yPhys = YL(smp.Physical);
                EditorGUI.DrawRect(new Rect(bx, yPhys, bw, plot.yMax - yPhys), ColPhys);
                if (smp.ElementalHit > 0f)
                {
                    float yTop = YL(smp.Physical + smp.ElementalHit);
                    EditorGUI.DrawRect(new Rect(bx, yTop, bw, yPhys - yTop), ColElem);
                }
                if (smp.Bonus > 0.05f)
                {
                    Handles.color = ColBonus;
                    float yBase = YL(smp.BaseValue);
                    Handles.DrawLine(new Vector3(bx, yBase), new Vector3(bx + bw, yBase));
                }
            }

            if (ownerStrip.HasValue)
                EditorGUI.DrawRect(new Rect(bx, plot.yMax, bw, 2f), ownerStrip.Value); // 소속 색 띠 (A/B)
        }

        private static Vector3[] BuildCumPoints(LabSeries s, Rect plot, float maxCum)
        {
            int totalCols = s.Turns * (s.MagazineSize + 1);
            float colW = (plot.width - TurnGap * Mathf.Max(0, s.Turns - 1)) / Mathf.Max(1, totalCols);
            var pts = new List<Vector3>(s.Samples.Count + 1) { new Vector3(plot.x, plot.yMax) };
            float x = plot.x;
            int curTurn = s.Samples[0].Turn;
            for (int k = 0; k < s.Samples.Count; k++)
            {
                if (s.Samples[k].Turn != curTurn) { x += TurnGap; curTurn = s.Samples[k].Turn; }
                float y = plot.yMax - Mathf.Clamp01(s.Cumulative[k] / maxCum) * plot.height;
                pts.Add(new Vector3(x + colW * 0.5f, y));
                x += colW;
            }
            return pts.ToArray();
        }

        private static int FirstClearIndex(LabSeries s)
        {
            for (int k = 0; k < s.Cumulative.Count; k++)
                if (s.Cumulative[k] >= s.Required) return k;
            return -1;
        }

        private static void DrawTurnLabel(Rect plot, float x0, float x1, int turn)
        {
            Label(new Rect((x0 + x1) * 0.5f - 24f, plot.yMax + 3f, 48f, 14f), "T" + turn, TextAnchor.MiddleCenter);
        }

        private static void DrawLegend(Rect r, bool compare)
        {
            float x = r.x;
            x = Chip(x, r.y, "물리", ColPhys);
            x = Chip(x, r.y, "속성", ColElem);
            x = Chip(x, r.y, "DoT", ColDot);
            x = Chip(x, r.y, "보너스", ColBonus);
            if (compare)
            {
                x = Chip(x, r.y, "A", ColLineA);
                Chip(x, r.y, "B", ColLineB);
            }
        }

        private static float Chip(float x, float y, string label, Color c)
        {
            EditorGUI.DrawRect(new Rect(x, y + 3f, 9f, 9f), c);
            Label(new Rect(x + 12f, y, 46f, 14f), label, TextAnchor.MiddleLeft);
            return x + 54f;
        }

        private static void DrawTooltip(Rect bounds, Vector2 mouse, LabSeries s, int k)
        {
            LabSample smp = s.Samples[k];
            string body;
            if (smp.Kind == LabSampleKind.Dot)
                body = $"T{smp.Turn} · DoT(화상)\n강도 {smp.Dot:F1}\n누적 {s.Cumulative[k]:F0}";
            else if (smp.IsEmpty)
                body = $"T{smp.Turn} · {smp.ShotIndex + 1}탄\n(빈 칸 — 불발)\n누적 {s.Cumulative[k]:F0}";
            else
            {
                string trig = string.IsNullOrEmpty(smp.TriggerLabel) ? "" : $" [{smp.TriggerLabel}]";
                string bonus = smp.Bonus > 0.05f ? $"\n보너스 +{smp.Bonus:F0}" : "";
                body = $"T{smp.Turn} · {smp.ShotIndex + 1}탄 — {smp.AmmoName}{trig}\n물리 {smp.Physical:F0} / 속성 {smp.ElementalHit:F0}{bonus}\n누적 {s.Cumulative[k]:F0}";
            }

            var content = new GUIContent(body);
            var textStyle = new GUIStyle(EditorStyles.label) { fontSize = 10 };
            textStyle.normal.textColor = Pro ? Color.white : Color.black;
            Vector2 size = textStyle.CalcSize(content);
            float w = Mathf.Min(size.x + 14f, 280f);
            float h = size.y + 10f;
            float tx = Mathf.Clamp(mouse.x + 14f, bounds.x, bounds.xMax - w);
            float ty = Mathf.Clamp(mouse.y + 14f, bounds.y, bounds.yMax - h);
            var box = new Rect(tx, ty, w, h);

            // 불투명 배경 + 테두리 (가독성)
            EditorGUI.DrawRect(box, Pro ? new Color(0.13f, 0.13f, 0.13f, 1f) : new Color(0.97f, 0.97f, 0.97f, 1f));
            DrawBorder(box, Pro ? new Color(1f, 1f, 1f, 0.55f) : new Color(0f, 0f, 0f, 0.55f));
            GUI.Label(new Rect(box.x + 7f, box.y + 4f, box.width - 12f, box.height - 8f), content, textStyle);
        }

        private static void DrawBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1f, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), c);
        }

        private static void Label(Rect r, string text, TextAnchor anchor, Color? color = null)
        {
            var st = new GUIStyle(EditorStyles.miniLabel) { alignment = anchor };
            st.normal.textColor = color ?? ColText;
            GUI.Label(r, text, st);
        }
    }
}
