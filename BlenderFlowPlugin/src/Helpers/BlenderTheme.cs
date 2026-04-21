namespace Loupedeck.BlenderFlowPlugin
{
    internal static class BlenderTheme
    {
        // Panel backgrounds (Blender's Blender Dark theme)
        public static readonly BitmapColor PanelBg = new BitmapColor(40, 40, 40);
        public static readonly BitmapColor PanelBgDim = new BitmapColor(28, 28, 28);

        // Icon foreground (Blender icon tint)
        public static readonly BitmapColor Icon = new BitmapColor(210, 210, 210);
        public static readonly BitmapColor IconDim = new BitmapColor(120, 120, 120);
        public static readonly BitmapColor IconBright = new BitmapColor(245, 245, 245);

        // Brand / state accents
        public static readonly BitmapColor Orange = new BitmapColor(232, 125, 18);       // active object outline
        public static readonly BitmapColor OrangeDeep = new BitmapColor(140, 72, 8);
        public static readonly BitmapColor OrangeTint = new BitmapColor(60, 34, 10);     // dark orange wash
        public static readonly BitmapColor EditBlue = new BitmapColor(86, 128, 194);     // edit-mode blue
        public static readonly BitmapColor EditBlueTint = new BitmapColor(20, 42, 72);
        public static readonly BitmapColor SculptRed = new BitmapColor(206, 96, 96);     // sculpt pink/red
        public static readonly BitmapColor SculptTint = new BitmapColor(60, 24, 24);
        public static readonly BitmapColor AiPurple = new BitmapColor(168, 116, 216);    // AI accent
        public static readonly BitmapColor AiPurpleTint = new BitmapColor(44, 28, 62);

        // Status
        public static readonly BitmapColor Success = new BitmapColor(94, 168, 88);
        public static readonly BitmapColor Warning = new BitmapColor(216, 166, 72);
        public static readonly BitmapColor Danger = new BitmapColor(196, 70, 70);

        // Blender's standard axis colors
        public static readonly BitmapColor AxisX = new BitmapColor(218, 82, 60);
        public static readonly BitmapColor AxisY = new BitmapColor(106, 184, 64);
        public static readonly BitmapColor AxisZ = new BitmapColor(62, 138, 216);
    }
}
