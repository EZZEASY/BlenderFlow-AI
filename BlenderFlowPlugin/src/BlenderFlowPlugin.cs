namespace Loupedeck.BlenderFlowPlugin
{
    using System;

    public class BlenderFlowPlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => false;
        public override Boolean HasNoApplication => false;

        public BlenderFlowPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        public override void Load()
        {
            PluginLog.Info("BlenderFlow AI plugin loaded");
        }

        public override void Unload()
        {
            PluginLog.Info("BlenderFlow AI plugin unloaded");
        }
    }
}
