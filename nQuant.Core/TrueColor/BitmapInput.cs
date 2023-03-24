using System;
using System.Collections.Generic;
using System.Text;

namespace nQuant.Core.TrueColor
{
    /// <summary>
    /// 32 bpp bitmap source
    /// </summary>
    public class BitmapInput
    {
        public int Width { get; set; }
        public int Height { get; set; }

        /// <summary>
        /// Lines and line pixels `0xAARRGGBB`
        /// </summary>
        public IEnumerable<int[]> PixelLines { get; set; }
    }
}
