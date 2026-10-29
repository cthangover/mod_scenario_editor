namespace Cthangover.Core.UI.Tool.ScenarioEditor
{
    /// <summary>Exposes the scenario editor in the in-game toolbar's tools list.</summary>
    public class ScenarioEditorToolBoxButton : IToolBoxButton
    {
        /// <inheritdoc />
        public string ToolId => "scenario_editor";

        /// <inheritdoc />
        public string IconPath => "";

        /// <inheritdoc />
        public string LocaleKey => "tools/scenario_editor/title";

        /// <inheritdoc />
        public bool IsVisible() => true;
    }
}
