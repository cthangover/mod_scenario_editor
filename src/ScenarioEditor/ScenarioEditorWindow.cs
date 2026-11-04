using System;
using System.Text.RegularExpressions;
using Cthangover.Core.Scenes;
using Cthangover.Mods.Dialog;
using Cthangover.Core.UI.Theming;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.Theming;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Tools.Common;
using Cthangover.Tools.Contracts;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Core.UI.Tool.ScenarioEditor
{
    /// <summary>
    /// Tool window hosting the scenario editor: a searchable tree of every
    /// mod's .scenario files, tabbed code/graph editors, a metadata panel that
    /// follows the text as it is typed, background thumbnails, plus a
    /// play-test mode that runs the current scenario in-game.
    /// <para/>
    /// Shortcuts: Ctrl+S saves the active file, F5 play-tests it.
    /// </summary>
    public partial class ScenarioEditorWindow : ToolWindow, IScenarioResourceEditor
    {
        private const double InfoRefreshDelay = 0.4;

        private Control _editLayout;

        private ScenarioFileTreePanel _fileTreePanel;
        private ScenarioEditTabs _editTabs;
        private ScenarioInfoPanel _infoPanel;
        private ScenarioThumbnailPanel _thumbPanel;
        private Timer _infoTimer;
        private bool _infoRefreshPending;

        private Button _saveBtn;
        private Button _openFileBtn;
        private Button _playBtn;
        private Label _filePathLabel;

        private bool _playActive;
        private bool _lastSaveSucceeded;
        private bool _ambientPreviewActive;
        private bool _ambientPreviewSeen;
        private string _ambientPreviewId;
        private IBackgroundScenarioTools _observationProvider;
        private string _pendingKey;
        private string _returnRoute;
        private string _returnScene;
        private CanvasLayer _stopOverlay;

        public ScenarioEditorWindow() : base("tools/scenario_editor/title")
        {
            CthangoverTheme.Apply(this);
            BuildUI();
            FocusEntered += _editTabs.ActivateTarget;
            VisibilityChanged += OnObservationVisibilityChanged;
            TreeExiting += OnObservationLeaving;
            SetHint(ToolUi.Tr("tools/scenario_editor/hint"));
        }

        /// <summary>Opens with the graph view so the editor starts on the cross-scene map, or on the file asked for by <see cref="OpenScenario"/>.</summary>
        public override void _Ready()
        {
            OnObservationVisibilityChanged();
            _editTabs.SetViewToGraph();
            if (_pendingKey != null)
                OpenScenario(_pendingKey);
        }

        /// <summary>
        /// Opens a scenario file (key <c>mod:path</c>) in a text tab and selects it in the
        /// tree — for other tools, e.g. the scene editor's "open" button. Before the window
        /// is in the tree the key waits for <see cref="_Ready"/>, which would otherwise
        /// switch the view back to the graph.
        /// </summary>
        public void OpenScenario(string key)
        {
            if (!IsInsideTree())
            {
                _pendingKey = key;
                return;
            }

            _pendingKey = null;
            _fileTreePanel.SelectFile(key);
            _editTabs.OpenFile(key);
        }

        /// <inheritdoc />
        public void OpenScenario(string modId, string relativePath)
            => OpenScenario(ScenarioFileService.MakeKey(modId, relativePath));

        /// <summary>Key of the scenario open in the active tab (<c>mod:path</c>), or null when the graph is shown.</summary>
        public string OpenKey => _editTabs.HasActiveFile ? _editTabs.CurrentKey : null;

        /// <summary>
        /// Writes a new scenario file <c>scenarios/&lt;name&gt;.scenario</c> into a folder mod, opens it in a text
        /// tab and returns its key; null (with a status line) when the name is not a plain file name, the file
        /// exists or the mod is read-only. The file holds one placeholder line, so the command palette can insert
        /// its lines into it at once, and nothing is written until the author saves.
        /// </summary>
        public string CreateScenario(string modId, string name)
        {
            if (string.IsNullOrEmpty(modId) || !Regex.IsMatch(name ?? string.Empty, "^[a-z0-9_]+$"))
            {
                SetStatus(ToolUi.Tr("tools/scenario_editor/name_invalid"), ToolStatusKind.Error);
                return null;
            }

            var relativePath = ScenariosDir + "/" + name + ScenarioExtension;
            if (ModFileService.ReadText(modId, relativePath) != null)
            {
                SetStatus(string.Format(ToolUi.Tr("tools/scenario_editor/name_exists"), ScenarioFileService.GetDisplayPath(modId, relativePath)), ToolStatusKind.Error);
                return null;
            }

            if (!ModFileService.WriteText(modId, relativePath, "text \"…\"\nend\n"))
            {
                SetStatus(ToolUi.Tr("tools/scenario_editor/readonly_zip"), ToolStatusKind.Error);
                return null;
            }

            var key = ScenarioFileService.MakeKey(modId, relativePath);
            _fileTreePanel.Reload();
            SceneManager.ReloadContent();
            OpenScenario(key);
            SetStatus(string.Format(ToolUi.Tr("tools/scenario_editor/created"), ScenarioFileService.GetDisplayPath(modId, relativePath)), ToolStatusKind.Ok);
            GameLogger.Log("SCENARIO_EDITOR", $"Created: {modId}/{relativePath}");
            return key;
        }

        /// <summary>Saves the active file as Ctrl+S does; false when no file is open or the save failed.</summary>
        public bool SaveActive()
        {
            if (!_editTabs.HasActiveFile)
                return false;
            OnSavePressed();
            return _lastSaveSucceeded;
        }

        private const string ScenariosDir = "scenarios";
        private const string ScenarioExtension = ".scenario";

        /// <summary>Opens the new-scenario dialog: a folder mod and a file name.</summary>
        private void OnNewPressed()
        {
            var writable = ModFileService.ListWritableMods();
            if (writable.Count == 0)
            {
                SetStatus(ToolUi.Tr("tools/scenario_editor/readonly_zip"), ToolStatusKind.Error);
                return;
            }

            var modSelect = new OptionButton { CustomMinimumSize = new Vector2(320, 0) };
            var preferred = _editTabs.HasActiveFile ? _editTabs.CurrentModId : null;
            for (var i = 0; i < writable.Count; i++)
            {
                modSelect.AddItem(writable[i]);
                if (writable[i] == preferred)
                    modSelect.Selected = i;
            }

            var nameEdit = new LineEdit { PlaceholderText = ToolUi.Tr("tools/scenario_editor/new_name_tip"), CustomMinimumSize = new Vector2(320, 0) };
            var box = new VBoxContainer();
            ToolUi.Field(box, "tools/scenario_editor/new_mod", modSelect);
            ToolUi.Field(box, "tools/scenario_editor/new_name", nameEdit);

            var dialog = new ConfirmationDialog
            {
                Title = ToolUi.Tr("tools/scenario_editor/new_title"),
                OkButtonText = ToolUi.Tr("tools/scenario_editor/new_create"),
                Exclusive = true,
            };
            dialog.AddChild(box);
            dialog.Confirmed += () =>
            {
                CreateScenario(modSelect.GetItemText(modSelect.Selected), nameEdit.Text.Trim());
                dialog.QueueFree();
            };
            dialog.Canceled += dialog.QueueFree;
            AddChild(dialog);
            dialog.PopupCentered(new Vector2I(480, 220));
        }

        /// <summary>Returns from play-test mode as soon as the in-game test play ends.</summary>
        public override void _Process(double delta)
        {
            if (_observationProvider != BackgroundScenarios.Provider) OnObservationVisibilityChanged();
            if (_ambientPreviewActive)
            {
                var found = false;
                if (BackgroundScenarios.Provider != null)
                    foreach (var run in BackgroundScenarios.Provider.Executions()) if (run.ScenarioId == _ambientPreviewId) found = true;
                if (found) _ambientPreviewSeen = true;
                else if (_ambientPreviewSeen || BackgroundScenarios.Provider == null) ReturnFromAmbientPreview();
            }
            if (_playActive && !SceneManager.IsTestPlayActive)
                ReturnFromPlay();
        }

        private void OnObservationVisibilityChanged()
        {
            if (_observationProvider != null) _observationProvider.Observe(this, false);
            _observationProvider = BackgroundScenarios.Provider;
            _observationProvider?.Observe(this, Visible && IsInsideTree());
        }
        private void OnObservationLeaving()
        {
            _observationProvider?.Observe(this, false);
            _observationProvider = null;
        }

        private void BuildUI()
        {
            _editLayout = new Control
            {
                AnchorRight = 1f,
                AnchorBottom = 1f
            };
            AddChild(_editLayout);

            var outerVBox = CreateFillContainer();
            _editLayout.AddChild(outerVBox);

            var toolbar = CreateToolbar();
            outerVBox.AddChild(toolbar);

            _saveBtn = ToolUi.Button("tools/common/save", OnSavePressed, ButtonKind.Primary, "tools/common/save_tip");
            toolbar.AddChild(_saveBtn);
            _playBtn = ToolUi.Button("tools/scenario_editor/play", OnPlayPressed, ButtonKind.Secondary, "tools/scenario_editor/play_tooltip");
            toolbar.AddChild(_playBtn);
            _openFileBtn = ToolUi.Button("tools/scenario_editor/open_file", OnOpenFilePressed, ButtonKind.Secondary, "tools/scenario_editor/open_file_tip");
            toolbar.AddChild(_openFileBtn);
            toolbar.AddChild(ToolUi.Divider());
            toolbar.AddChild(ToolUi.Button("tools/scenario_editor/new", OnNewPressed, ButtonKind.Secondary, "tools/scenario_editor/new_tip"));
            toolbar.AddChild(ToolUi.Button("tools/scenario_editor/rescan", OnRescanPressed, ButtonKind.Secondary, "tools/scenario_editor/rescan_tip"));

            _filePathLabel = new Label
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
            };
            _filePathLabel.AddThemeFontOverride("font", ToolUi.Mono());
            _filePathLabel.AddThemeColorOverride("font_color", UiTokens.Muted);
            _filePathLabel.AddThemeFontSizeOverride("font_size", UiTokens.FontBodySmall);
            toolbar.AddChild(_filePathLabel);

            var mainSplit = new HSplitContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            outerVBox.AddChild(mainSplit);

            var leftVBox = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            var leftPanel = CreateSidebar(leftVBox, 320);
            mainSplit.AddChild(leftPanel);

            var tabsContainer = new TabContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 420)
            };
            leftVBox.AddChild(tabsContainer);

            _fileTreePanel = (ScenarioFileTreePanel)TscnScenes.LoadAndBuild("scenes/scenario_file_tree_panel.tscn");
            _fileTreePanel.FileSelected += OnFileSelected;
            tabsContainer.AddChild(_fileTreePanel);
            tabsContainer.SetTabTitle(0, ToolUi.Tr("tools/scenario_editor/tab_files"));

            _infoPanel = (ScenarioInfoPanel)TscnScenes.LoadAndBuild("scenes/scenario_info_panel.tscn");
            tabsContainer.AddChild(_infoPanel);
            tabsContainer.SetTabTitle(1, ToolUi.Tr("tools/scenario_editor/tab_info"));

            leftVBox.AddChild(ToolUi.Section("tools/scenario_editor/bg_previews"));
            _thumbPanel = (ScenarioThumbnailPanel)TscnScenes.LoadAndBuild("scenes/scenario_thumbnail_panel.tscn");
            leftVBox.AddChild(_thumbPanel);

            _editTabs = new ScenarioEditTabs(new ScenarioGraphView());
            _editTabs.TextTabSelected += OnTextTabSelected;
            _editTabs.GraphTabSelected += OnGraphTabSelected;
            _editTabs.DirtyChanged += OnDirtyChanged;
            _editTabs.GraphFileRequested += OnGraphFileRequested;
            _editTabs.BufferChanged += OnBufferChanged;
            mainSplit.AddChild(_editTabs);

            _infoTimer = new Timer { OneShot = true, WaitTime = InfoRefreshDelay };
            _infoTimer.Timeout += () => _infoPanel.Update(_editTabs.CurrentText);
            _infoTimer.TreeEntered += OnInfoTimerEntered;
            AddChild(_infoTimer);
        }

        private void OnBufferChanged()
        {
            // Buffer signals can arrive while the window is detached or entering the tree.
            _infoRefreshPending = true;
            if (_infoTimer.IsInsideTree())
                OnInfoTimerEntered();
        }

        private void OnInfoTimerEntered()
        {
            if (!_infoRefreshPending)
                return;

            _infoRefreshPending = false;
            _infoTimer.Start();
        }

        private void OnFileSelected(string filePath)
        {
            _editTabs.OpenFile(filePath);
        }

        private void OnGraphFileRequested(string filePath)
        {
            _fileTreePanel.SelectFile(filePath);
        }

        private void OnRescanPressed()
        {
            _fileTreePanel.Reload();
            _editTabs.RefreshGraph();
            SetStatus(ToolUi.Tr("tools/scenario_editor/rescanned"));
        }

        private void OnTextTabSelected(string text)
        {
            var hasFile = _editTabs.CurrentKey != null;
            _filePathLabel.Text = hasFile
                ? ScenarioFileService.GetDisplayPath(_editTabs.CurrentModId, _editTabs.CurrentRelativePath) : "";
            SetFileButtonsEnabled(hasFile);
            _infoPanel.Update(text);
            _thumbPanel.Load(text);
        }

        private void OnGraphTabSelected()
        {
            _filePathLabel.Text = "";
            SetFileButtonsEnabled(false);
            _infoPanel.Clear();
            _thumbPanel.Clear();
            _editTabs.RefreshGraph();
        }

        private void SetFileButtonsEnabled(bool enabled)
        {
            _saveBtn.Disabled = !enabled;
            _openFileBtn.Disabled = !enabled;
            _playBtn.Disabled = !enabled;
        }

        private void OnDirtyChanged(bool anyDirty)
        {
            if (anyDirty)
                SetDirty();
            else
                MarkClean();
        }

        /// <summary>Ctrl+S.</summary>
        protected override void OnSaveShortcut() => OnSavePressed();

        /// <summary>F5 play-tests the active scenario.</summary>
        protected override bool HandleKey(InputEventKey key)
        {
            if (key.Keycode != Key.F5 || !_editTabs.HasActiveFile)
                return false;

            OnPlayPressed();
            return true;
        }

        private void OnSavePressed()
        {
            _lastSaveSucceeded = false;
            if (!_editTabs.HasActiveFile)
                return;

            var modId = _editTabs.CurrentModId;
            var relativePath = _editTabs.CurrentRelativePath;
            var text = _editTabs.CurrentText;
            try
            {
                if (BackgroundScenarios.Provider != null && !BackgroundScenarios.Provider.Validate(modId, relativePath, text, out var validationError))
                {
                    SetStatus(validationError, ToolStatusKind.Error);
                    return;
                }
                if (!ScenarioFileService.WriteAllText(modId, relativePath, text))
                {
                    SetStatus(ToolUi.Tr("tools/scenario_editor/readonly_zip"), ToolStatusKind.Error);
                    return;
                }

                _editTabs.SaveCurrentFile();
                _lastSaveSucceeded = true;
                // The running game caches scenario headers and texts: drop them, so a new or
                // edited scenario plays on the next entry into its scene without a mod reload.
                SceneManager.ReloadContent();
                SetStatus(string.Format(ToolUi.Tr("tools/common/saved"), ScenarioFileService.GetDisplayPath(modId, relativePath)), ToolStatusKind.Ok);
                _infoPanel.Update(text);
                _thumbPanel.Load(text);
                GameLogger.Log("SCENARIO_EDITOR", $"Saved: {modId}/{relativePath}");
            }
            catch (Exception ex)
            {
                GameLogger.Log("SCENARIO_EDITOR", $"Save failed: {ex.Message}", LogLevel.Error);
                SetStatus(string.Format(ToolUi.Tr("tools/scenario_editor/save_error"), ex.Message), ToolStatusKind.Error);
            }
        }

        private void OnOpenFilePressed()
        {
            if (_editTabs.CurrentKey == null)
                return;

            var path = ScenarioFileService.GetAbsolutePath(_editTabs.CurrentModId, _editTabs.CurrentRelativePath);
            if (string.IsNullOrEmpty(path))
            {
                SetStatus(ToolUi.Tr("tools/scenario_editor/readonly_zip"), ToolStatusKind.Error);
                return;
            }

            var err = OS.ShellOpen(path);
            if (err != Error.Ok)
            {
                GameLogger.Log("SCENARIO_EDITOR", $"ShellOpen '{path}' failed: {err}", LogLevel.Error);
                SetStatus($"{ToolUi.Tr("tools/scenario_editor/open_file_error")}: {err}", ToolStatusKind.Error);
            }
        }

        private void OnPlayPressed()
        {
            if (!_editTabs.HasActiveFile)
                return;

            var text = _editTabs.CurrentText;
            var metadata = ScenarioFileService.ParseMetadata(text);
            var (header, _) = Cthangover.Core.Scenarios.ScenarioMetadata.ParseMetadata(text);
            if (header.TryGetValue("channel", out var channel) && channel == "ambient")
            {
                var tools = BackgroundScenarios.Provider;
                if (tools == null) { SetStatus(ToolUi.Tr("tools/scenario_editor/ambient_missing"), ToolStatusKind.Error); return; }
                if (!tools.Validate(_editTabs.CurrentModId, _editTabs.CurrentRelativePath, text, out var error)
                    || !tools.Play(_editTabs.CurrentModId, _editTabs.CurrentRelativePath, text, false, out error))
                { SetStatus(error, ToolStatusKind.Error); return; }
                _ambientPreviewId = _editTabs.CurrentRelativePath.Substring(10, _editTabs.CurrentRelativePath.Length - 19);
                _ambientPreviewActive = true; _ambientPreviewSeen = false;
                _stopOverlay = CreateStopOverlay();
                GetTree().Root.AddChild(_stopOverlay);
                Hide();
                return;
            }
            if (header.ContainsKey("patch_of"))
            { SetStatus(ToolUi.Tr("tools/scenario_editor/ambient_patch_play"), ToolStatusKind.Error); return; }

            if (metadata.Scene == "-")
            {
                SetStatus(ToolUi.Tr("tools/scenario_editor/no_scene_error"), ToolStatusKind.Error);
                return;
            }

            var modId = _editTabs.CurrentModId;
            var relativePath = _editTabs.CurrentRelativePath;
            try
            {
                if (ScenarioFileService.WriteAllText(modId, relativePath, text))
                {
                    _editTabs.SaveCurrentFile();
                }
                else
                {
                    GameLogger.Log("SCENARIO_EDITOR",
                        $"Test play: '{modId}/{relativePath}' is read-only, playing from the editor buffer",
                        LogLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(ToolUi.Tr("tools/scenario_editor/save_error"), ex.Message), ToolStatusKind.Error);
                return;
            }

            SceneManager.IsTestPlayActive = true;
            SceneManager.TestScenarioText = text;

            GameLogger.Log("SCENARIO_EDITOR", $"Test play START: scene='{metadata.Scene}', queue={text.Split('\n').Length} lines");

            Visible = false;

            // The screen the editor was opened from (a mod menu, a mod
            // route) is freed by the shell load below; remember its route so
            // the play-test returns there instead of to the engine's main menu.
            var sceneService = GetNodeOrNull<GodotSceneService>("/root/GodotSceneService");
            var current = sceneService?.CurrentGodotSceneName;
            _returnRoute = !string.IsNullOrEmpty(current) && SceneRouteRegistry.TryGet(current, out _) ? current : null;

            // Opened inside a running game: come back into the scene the
            // player was in, not to the main menu (there is no way back to the
            // editor from there).
            var sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
            var gameScene = sceneManager?.CurrentSceneName;
            _returnScene = _returnRoute == null && current != "mainmenu" && !string.IsNullOrEmpty(gameScene) && sceneManager.HasScene(gameScene)
                ? gameScene
                : null;

            _stopOverlay = CreateStopOverlay();
            var tree = Godot.Engine.GetMainLoop() as SceneTree;
            tree?.Root.AddChild(_stopOverlay);

            if (sceneManager != null)
            {
                sceneManager.Initialize();
                sceneManager.PendingSceneName = metadata.Scene;
            }

            sceneService?.LoadShell();

            _playActive = true;
        }

        private void OnStopPressed()
        {
            if (_ambientPreviewActive)
            {
                BackgroundScenarios.Provider?.Command(_ambientPreviewId, "cancel_preview", out _);
                ReturnFromAmbientPreview();
                return;
            }
            SceneManager.IsTestPlayActive = false;
            ReturnFromPlay();
        }

        private void ReturnFromAmbientPreview()
        {
            _ambientPreviewActive = false; _ambientPreviewSeen = false;
            if (_stopOverlay != null) { _stopOverlay.QueueFree(); _stopOverlay = null; }
            Show(); GrabFocus();
            SetStatus(ToolUi.Tr("tools/scenario_editor/play_stopped"));
        }
        protected override void Cleanup()
        {
            if (_ambientPreviewActive) BackgroundScenarios.Provider?.Command(_ambientPreviewId, "cancel_preview", out _);
            _ambientPreviewActive = false;
            if (_stopOverlay != null) { _stopOverlay.QueueFree(); _stopOverlay = null; }
        }

        private void ReturnFromPlay()
        {
            _playActive = false;
            Visible = true;

            if (_stopOverlay != null)
            {
                _stopOverlay.QueueFree();
                _stopOverlay = null;
            }

            var sceneService = GetNodeOrNull<GodotSceneService>("/root/GodotSceneService");
            var sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
            if (_returnRoute != null && SceneRouteRegistry.TryGet(_returnRoute, out var route))
            {
                sceneService?.SwitchRoute(route);
            }
            else if (_returnScene != null && sceneManager != null)
            {
                sceneManager.PendingSceneName = _returnScene;
                sceneService?.LoadShell();
            }
            else
            {
                sceneService?.SwitchToMenu();
            }

            _returnRoute = null;
            _returnScene = null;
            SetStatus(ToolUi.Tr("tools/scenario_editor/play_stopped"));
        }

        /// <summary>Stop button shown over the running game while the scenario is play-tested.</summary>
        private CanvasLayer CreateStopOverlay()
        {
            var layer = new CanvasLayer { Layer = 128 };

            var btn = ToolUi.Button("tools/scenario_editor/stop", OnStopPressed, ButtonKind.Danger);
            btn.Name = "ReturnToEditor";
            btn.Theme = ToolTheme.Get();
            btn.CustomMinimumSize = new Vector2(120, 36);

            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_top", UiTokens.Space3);
            margin.AddThemeConstantOverride("margin_left", UiTokens.Space3);
            margin.AddChild(btn);

            layer.AddChild(margin);
            return layer;
        }
    }
}
