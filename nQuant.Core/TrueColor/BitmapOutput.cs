using System;
using System.Collections.Generic;
using System.Text;

namespace nQuant.Core.TrueColor
{
    public class BitmapOutput
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public byte BitsPixel { get; set; }
        public IEnumerable<byte[]> PixelLines { get; set; }
        public int[] Palette { get; set; }
    }
}
