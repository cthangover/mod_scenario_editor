namespace Cthangover.Core.UI.Tool.ScenarioEditor.Tests
{
    using System.Linq;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Utils;
    using Godot;

    /// <summary>Mounts <see cref="NewScenarioTestDriver"/> when the game runs with <c>--itest=new_scenario</c>.</summary>
    public class NewScenarioTestInitializer : IModInitializer
    {
        private static bool driverQueued;

        /// <inheritdoc />
        public void OnModLoaded(string modId)
        {
        }

        /// <inheritdoc />
        public void OnModResourcesReady()
        {
            if (driverQueued || !OS.GetCmdlineUserArgs().Any(a => a == "--itest=new_scenario"))
                return;
            driverQueued = true;
            GameLogger.Log("TEST", "NewScenarioTestInitializer: activating new_scenario");
            ModRoot.AddDeferred(new NewScenarioTestDriver());
        }
    }
}
