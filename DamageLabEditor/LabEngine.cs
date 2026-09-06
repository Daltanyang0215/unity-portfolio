using UnityEngine;
using NightView.Data;
using NightView.Gameplay;

namespace NightView.EditorTools.DamageLab
{
    /// <summary>
    /// 분석/시뮬 데이터 생성 — 게임의 순수 코어를 그대로 호출(= 게임과 동일 수식, DAMAGE_LAB §4).
    /// 빌드 조립(RunBuild) → 턴-워크(탄별 즉발 + 턴 종료 DoT)로 LabSeries 산출. 코어 무수정 — 읽기만.
    /// </summary>
    public static class LabEngine
    {
        /// <summary>분석 탭 — 프리셋(총기/시퀀스/모드/델타)으로 조립 후 워크. multiTargetMult = 다표적·관통/폭발 보정(단일 = 1).</summary>
        public static LabSeries Build(LabPreset preset, int magazineCount, float required, AmmoDataSO defaultAmmo, float multiTargetMult = 1f)
        {
            int effMagCount = Mathf.Max(1, magazineCount + (preset != null ? preset.Delta.MagazineCountAdd : 0));
            RunBuild build = AssembleBuild(preset, effMagCount, defaultAmmo);
            if (build == null) return new LabSeries { Required = required, Turns = effMagCount };
            return Walk(build, required, preset.Delta, multiTargetMult);
        }

        /// <summary>시뮬 탭 — 기존 RunBuild(런 장비)를 그대로 워크. 도달 판정·DoT 누적 공유. multiTargetMult = 누적·총합에 곱(단일 = 1).
        /// boss = 보스 패시브(기본 None=무영향). 물리경감·속성면역=즉발 배율, 상태이상/속성면역=DoT 부여 차단(게임 BattleController와 동일).
        ///
        /// ⭐ 2026-09-03 — 조건부 모드가 Lab에서 통째로 0 기여이던 것을 고쳤다(감사 [12]).
        ///   <b>판 상태(L0-d)는 가정이 아니라 시뮬레이션이 아는 사실</b>이라 자동으로 채운다(턴 번호·잔여 턴·정원).
        ///   <b>표적 상태(L2-b)는 어디를 겨누느냐에 달린 가정</b>이라 호출자가 넘길 때만 켜진다.</summary>
        /// <param name="fieldCap">표적 정원 — 양수면 판 상태 조건(첫 턴·막 턴·잔여 턴당·정원당)이 활성화된다.
        /// 0이면 BoardContext.None으로 돌아 종전과 동일하게 계산된다(회귀 0).</param>
        /// <param name="target">조준 표적 가정 — 기본값(Valid=false)이면 표적 조건이 전부 불충족.</param>
        public static LabSeries Walk(RunBuild build, float required, LabDelta delta = default, float multiTargetMult = 1f,
                                     BossSpec boss = default, int fieldCap = 0, TargetContext target = default)
        {
            float mult = multiTargetMult > 0f ? multiTargetMult : 1f;
            var series = new LabSeries
            {
                Required = required,
                Turns = build.MagazineCount,
                HasGun = true,
                MagazineSize = build.MagazineSize,
            };

            int magSize = build.MagazineSize;
            var burn = new BurnState();
            var poison = new PoisonState(); // 독 DoT — 무감쇠 누적(게임 PoisonState와 동일). 펠릿 곱 O
            float cum = 0f;
            float firstTurnImmediate = 0f;

            for (int turn = 1; turn <= series.Turns; turn++)
            {
                float turnImmediate = 0f;

                // ⭐ 집계는 <b>턴 시작마다</b> 다시 돈다 — 게임 FireRoutine과 같은 자리다(BoardContext 주석 참조).
                //   종전엔 루프 밖에서 1회만 돌아 판 상태 조건이 값을 낼 자리 자체가 없었다.
                //   LastTurnKills·AllShocked는 Lab이 모르는 값이라 0/false로 둔다(보수적 = 미충족).
                BoardContext board = fieldCap > 0
                    ? new BoardContext(turn, series.Turns - turn + 1, fieldCap, fieldCap, false, 0)
                    : default;
                BuildStats baseStats = build.ToBuildStats(board);
                ApplyDelta(ref baseStats, delta); // 실험 델타 (임시 — SO 무수정)

                for (int i = 0; i < magSize; i++)
                {
                    AmmoEntry entry = build.GetShotEntry(i);
                    var s = new LabSample { Turn = turn, ShotIndex = i, Kind = LabSampleKind.Shot };

                    if (entry.IsEmpty)
                    {
                        s.IsEmpty = true;
                        s.AmmoName = "(빈 칸)";
                    }
                    else
                    {
                        BuildStats shot = build.GetShotStats(i, baseStats); // 순번 트리거 합성
                        // L2-b 표적 조건 — 호출자가 조준 표적을 가정해 넘겼을 때만 얹힌다(게임 FireOneShot과 같은 순서)
                        shot = build.ApplyTargetStatus(shot, target, entry.Spec.Element);
                        // 멀티샷 — 펠릿 수만큼 즉발 누적 (게임 FireOneShot와 동일: 펄스당 롤, 펠릿 합산). 펠릿 1 = 종전 동작
                        int pellets = DamageCalculator.EffectivePellets(shot.MultiShot, entry.Spec.MultiShot);
                        // ⚠ 2026-08-27 — 크리 해석을 PhysicalCritExpected에 위임. 직접 CritExpectedMultiplier를 부르면
                        //   정밀 초과(초과분 → 배율 전환)가 빠져 Lab만 실전투와 어긋난다.
                        float physPer = DamageCalculator.CalcPerShotBeforeCrit(shot, entry.Spec.PhysicalBase)
                                        * DamageCalculator.PhysicalCritExpected(shot)
                                        * BossGimmickResolver.PhysicalFactor(boss);                 // 물리 경감
                        float elemRaw = DamageCalculator.CalcElemental(shot, entry.Spec); // 보스 배율 전 순수 속성 즉발 — 상태이상 부여의 기준값
                        // ⭐ 원소 치명은 즉발에만 — elemRaw(부여량 기준)에는 곱하지 않는다(더블딥 방지)
                        float elemPer = elemRaw
                                        * DamageCalculator.ElementalCritExpected(shot)
                                        * BossGimmickResolver.ImmediateElementalFactor(boss, entry.Spec.Element); // 속성 저항/약점

                        s.Physical = physPer * pellets;
                        s.ElementalHit = elemPer * pellets;
                        int basePellets = DamageCalculator.EffectivePellets(baseStats.MultiShot, entry.Spec.MultiShot);
                        s.BaseValue = DamageCalculator.CalcPerShot(baseStats, entry.Spec) * basePellets; // 조건 무 기준(펠릿 동반)
                        s.AmmoName = AmmoName(entry);
                        s.TriggerLabel = TriggerLabelAt(build, i, magSize);

                        // 화상 부여 — 강도 = 화염 즉발 × (1+상태이상 강도%), 중첩 X라 펠릿 수 무관(§4.1·§4.3).
                        // ⚠ 기준은 elemRaw(보스 배율 전) — 게임은 부여량을 즉발과 따로 산출하므로 elemPer를 쓰면 보스 경감이 이중으로 걸린다.
                        if (entry.Spec.Element == ElementType.Fire && entry.Spec.HasElement)
                            burn.Apply(elemRaw * (1f + shot.StatusIntensity)
                                       * BossGimmickResolver.StatusFactor(boss, ElementType.Fire));
                        // 독 부여 — 스택 누적(중첩 O) → 펠릿 곱 O (게임 FireOneShot poisonAmount와 동일, §4.2)
                        else if (entry.Spec.Element == ElementType.Poison && entry.Spec.HasElement)
                            poison.Apply(elemRaw * (1f + shot.StatusIntensity) * pellets
                                         * BossGimmickResolver.StatusFactor(boss, ElementType.Poison));
                    }

                    cum += s.Total * mult;
                    turnImmediate += s.Total;
                    series.Samples.Add(s);
                    series.Cumulative.Add(cum);
                    if (s.Total > series.MaxBar) series.MaxBar = s.Total;
                }

                // 턴 종료 DoT칸 — 화상(감쇠) + 독(무감쇠 누적) 합산. 게임 EndTurnAsync와 동일 순서
                // ⚠ 감쇠는 빌드에서 읽는다 — 상수를 쓰면 M11(불씨 유지) 3종이 Lab에서 통째로 무시된다(2026-09-03).
                float tick = burn.TickAtTurnEnd(build.BurnDecay) + poison.TickAtTurnEnd();
                cum += tick * mult;
                series.Samples.Add(new LabSample { Turn = turn, ShotIndex = -1, Kind = LabSampleKind.Dot, Dot = tick, AmmoName = "DoT" });
                series.Cumulative.Add(cum);
                if (tick > series.MaxBar) series.MaxBar = tick;

                series.TurnRows.Add(new LabTurnRow { Turn = turn, Immediate = turnImmediate, Dot = tick, CumAtEnd = cum });
                if (turn == 1) firstTurnImmediate = turnImmediate;

                if (series.ClearedTurn == 0 && cum >= required) // 도달 판정 — 틱 후 (§4.1)
                {
                    series.ClearedTurn = turn;
                    series.Cleared = true;
                }
            }

            series.StageTotal = cum;
            series.PerTurn = firstTurnImmediate;
            series.PerShotAvg = magSize > 0 ? firstTurnImmediate / magSize : 0f;
            return series;
        }

        private static RunBuild AssembleBuild(LabPreset p, int magazineCount, AmmoDataSO defaultAmmo)
        {
            if (p == null || p.Gun == null) return null;

            float baseDmg = defaultAmmo != null ? defaultAmmo.BaseDamage : 10f;
            var build = new RunBuild(baseDmg, p.Gun.BaseMagazineSize, magazineCount, p.Gun.SlotCount,
                p.Gun.MagazineSizeLocked, p.Gun.MultiShot); // 실린더 고정·멀티샷 반영 (샷건·리볼버 정확도)

            int magSize = build.MagazineSize;
            for (int i = 0; i < magSize; i++)
            {
                AmmoDataSO ammoSO = i < p.Sequence.Count ? p.Sequence[i] : null;
                AmmoEntry entry = ammoSO != null
                    ? BuildFactory.CreateEntry(ammoSO, false)
                    : AmmoEntry.CreateEmpty();
                build.Sequence.Set(i, entry, out _);
            }

            for (int i = 0; i < p.Mods.Count; i++)
                if (p.Mods[i] != null) BuildFactory.Equip(build, p.Mods[i]); // 슬롯 한도 내에서만 장착

            return build;
        }

        private static void ApplyDelta(ref BuildStats s, LabDelta d)
        {
            if (!d.Any) return;
            s.FlatAdded += d.FlatAdd;
            s.IncreasedPercent += d.IncreasedAdd;
            s.MoreMultiplier *= (1f + d.MoreAdd);
            s.CritChance += d.CritChanceAdd;
            s.CritMultiplier += d.CritMultAdd;
            s.MultiShot += d.MultiShotAdd;                // 펠릿 가산 (총 탄 수 축)
            s.StatusIntensity += d.StatusIntensityAdd;    // 상태이상 강도% (화상 부여량 ×(1+값))
        }

        private static string TriggerLabelAt(RunBuild build, int shotIndex, int magSize)
        {
            var mods = build.Mods;
            int last = magSize - 1;
            for (int i = 0; i < mods.Count; i++)
            {
                ModSpec m = mods[i];
                switch (m.Trigger)
                {
                    case TriggerType.FirstShot: if (shotIndex == 0) return "첫 탄"; break;
                    case TriggerType.LastShot:  if (shotIndex == last) return "막 탄"; break;
                    case TriggerType.NthShot:   if (shotIndex == m.TriggerParam - 1) return m.TriggerParam + "번째"; break;
                }
            }
            return "";
        }

        private static string AmmoName(in AmmoEntry e)
        {
            if (e.Source is AmmoDataSO so && so != null) return so.DisplayName;
            if (e.IsDefault) return "기본탄";
            return e.Spec.HasElement ? e.Spec.Element.ToString() : "물리탄";
        }
    }
}
