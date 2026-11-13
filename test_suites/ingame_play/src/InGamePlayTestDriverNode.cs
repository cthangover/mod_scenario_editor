using System.Linq;
using Cthangover.Core.Bootstrap;
using Cthangover.Core.Games;
using Cthangover.Core.Scenes;
using Cthangover.Core.UI.Tool.ScenarioEditor;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Tools.Common;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.Tests.InGamePlay
{
    /// <summary>
    /// The player's path that used to throw them out to the main menu: inside
    /// a running game (its dialog on screen) open the scenario editor, play a
    /// scenario, and expect the play-test to keep running — the teardown of the
    /// game's own dialog must not count as the end of the played scenario.
    /// Stop, and the played scenario leaving its scene (an option's
    /// <c>switch_scene</c>), must both come back into the game with the editor
    /// visible. Ends with
    /// <c>[TEST_RESULT] name=ingame_play passed=N failed=M</c> and the exit code.
    /// </summary>
    public partial class InGamePlayTestDriverNode : Node
    {
        private const string ResultName = "ingame_play";
        private const string GameModId = "my_novel_game";
        private const string ScenarioPath = "scenarios/my_novel_home.scenario";
        private const float TickSeconds = 0.05f;
        private const float TimeoutSeconds = 90f;

        private int _passed;
        private int _failed;
        private int _step;
        private int _waitingStep = -1;
        private ulong _waitStarted;
        private ulong _readyFrame;
        private double _elapsed;
        private bool _finished;
        private ScenarioEditorWindow _window;

        /// <summary>Creates the driver ticking on its own timer.</summary>
        public InGamePlayTestDriverNode()
        {
            var timer = new Timer { WaitTime = TickSeconds, Autostart = true };
            AddChild(timer);
            timer.Timeout += Tick;
        }

        private void Tick()
        {
            if (_finished)
                return;

            _elapsed += TickSeconds;
            if (_elapsed >= TimeoutSeconds)
            {
                Check(false, $"timeout ({TimeoutSeconds:0}s) at step {_step}");
                Finish();
                return;
            }

            var tree = GetTree();
            var scenes = tree.Root.GetNodeOrNull<GodotSceneService>("GodotSceneService");
            if (!ReadyForStep(scenes))
                return;
            switch (_step)
            {
                case 0:
                    if (!Boot.IsReady || !ActiveGame.IsActive || InMainMenu(scenes) || GodotSceneService.IsTransitioning)
                        return;
                    Check(SceneManager.GetCurrentSceneName() == "my_novel_town", $"game entered its town (scene '{SceneManager.GetCurrentSceneName()}')");
                    _window = (ScenarioEditorWindow)ToolWindow.ShowWindow(new ScenarioEditorWindow());
                    var tabs = _window.FindChildren("*", "", true, false).OfType<ScenarioEditTabs>().FirstOrDefault();
                    tabs?.OpenFile(ScenarioFileService.MakeKey(GameModId, ScenarioPath));
                    Check(tabs?.HasActiveFile == true, "scenario editor opened the home scenario");
                    break;

                case 1:
                    PressButton(_window, "tools/scenario_editor/play");
                    break;

                case 2:
                    if (GodotSceneService.IsTransitioning)
                        return;
                    if (InMainMenu(scenes))
                    {
                        Check(false, "play-test threw the player out to the main menu");
                        Finish();
                        return;
                    }

                    Check(SceneManager.IsTestPlayActive, "play-test is still running while its dialog waits for the player");
                    Check(!_window.Visible, "the editor is hidden during the play-test");
                    Check(SceneManager.GetCurrentSceneName() == "my_novel_home", $"play-test entered the scenario's scene (scene '{SceneManager.GetCurrentSceneName()}')");
                    PressButton(tree.Root, "tools/scenario_editor/stop");
                    break;

                case 3:
                    if (GodotSceneService.IsTransitioning)
                        return;
                    Check(!InMainMenu(scenes), "Stop returns into the game, not to the main menu");
                    Check(IsInstanceValid(_window) && _window.Visible, "the editor is visible again after Stop");
                    Check(!SceneManager.IsTestPlayActive, "Stop ends the play-test");
                    Check(SceneManager.GetCurrentSceneName() == "my_novel_town", $"Stop returns to the scene the player was in (scene '{SceneManager.GetCurrentSceneName()}')");
                    break;

                case 4:
                    PressButton(_window, "tools/scenario_editor/play");
                    break;

                case 5:
                    if (GodotSceneService.IsTransitioning)
                        return;
                    // The played scenario's option "Back to the town" does exactly this.
                    tree.Root.GetNodeOrNull<SceneManager>("SceneManager")?.SwitchScene("my_novel_town");
                    break;

                case 6:
                    if (GodotSceneService.IsTransitioning)
                        return;
                    Check(!InMainMenu(scenes), "a scene switch in the played scenario returns into the game, not to the main menu");
                    Check(!SceneManager.IsTestPlayActive, "leaving the played scene ends the play-test");
                    Check(IsInstanceValid(_window) && _window.Visible, "the editor is visible again after the played scenario left its scene");
                    Finish();
                    return;
            }

            _step++;
        }

        private bool ReadyForStep(GodotSceneService scenes)
        {
            var now = Time.GetTicksMsec();
            if (_waitingStep != _step)
            {
                _waitingStep = _step;
                _waitStarted = now;
                _readyFrame = 0;
            }
            if (now - _waitStarted > 15000)
            {
                Check(false, $"timed out waiting for game/play/editor readiness at step {_step}, scene '{SceneManager.GetCurrentSceneName()}'");
                Finish();
                return false;
            }
            var scene = SceneManager.GetCurrentSceneName();
            var ready = _step switch
            {
                0 => Boot.IsReady && ActiveGame.IsActive && !InMainMenu(scenes) && scene == "my_novel_town",
                1 or 4 => IsInstanceValid(_window) && _window.Visible,
                2 => InMainMenu(scenes) || PlayIsReady(scene),
                5 => PlayIsReady(scene),
                3 or 6 => !SceneManager.IsTestPlayActive && IsInstanceValid(_window) && _window.Visible && scene == "my_novel_town",
                _ => true
            };
            if (!ready || GodotSceneService.IsTransitioning)
            {
                _readyFrame = 0;
                return false;
            }
            if (_readyFrame == 0)
                _readyFrame = Engine.GetProcessFrames();
            return Engine.GetProcessFrames() - _readyFrame >= 2;
        }

        private static bool PlayIsReady(string scene)
            => SceneManager.IsTestPlayActive && scene == "my_novel_home"
                && SceneContextNode.FindNode<Cthangover.Mods.Dialog.DialogBox>("DialogBox")?.IsRunning == true;

        private static bool InMainMenu(GodotSceneService scenes) => scenes?.CurrentGodotSceneName == "mainmenu";

        private static void PressButton(Node root, string textKey)
        {
            var text = ToolUi.Tr(textKey);
            var button = root.FindChildren("*", nameof(Button), true, false).OfType<Button>().FirstOrDefault(b => b.Text == text);
            button?.EmitSignal(BaseButton.SignalName.Pressed);
        }

        private void Check(bool condition, string label)
        {
            if (condition)
                _passed++;
            else
                _failed++;
            GameLogger.Log("TEST", $"InGamePlayTest: [{(condition ? "PASS" : "FAIL")}] {label}");
        }

        private void Finish()
        {
            _finished = true;
            GameLogger.Log("TEST", $"[TEST_RESULT] name={ResultName} passed={_passed} failed={_failed}");
            Cthangover.Core.Bootstrap.ApplicationExit.Quit(GetTree(), _failed == 0 ? 0 : 1);
        }
    }
}
