using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;

namespace nQuant.Core.TrueColor
{
    public class BitmapQuantizer : BitmapQuantizerBase
    {
        private static IEnumerable<byte[]> indexedPixels(BitmapInput image, Pixel[] lookups, int alphaThreshold, int maxColors, PaletteColorHistory[] paletteHistogram)
        {
            int pixelsCount = image.Width * image.Height;
            var lineIndexes = new byte[(int)System.Math.Ceiling(image.Width / System.Math.Log(256.0, maxColors))];
            PaletteLookup lookup = new PaletteLookup(lookups);
            --maxColors;
            foreach (var pixelLine in image.PixelLines)
            {
                for (int pixelIndex = 0; pixelIndex < pixelLine.Length; pixelIndex++)
                {
                    Pixel pixel = new Pixel(pixelLine[pixelIndex]);
                    byte bestMatch = (byte)maxColors;
                    if (pixel.Alpha > alphaThreshold)
                    {
                        bestMatch = lookup.GetPaletteIndex(pixel);
                        paletteHistogram[bestMatch].AddPixel(pixel);
                    }
                    switch (maxColors)
                    {
                        case 256 - 1:
                            lineIndexes[pixelIndex] = bestMatch;
                            break;
                        case 16 - 1:
                            if (pixelIndex % 2 == 0)
                            { lineIndexes[pixelIndex / 2] = (byte)(bestMatch << 4); }
                            else
                            { lineIndexes[pixelIndex / 2] |= (byte)(bestMatch & 0x0F); }
                            break;
                        case 2 - 1:
                            if (pixelIndex % 8 == 0)
                            { lineIndexes[pixelIndex / 8] = (byte)(bestMatch << 7); }
                            else
                            { lineIndexes[pixelIndex / 8] |= (byte)((bestMatch & 0x01) << 7 - pixelIndex % 8); }
                            break;
                    }
                }
                yield return lineIndexes;
            }
        }

        internal override BitmapOutput GetQuantizedImage(BitmapInput image, int colorCount, int maxColors, Pixel[] lookups, int alphaThreshold)
        {
            byte bitsPixel;
            switch (maxColors)
            {
                case 256:
                    bitsPixel = 8;
                    break;
                case 16:
                    bitsPixel = 4;
                    break;
                case 2:
                    bitsPixel = 1;
                    break;
                default:
                    throw new QuantizationException(string.Format("The target amount of colors is not supported. Requested {0} colors.", maxColors));
            }
            var result = new BitmapOutput { Width = image.Width, Height = image.Height, BitsPixel = bitsPixel };
            var paletteHistogram = new PaletteColorHistory[colorCount + 1];
            result.PixelLines = indexedPixels(image, lookups, alphaThreshold, maxColors, paletteHistogram)
                .Select(line => line.ToArray())
                .ToArray();
            result.Palette = BuildPalette(new int[maxColors], paletteHistogram);
            return result;
        }

        private static int[] BuildPalette(int[] palette, PaletteColorHistory[] paletteHistogram)
        {
            for (int paletteColorIndex = 0; paletteColorIndex < paletteHistogram.Length; paletteColorIndex++)
            {
                palette[paletteColorIndex] = paletteHistogram[paletteColorIndex].ToNormalizedColor().ToArgb();
            }
            return palette;
        }
    }
}
