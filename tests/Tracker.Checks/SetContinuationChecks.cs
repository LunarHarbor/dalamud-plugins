using System.Text.Json;
using System.Text.Json.Nodes;
using DeepDungeonTracker;

internal static class SetContinuationChecks
{
    public static int Run()
    {
        var count = 0;
        void Equal<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"{label}: expected {expected}, got {actual}");
            count++;
        }

        const string key = "Synthetic Runner-SyntheticWorld";
        const DeepDungeon dungeon = DeepDungeon.PilgrimsTraverse;
        var directory = Path.Combine(Path.GetTempPath(), "ddt-continuation-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CaptureStartResult Start(string path, SaveSlotSelection choices, int floor, FloorSetTime timer) =>
                CaptureRunStore.Start(path, key, choices.GetCaptureSelectionData(), dungeon,
                    60041 + (floor - 1) / 10, floor, 24, 100, TimeSpan.Zero, timer);
            void ObserveSlot(SaveSlotSelection choices, int number)
            {
                choices.SetSelectionData(dungeon, number);
                choices.Save(key);
            }
            void Persist(string path, CaptureStartResult start) =>
                LocalStream.Save(path, start.FileName, start.Save).GetAwaiter().GetResult();
            SaveSlot Reload(string path, string file) =>
                LocalStream.Load<SaveSlot>(path, file, SaveSlot.IsValid)
                    ?? throw new Exception("Expected a valid persisted capture.");
            void ReachBoss(SaveSlot save, FloorSetTime timer, int lastFloor)
            {
                while (save.CurrentFloorNumber() < lastFloor)
                {
                    var floor = save.CurrentFloor()!;
                    floor.EnemyKilled();
                    timer.Synchronize(TimeSpan.FromMinutes((floor.Number - 1) % 10 + 1));
                    if (!CaptureFloorTransition.Advance(save, timer, floor.Number + 1))
                        throw new Exception("The set replay could not advance to the next floor.");
                }
                save.CurrentFloor()!.EnemyKilled();
                save.CurrentFloor()!.MarkBossDefeated();
                timer.Synchronize(TimeSpan.FromMinutes(10));
                save.CurrentFloorSet()!.MarkBossTime(timer.TotalTime);
            }

            var selections = new SaveSlotSelection(directory);
            ObserveSlot(selections, 1);
            var timer = new FloorSetTime();
            var first = Start(directory, selections, 1, timer);
            first.Save.AetherpoolUpdate(99, 99);
            first.Save.CurrentFloorSet()!.SetPartySize(2);
            first.Save.CurrentFloor()!.MapFullyRevealed();
            first.Save.CurrentFloor()!.CofferOpened(Coffer.PomanderOfSafety);
            ReachBoss(first.Save, timer, 10);
            CaptureSetOutcome.FinalizeExit(first.Save, timer, completionObserved: false, confirmedFailure: false);
            Persist(directory, first);
            var firstId = first.Save.RunId;
            Equal(false, Reload(directory, first.FileName).CurrentFloorSet()!.Failed,
                "boss clear without native completion is not persisted as a failure");

            selections.ResetSelectionData();
            Equal(null, selections.GetCaptureSelectionData(), "leaving the first set clears slot evidence");
            ObserveSlot(selections, 1);
            var second = Start(directory, selections, 11, timer);
            Equal(CaptureStartKind.Continue, second.Kind, "persisted first set continues at floor11");
            Equal(firstId, second.Save.RunId, "between-set continuation preserves run identity");
            Equal(first.FileName, second.FileName, "same-slot continuation preserves its destination");
            Equal(2, second.Save.FloorSets.Count, "continuation appends a second set");
            Equal(10, second.Save.Kills(), "first-set kills survive disk reload");
            Equal(1, second.Save.Coffers(), "first-set coffer survives disk reload");
            Equal(0, second.Save.CurrentFloorSet()!.Kills(), "new set has no inherited kills");
            Equal(0, second.Save.CurrentFloorSet()!.Coffers(), "new set has no inherited coffers");
            Equal(0, timer.PreviousFloorsTime.Count, "new set clears the previous floor timer segments");
            Equal(true, second.Save.FloorSets[0].Completed, "adjacent next entry retains confirmed earlier completion");
            // 51,980 character + 49,800 floors + 1,100 kills + 326 * 101 bonus.
            Equal(135806, ScoreEngine.Calculate(second.Save.Snapshot()).Total,
                "floor11 estimate includes the persisted first-set history");
            second.Save.CurrentFloorSet()!.SetPartySize(2);
            ReachBoss(second.Save, timer, 20);
            CaptureSetOutcome.FinalizeExit(second.Save, timer, completionObserved: false, confirmedFailure: false);
            Persist(directory, second);
            var complete = Reload(directory, second.FileName);
            Equal(firstId, complete.RunId, "two-set run identity survives the second exit save");
            Equal(20, complete.Kills(), "both sets retain their captured kills");
            Equal(10, complete.FloorSets[0].Kills(), "continuing does not rewrite first-set kills");
            Equal(10, complete.FloorSets[1].Kills(), "second-set kills stay in their own set");
            // 51,980 character + 94,620 floors + 2,400 kills + 626 * 101 bonus.
            Equal(212226, ScoreEngine.Calculate(complete.Snapshot()).Total,
                "two completed sets retain the hand-calculated score history");

            var firstBytes = File.ReadAllText(Path.Combine(directory, first.FileName));
            selections.ResetSelectionData();
            ObserveSlot(selections, 2);
            var other = Start(directory, selections, 1, new FloorSetTime());
            other.Save.CurrentFloor()!.EnemyKilled();
            Persist(directory, other);
            Equal(CaptureStartKind.New, other.Kind, "same-job second game slot starts independently");
            Equal(false, firstId == other.Save.RunId, "same-job slots cannot share run identity");
            Equal(1, Reload(directory, other.FileName).Kills(), "second slot contains only its own kill");
            Equal(firstBytes, File.ReadAllText(Path.Combine(directory, first.FileName)),
                "recording slot2 leaves the slot1 file byte-for-byte unchanged");

            var secondBytes = File.ReadAllText(Path.Combine(directory, other.FileName));
            var afterReload = new SaveSlotSelection(directory);
            Equal(2, afterReload.GetSelectionData(key)!.SaveSlotNumber, "reload remembers only the browsing selection");
            Equal(null, afterReload.GetCaptureSelectionData(), "reload cannot turn browsing metadata into duty identity");
            var unknown = Start(directory, afterReload, 1, new FloorSetTime());
            Persist(directory, unknown);
            Equal(CaptureStartKind.New, unknown.Kind, "unknown current slot creates a detached capture");
            Equal(false, unknown.FileName == first.FileName || unknown.FileName == other.FileName,
                "unknown current slot uses neither existing slot destination");
            Equal(firstBytes, File.ReadAllText(Path.Combine(directory, first.FileName)), "unknown reload preserves slot1");
            Equal(secondBytes, File.ReadAllText(Path.Combine(directory, other.FileName)), "unknown reload preserves slot2");

            string Scenario(string name)
            {
                var path = Path.Combine(directory, name);
                Directory.CreateDirectory(path);
                return path;
            }
            SaveSlot IncompleteLastFloor()
            {
                var save = new SaveSlot(dungeon, 60041, 24, 100);
                save.MarkCurrentSchema();
                save.AddFloorSet(10);
                save.CurrentFloorSet()!.SetPartySize(2);
                save.CurrentFloorSet()!.SetTimerKnown(false);
                return save;
            }
            var incompletePath = Scenario("unobserved-outcome");
            var incompleteChoices = new SaveSlotSelection(incompletePath);
            ObserveSlot(incompleteChoices, 1);
            var incompleteFile = SaveSlotSelection.GetSaveSlotFileName(key, incompleteChoices.GetCaptureSelectionData());
            var incomplete = IncompleteLastFloor();
            var incompleteTimer = new FloorSetTime();
            incompleteTimer.Start();
            incompleteTimer.Synchronize(TimeSpan.FromMinutes(20));
            CaptureSetOutcome.FinalizeExit(incomplete, incompleteTimer, completionObserved: false, confirmedFailure: false);
            LocalStream.Save(incompletePath, incompleteFile, incomplete).GetAwaiter().GetResult();
            incompleteChoices.ResetSelectionData();
            ObserveSlot(incompleteChoices, 1);
            var recovered = Start(incompletePath, incompleteChoices, 11, new FloorSetTime());
            Equal(CaptureStartKind.Continue, recovered.Kind, "next-set entry resolves an unobserved prior outcome");
            Equal(incomplete.RunId, recovered.Save.RunId, "unobserved outcome recovery retains run identity");
            Equal(true, recovered.Save.FloorSets[0].Completed, "next-set entry proves completion of the prior set");
            Equal(false, recovered.Save.FloorSets[0].CurrentFloor()!.BossDefeated, "continuation cannot invent a boss observation");
            Equal(false, recovered.Save.FloorSets[0].TimeBonus, "continuation cannot invent a speed bonus");
            Equal(0, recovered.Save.Kills(), "continuation cannot invent missing boss kills");
            Persist(incompletePath, recovered);
            Equal(true, SaveSlot.IsValid(Reload(incompletePath, recovered.FileName)), "reconciled incomplete capture remains replayable");

            var trailingPath = Scenario("missing-final-floor-checkpoint");
            var trailingChoices = new SaveSlotSelection(trailingPath);
            ObserveSlot(trailingChoices, 1);
            var trailingFile = SaveSlotSelection.GetSaveSlotFileName(key, trailingChoices.GetCaptureSelectionData());
            var trailing = new SaveSlot(dungeon, 60041, 24, 100);
            trailing.MarkCurrentSchema();
            trailing.AddFloorSet(9);
            trailing.CurrentFloor()!.EnemyKilled();
            trailing.CurrentFloor()!.EnemyKilled();
            trailing.CurrentFloor()!.TimeUpdate(TimeSpan.FromMinutes(7));
            LocalStream.Save(trailingPath, trailingFile, trailing).GetAwaiter().GetResult();
            trailingChoices.ResetSelectionData();
            ObserveSlot(trailingChoices, 1);
            var trailingTimer = new FloorSetTime();
            var trailingRecovered = Start(trailingPath, trailingChoices, 11, trailingTimer);
            Equal(CaptureStartKind.Continue, trailingRecovered.Kind, "same-slot floor11 recovers a final checkpoint still on floor9");
            Equal(trailing.RunId, trailingRecovered.Save.RunId, "missing final-floor checkpoint preserves run identity");
            Equal(2, trailingRecovered.Save.FloorSets.Count, "checkpoint recovery appends the observed new set");
            Equal(2, trailingRecovered.Save.FloorSets[0].Floors.Count, "checkpoint recovery inserts the missing floor10 placeholder");
            Equal(10, trailingRecovered.Save.FloorSets[0].CurrentFloor()!.Number, "previous set reaches its demonstrated boundary");
            Equal(true, trailingRecovered.Save.FloorSets[0].Floors.All(floor => floor.Cleared), "adjacent entry proves passage through the missing last floor");
            Equal(2, trailingRecovered.Save.Kills(), "missing-floor recovery preserves the two observed kills");
            Equal(0, trailingRecovered.Save.FloorSets[0].CurrentFloor()!.Kills, "missing final-floor placeholder contains no invented kills");
            Equal(false, trailingRecovered.Save.FloorSets[0].Floors.Any(floor => floor.BossDefeated), "missing-floor recovery invents no boss observation");
            Equal(false, trailingRecovered.Save.FloorSets[0].TimeBonus, "known checkpoint elapsed time cannot invent a boss speed award");
            Equal(0, trailingRecovered.Save.CurrentFloorSet()!.Kills(), "recovered continuation starts the new set with fresh counters");
            Equal(0, trailingTimer.PreviousFloorsTime.Count, "recovering the old final floor does not contaminate the new set timer");
            Persist(trailingPath, trailingRecovered);
            Equal(trailing.RunId, Reload(trailingPath, trailingRecovered.FileName).RunId, "missing-floor continuation persists as a valid replayable run");

            var skippedPath = Scenario("missing-whole-set");
            var skippedChoices = new SaveSlotSelection(skippedPath);
            ObserveSlot(skippedChoices, 1);
            var skippedFile = SaveSlotSelection.GetSaveSlotFileName(key, skippedChoices.GetCaptureSelectionData());
            LocalStream.Save(skippedPath, skippedFile, trailing).GetAwaiter().GetResult();
            var skipped = Start(skippedPath, skippedChoices, 21, new FloorSetTime());
            Equal(CaptureStartKind.New, skipped.Kind, "floor9 checkpoint cannot invent the entire intervening11-20 set");
            Equal(false, trailing.RunId == skipped.Save.RunId, "nonadjacent entry remains a separate capture");
            Equal(0, skipped.Save.Kills(), "nonadjacent entry does not inherit earlier capture counters");

            void FailedScenario(string name, bool observedFailure, bool observedBoss, CaptureStartKind expected)
            {
                var path = Scenario(name);
                var choices = new SaveSlotSelection(path);
                ObserveSlot(choices, 1);
                var file = SaveSlotSelection.GetSaveSlotFileName(key, choices.GetCaptureSelectionData());
                var previous = IncompleteLastFloor();
                if (observedBoss)
                {
                    previous.CurrentFloor()!.EnemyKilled();
                    previous.CurrentFloor()!.MarkBossDefeated();
                }
                previous.CurrentFloorSet()!.Fail();
                var json = JsonNode.Parse(JsonSerializer.Serialize(previous))!.AsObject();
                if (!observedFailure) json["FloorSets"]![0]!.AsObject().Remove("FailureObserved");
                File.WriteAllText(Path.Combine(path, file), json.ToJsonString());
                var loaded = Reload(path, file);
                Equal(observedFailure, loaded.CurrentFloorSet()!.FailureObserved,
                    name + ": persisted failure provenance is preserved");
                choices.ResetSelectionData();
                ObserveSlot(choices, 1);
                var oldFiles = Directory.GetFiles(path, "*.json").ToHashSet(StringComparer.OrdinalIgnoreCase);
                var result = Start(path, choices, 11, new FloorSetTime());
                Equal(expected, result.Kind, name + ": adjacent entry chooses the correct disposition");
                Equal(expected == CaptureStartKind.Continue, result.Save.RunId == previous.RunId,
                    name + ": only justified continuation preserves the run identity");
                var archived = Directory.GetFiles(path, "*.json").Where(filePath => !oldFiles.Contains(filePath))
                    .Select(filePath => LocalStream.Load<SaveSlot>(path, filePath, SaveSlot.IsValid))
                    .Any(save => save?.RunId == previous.RunId && save.CurrentFloorSet()!.Failed &&
                        save.CurrentFloorSet()!.FailureObserved == observedFailure);
                Equal(true, archived, name + ": previous failed capture is archived before reconciliation or replacement");
                if (expected == CaptureStartKind.Continue)
                {
                    Equal(false, result.Save.FloorSets[0].Failed, "repaired legacy false failure no longer blocks continuation");
                    Equal(true, result.Save.FloorSets[0].Completed, "repaired legacy boss-clear set is completed");
                    Equal(1, result.Save.Kills(), "legacy recovery preserves its observed boss kill");
                    Equal(false, result.Save.FloorSets[0].TimeBonus, "legacy recovery cannot invent unknown boss timing");
                }
                Persist(path, result);
                Equal(result.Save.RunId, Reload(path, result.FileName).RunId, name + ": returned capture persists to its selected slot");
            }
            FailedScenario("legacy-false-failure", observedFailure: false, observedBoss: true, CaptureStartKind.Continue);
            FailedScenario("legacy-failure-without-boss", observedFailure: false, observedBoss: false, CaptureStartKind.New);
            FailedScenario("confirmed-failure", observedFailure: true, observedBoss: true, CaptureStartKind.New);
        }
        finally
        {
            var resolved = Path.GetFullPath(directory);
            if (!resolved.StartsWith(Path.Combine(Path.GetTempPath(), "ddt-continuation-checks-"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected temporary continuation check directory.");
            Directory.Delete(resolved, true);
        }
        return count;
    }
}
