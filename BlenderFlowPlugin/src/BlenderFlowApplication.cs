namespace Loupedeck.BlenderFlowPlugin
{
    using System;

    public class BlenderFlowApplication : ClientApplication
    {
        public BlenderFlowApplication()
        {
        }

        protected override String GetProcessName() => "Blender";

        protected override String GetBundleName() => "org.blenderfoundation.blender";

        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
    }
}
