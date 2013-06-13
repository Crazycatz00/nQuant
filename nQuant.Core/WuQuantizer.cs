﻿using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace nQuant
{
    public class WuQuantizer : WuQuantizerBase, IWuQuantizer
    {
        private IEnumerable<byte[]> indexedPixels(ImageBuffer image, Pixel[] lookups, int alphaThreshold, PaletteColorHistory[] paletteHistogram)
        {
            int pixelsCount = image.Image.Width * image.Image.Height;
            var lineIndexes = new byte[image.Image.Width];
            PaletteLookup lookup = new PaletteLookup(lookups);
            foreach (var pixelLine in image.PixelLines)
            {
                for (int pixelIndex = 0; pixelIndex < pixelLine.Length; pixelIndex++)
                {
                    Pixel pixel = pixelLine[pixelIndex];
                    byte bestMatch = AlphaColor;
                    if (pixel.Alpha > alphaThreshold)
                    {
                        bestMatch = lookup.GetPaletteIndex(pixel);
                        paletteHistogram[bestMatch].AddPixel(pixel);
                    }
                    lineIndexes[pixelIndex] = bestMatch;
                }
                yield return lineIndexes;
            }
        }

        internal override Image GetQuantizedImage(ImageBuffer image, int colorCount, Pixel[] lookups, int alphaThreshold)
        {
            var result = new Bitmap(image.Image.Width, image.Image.Height, PixelFormat.Format8bppIndexed);
            var resultBuffer = new ImageBuffer(result);
            var paletteHistogram = new PaletteColorHistory[colorCount + 1];
            resultBuffer.UpdatePixelIndexes(indexedPixels(image, lookups, alphaThreshold, paletteHistogram));
            result.Palette = BuildPalette(result.Palette, paletteHistogram);
            return result;
        }

        private ColorPalette BuildPalette(ColorPalette palette, PaletteColorHistory[] paletteHistogram)
        {
            for (int paletteColorIndex = 0; paletteColorIndex < paletteHistogram.Length; paletteColorIndex++)
            {
                palette.Entries[paletteColorIndex] = paletteHistogram[paletteColorIndex].ToNormalizedColor();
            }
            return palette;
        }
    }
}
