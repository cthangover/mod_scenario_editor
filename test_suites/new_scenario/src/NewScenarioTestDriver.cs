namespace Cthangover.Core.UI.Tool.ScenarioEditor.Tests
{
    using System.Collections.Generic;
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Scenes;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Tools.Schemas;
    using Cthangover.Tools.Services;
    using Godot;

    /// <summary>
    /// Drives the scenario editor and the command palette in rpg_plus as an author without the dev tools: a new
    /// scenario is made from the editor, a bad name and an existing name are refused, the palette writes a stats
    /// line and inserts it into the open scenario, and saving writes it. The file the test made is removed again.
    /// </summary>
    public partial class NewScenarioTestDriver : Node
    {
        private const float TickSeconds = 0.15f;
        private const float TimeoutSeconds = 60f;
        private const string Mod = "rpg_plus_game";
        private const string ScenarioName = "itest_new_scenario";
        private const string Path = "scenarios/" + ScenarioName + ".scenario";
        private const string Line = "action stat.add stat=str value=2";

        private readonly List<string> failures = new();
        private int passed;
        private int step;
        private double elapsed;
        private ScenarioEditorWindow scenarios;
        private CommandPaletteWindow palette;
        private bool fileExisted;

        public NewScenarioTestDriver()
        {
            Name = "NewScenarioTestDriver";
            var timer = new Timer { WaitTime = TickSeconds, Autostart = true };
            AddChild(timer);
            timer.Timeout += Tick;
        }

        private void Tick()
        {
            if (step < 0)
                return;
            elapsed += TickSeconds;
            if (elapsed > TimeoutSeconds)
            {
                Fail($"timeout at step {step}");
                Cleanup();
                Finish();
                return;
            }

            try
            {
                RunStep();
            }
            catch (System.Exception ex)
            {
                Fail($"step {step}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                Cleanup();
                Finish();
            }
        }

        private void RunStep()
        {
            switch (step)
            {
                case 0:
                    if (!Boot.IsReady)
                        return;
                    fileExisted = ModFileService.ReadText(Mod, Path) != null;
                    Check(!fileExisted, "the test's scenario file does not exist yet");
                    scenarios = ToolWindow.Open<ScenarioEditorWindow>();
                    palette = ToolWindow.Open<CommandPaletteWindow>();
                    step++;
                    return;
                case 1:
                    if (!scenarios.IsInsideTree() || !palette.IsInsideTree())
                        return;
                    Check(scenarios.CreateScenario(Mod, "Bad Name") == null, "a name with capitals and a space is refused");
                    var key = scenarios.CreateScenario(Mod, ScenarioName);
                    Check(key == ScenarioFileService.MakeKey(Mod, Path), $"New makes {Path} in {Mod} ({key})");
                    Check(ModFileService.ReadText(Mod, Path) != null, "the file is on disk");
                    Check(scenarios.CreateScenario(Mod, ScenarioName) == null, "a second New with the same name is refused");
                    Check(scenarios.OpenKey == key, "the new scenario is open in the editor");
                    step++;
                    return;
                case 2:
                    if (scenarios.OpenKey == null)
                        return;
                    palette.Choose("action:stat.add");
                    palette.SetValue("stat", "str");
                    palette.SetValue("value", "2");
                    Check(palette.Line == Line, $"the palette writes the line ({palette.Line})");
                    Check(palette.InsertIntoEditor(), "Insert puts the line into the new scenario");
                    step++;
                    return;
                case 3:
                    var saved = scenarios.SaveActive();
                    Check(saved, "saving the scenario succeeds");
                    var written = ModFileService.ReadText(Mod, Path) ?? string.Empty;
                    Check(written.Contains(Line), "the file holds the palette's line after the save");
                    SceneManager.ReloadContent();
                    step++;
                    return;
                case 4:
                    Cleanup();
                    Finish();
                    return;
            }
        }

        private void Check(bool condition, string what)
        {
            if (condition)
            {
                passed++;
                GameLogger.Log("TEST", $"[PASS] {what}");
            }
            else
            {
                Fail(what);
            }
        }

        private void Fail(string what)
        {
            failures.Add(what);
            GameLogger.Log("TEST", $"[FAIL] {what}", LogLevel.Error);
        }

        private void Cleanup()
        {
            if (step < 0)
                return;
            foreach (var window in new Node[] { scenarios, palette })
            {
                if (window != null && IsInstanceValid(window))
                    window.QueueFree();
            }

            if (!fileExisted && ModFileService.ReadText(Mod, Path) != null)
                ModFileService.DeleteFile(Mod, Path);
            SceneManager.ReloadContent();
        }

        private void Finish()
        {
            step = -1;
            GameLogger.Log("TEST", $"[TEST_RESULT] name=new_scenario passed={passed} failed={failures.Count}");
            ApplicationExit.Quit(GetTree(), failures.Count == 0 ? 0 : 1);
        }
    }
}
