using System.Collections.Generic;
using Eq2Auras.Core.Config;

namespace Eq2Auras.Core.Timers
{
    /// Final display-color resolution (SPEC §Timer colors). Palette/greyscale are
    /// designed colors rendered as-is; ActColor is user data softened toward slate.
    public static class ColorPolicy
    {
        // Guild-approved palette v2: sky, amber, teal, rose, indigo.
        public static readonly int[] DefaultPaletteArgb =
        {
            unchecked((int)0xFF56B4E9),
            unchecked((int)0xFFE69F00),
            unchecked((int)0xFF009E73),
            unchecked((int)0xFFE37DA4),
            unchecked((int)0xFF5E6BD8),
        };

        // Light-to-dark grey ramp, legible over the dark backplate at fill alpha.
        public static readonly int[] GreyArgb =
        {
            unchecked((int)0xFFF2F2F2),
            unchecked((int)0xFFC4C4C4),
            unchecked((int)0xFF999999),
            unchecked((int)0xFF787878),
            unchecked((int)0xFF5A5A5A),
        };

        public static int Resolve(ColorSource source, int paletteIndex, int actArgb, IReadOnlyList<int> palette = null)
        {
            IReadOnlyList<int> colors = palette != null && palette.Count > 0 ? palette : DefaultPaletteArgb;
            switch (source)
            {
                case ColorSource.Greyscale: return GreyArgb[paletteIndex % GreyArgb.Length];
                case ColorSource.ActColor: return Soften(actArgb);
                default: return colors[paletteIndex % colors.Count];
            }
        }

        public static int Soften(int argb)
        {
            const double keep = 0.65;
            const int slateR = 110, slateG = 118, slateB = 130;
            int r = (byte)(((argb >> 16) & 0xFF) * keep + slateR * (1 - keep));
            int g = (byte)(((argb >> 8) & 0xFF) * keep + slateG * (1 - keep));
            int b = (byte)((argb & 0xFF) * keep + slateB * (1 - keep));
            return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
        }
    }
}
