﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace nQuant
{
    public abstract class WuQuantizerBase
    {
        protected const int Alpha = 3;
        protected const int Red = 2;
        protected const int Green = 1;
        protected const int Blue = 0;
        /// <summary><para>Shift color values right this many bits.</para><para>This reduces the granularity of the color maps produced, making it much faster.</para></summary>
        /// 3 = value error of 8 (0 and 7 will look the same to it, 0 and 8 different); Takes ~4MB for color tables; ~.25 -> .50 seconds
        /// 2 = value error of 4; Takes ~64MB for color tables; ~3 seconds
        /// RAM usage roughly estimated with: ( ( 256 >> SidePixShift ) ^ 4 ) * 60
        /// Default SidePixShift = 3
        private const int SidePixShift = 3;
        private const int MaxSideIndex = 256 / (1 << SidePixShift);
        private const int SideSize = MaxSideIndex + 1;

        // The following 2 methods are kept for compatibility.
        public Image QuantizeImage(Bitmap image) { return QuantizeImage(image, 10, 70, 256); }
        public Image QuantizeImage(Bitmap image, int alphaThreshold, int alphaFader) { return QuantizeImage(image, alphaThreshold, alphaFader, 256); }

        public Image QuantizeImage(Bitmap image, int alphaThreshold = 10, int alphaFader = 70, int maxColors = 256)
        {
            var colorCount = maxColors;
            ImageBuffer buffer = new ImageBuffer(image);
            var moments = BuildHistogram(buffer, alphaThreshold, alphaFader);
            CalculateMoments(moments);
            var cubes = SplitData(ref colorCount, maxColors, moments);
            var lookups = BuildLookups(cubes, moments);
            return GetQuantizedImage(buffer, colorCount, maxColors, lookups, alphaThreshold);
        }

        private static ColorMoment[, , ,] BuildHistogram(ImageBuffer sourceImage, int alphaThreshold, int alphaFader)
        {
            var moments = new ColorMoment[SideSize, SideSize, SideSize, SideSize];
            foreach(var pixelLine in sourceImage.PixelLines)
            {
                for (int pixelIndex = 0; pixelIndex < pixelLine.Length; pixelIndex++)
                {
                    Pixel pixel = pixelLine[pixelIndex];
                    byte pixelAlpha = pixel.Alpha;
                    if (pixelAlpha > alphaThreshold)
                    {
                        if (pixelAlpha < 255)
                        {
                            var alpha = pixel.Alpha + (pixel.Alpha % alphaFader);
                            pixelAlpha = (byte)(alpha > 255 ? 255 : alpha);
                        }
                        byte pixelRed = pixel.Red;
                        byte pixelGreen = pixel.Green;
                        byte pixelBlue = pixel.Blue;

                        pixelAlpha = (byte)((pixelAlpha >> SidePixShift) + 1);
                        pixelRed = (byte)((pixelRed >> SidePixShift) + 1);
                        pixelGreen = (byte)((pixelGreen >> SidePixShift) + 1);
                        pixelBlue = (byte)((pixelBlue >> SidePixShift) + 1);
                        moments[pixelAlpha, pixelRed, pixelGreen, pixelBlue].Add(pixel);
                    }
                }
            }
            
            return moments;
        }

        private static void CalculateMoments(ColorMoment[, , ,] moments)
        {
            var xarea = new ColorMoment[SideSize, SideSize];
            var area = new ColorMoment[SideSize];
            for (var alphaIndex = 1; alphaIndex < SideSize; alphaIndex++)
            {
                for (var redIndex = 1; redIndex < SideSize; redIndex++)
                {
                    Array.Clear(area, 0, area.Length);
                    for (var greenIndex = 1; greenIndex < SideSize; greenIndex++)
                    {
                        ColorMoment line = new ColorMoment();
                        for (var blueIndex = 1; blueIndex < SideSize; blueIndex++)
                        {
                            line.AddFast(ref moments[alphaIndex, redIndex, greenIndex, blueIndex]);
                            area[blueIndex].AddFast(ref line);
                            xarea[greenIndex, blueIndex].AddFast(ref area[blueIndex]);

                            ColorMoment moment = moments[alphaIndex - 1, redIndex, greenIndex, blueIndex];
                            moment.AddFast(ref xarea[greenIndex, blueIndex]);
                            moments[alphaIndex, redIndex, greenIndex, blueIndex] = moment;
                        }
                    }
                }
            }
        }

        private static ColorMoment Top(Box cube, int direction, int position, ColorMoment[, , ,] moment)
        {
            switch (direction)
            {
                case Alpha:
                    return (moment[position, cube.RedMaximum, cube.GreenMaximum, cube.BlueMaximum] -
                            moment[position, cube.RedMaximum, cube.GreenMinimum, cube.BlueMaximum] -
                            moment[position, cube.RedMinimum, cube.GreenMaximum, cube.BlueMaximum] +
                            moment[position, cube.RedMinimum, cube.GreenMinimum, cube.BlueMaximum]) -
                           (moment[position, cube.RedMaximum, cube.GreenMaximum, cube.BlueMinimum] -
                            moment[position, cube.RedMaximum, cube.GreenMinimum, cube.BlueMinimum] -
                            moment[position, cube.RedMinimum, cube.GreenMaximum, cube.BlueMinimum] +
                            moment[position, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum]);

                case Red:
                    return (moment[cube.AlphaMaximum, position, cube.GreenMaximum, cube.BlueMaximum] -
                            moment[cube.AlphaMaximum, position, cube.GreenMinimum, cube.BlueMaximum] -
                            moment[cube.AlphaMinimum, position, cube.GreenMaximum, cube.BlueMaximum] +
                            moment[cube.AlphaMinimum, position, cube.GreenMinimum, cube.BlueMaximum]) -
                           (moment[cube.AlphaMaximum, position, cube.GreenMaximum, cube.BlueMinimum] -
                            moment[cube.AlphaMaximum, position, cube.GreenMinimum, cube.BlueMinimum] -
                            moment[cube.AlphaMinimum, position, cube.GreenMaximum, cube.BlueMinimum] +
                            moment[cube.AlphaMinimum, position, cube.GreenMinimum, cube.BlueMinimum]);

                case Green:
                    return (moment[cube.AlphaMaximum, cube.RedMaximum, position, cube.BlueMaximum] -
                            moment[cube.AlphaMaximum, cube.RedMinimum, position, cube.BlueMaximum] -
                            moment[cube.AlphaMinimum, cube.RedMaximum, position, cube.BlueMaximum] +
                            moment[cube.AlphaMinimum, cube.RedMinimum, position, cube.BlueMaximum]) -
                           (moment[cube.AlphaMaximum, cube.RedMaximum, position, cube.BlueMinimum] -
                            moment[cube.AlphaMaximum, cube.RedMinimum, position, cube.BlueMinimum] -
                            moment[cube.AlphaMinimum, cube.RedMaximum, position, cube.BlueMinimum] +
                            moment[cube.AlphaMinimum, cube.RedMinimum, position, cube.BlueMinimum]);

                case Blue:
                    return (moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMaximum, position] -
                            moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMinimum, position] -
                            moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMaximum, position] +
                            moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMinimum, position]) -
                           (moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMaximum, position] -
                            moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMinimum, position] -
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMaximum, position] +
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, position]);

                default:
                    return new ColorMoment();
            }
        }

        private static ColorMoment Bottom(Box cube, int direction, ColorMoment[, , ,] moment)
        {
            switch (direction)
            {
                case Alpha:
                    return (-moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMaximum, cube.BlueMaximum] +
                            moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMaximum] +
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMaximum] -
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMaximum]) -
                           (-moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMaximum, cube.BlueMinimum] +
                            moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMinimum] +
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMinimum] -
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum]);

                case Red:
                    return (-moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMaximum] +
                            moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMaximum] +
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMaximum] -
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMaximum]) -
                           (-moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMinimum] +
                            moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum] +
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMinimum] -
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum]);

                case Green:
                    return (-moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMaximum] +
                            moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMaximum] +
                            moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMaximum] -
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMaximum]) -
                           (-moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMinimum] +
                            moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum] +
                            moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMinimum] -
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum]);

                case Blue:
                    return (-moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMaximum, cube.BlueMinimum] +
                            moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMinimum] +
                            moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMinimum] -
                            moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum]) -
                           (-moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMaximum, cube.BlueMinimum] +
                            moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMinimum] +
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMinimum] -
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum]);

                default:
                    return new ColorMoment();
            }
        }

        private static CubeCut Maximize(ColorMoment[, , ,] moments, Box cube, int direction, byte first, byte last, ColorMoment whole)
        {
            var bottom = Bottom(cube, direction, moments);
            var result = 0.0f;
            byte? cutPoint = null;

            for (var position = first; position < last; ++position)
            {
                var half = bottom + Top(cube, direction, position, moments);
                if (half.Weight == 0) continue;

                var temp = half.WeightedDistance();

                half = whole - half;
                if (half.Weight != 0)
                {
                    temp += half.WeightedDistance();

                    if (temp > result)
                    {
                        result = temp;
                        cutPoint = position;
                    }
                }
            }

            return new CubeCut(cutPoint, result);
        }

        private static bool Cut(ColorMoment[, , ,] moments, ref Box first, ref Box second)
        {
            int direction;
            var whole = Volume(first, moments);
            var maxAlpha = Maximize(moments, first, Alpha, (byte)(first.AlphaMinimum + 1), first.AlphaMaximum, whole);
            var maxRed = Maximize(moments, first, Red, (byte)(first.RedMinimum + 1), first.RedMaximum, whole);
            var maxGreen = Maximize(moments, first, Green, (byte)(first.GreenMinimum + 1), first.GreenMaximum, whole);
            var maxBlue = Maximize(moments, first, Blue, (byte)(first.BlueMinimum + 1), first.BlueMaximum, whole);

            if ((maxAlpha.Value >= maxRed.Value) && (maxAlpha.Value >= maxGreen.Value) && (maxAlpha.Value >= maxBlue.Value))
            {
                direction = Alpha;
                if (!maxAlpha.Position.HasValue) return false;
            }
            else if ((maxRed.Value >= maxAlpha.Value) && (maxRed.Value >= maxGreen.Value) && (maxRed.Value >= maxBlue.Value))
                direction = Red;
            else
            {
                if ((maxGreen.Value >= maxAlpha.Value) && (maxGreen.Value >= maxRed.Value) && (maxGreen.Value >= maxBlue.Value))
                    direction = Green;
                else
                    direction = Blue;
            }

            second.AlphaMaximum = first.AlphaMaximum;
            second.RedMaximum = first.RedMaximum;
            second.GreenMaximum = first.GreenMaximum;
            second.BlueMaximum = first.BlueMaximum;

            switch (direction)
            {
                case Alpha:
                    second.AlphaMinimum = first.AlphaMaximum = maxAlpha.Position.Value;
                    second.RedMinimum = first.RedMinimum;
                    second.GreenMinimum = first.GreenMinimum;
                    second.BlueMinimum = first.BlueMinimum;
                    break;

                case Red:
                    second.RedMinimum = first.RedMaximum = maxRed.Position.Value;
                    second.AlphaMinimum = first.AlphaMinimum;
                    second.GreenMinimum = first.GreenMinimum;
                    second.BlueMinimum = first.BlueMinimum;
                    break;

                case Green:
                    second.GreenMinimum = first.GreenMaximum = maxGreen.Position.Value;
                    second.AlphaMinimum = first.AlphaMinimum;
                    second.RedMinimum = first.RedMinimum;
                    second.BlueMinimum = first.BlueMinimum;
                    break;

                case Blue:
                    second.BlueMinimum = first.BlueMaximum = maxBlue.Position.Value;
                    second.AlphaMinimum = first.AlphaMinimum;
                    second.RedMinimum = first.RedMinimum;
                    second.GreenMinimum = first.GreenMinimum;
                    break;
            }

            first.Size = (first.AlphaMaximum - first.AlphaMinimum) * (first.RedMaximum - first.RedMinimum) * (first.GreenMaximum - first.GreenMinimum) * (first.BlueMaximum - first.BlueMinimum);
            second.Size = (second.AlphaMaximum - second.AlphaMinimum) * (second.RedMaximum - second.RedMinimum) * (second.GreenMaximum - second.GreenMinimum) * (second.BlueMaximum - second.BlueMinimum);

            return true;
        }

        private static float CalculateVariance(ColorMoment[, , ,] moments, Box cube)
        {
            ColorMoment volume = Volume(cube, moments);
            return volume.Variance();
        }

        private static ColorMoment Volume(Box cube, ColorMoment[, , ,] moment)
        {
            return (moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMaximum, cube.BlueMaximum] -
                    moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMaximum] -
                    moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMaximum] +
                    moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMaximum] -
                    moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMaximum, cube.BlueMaximum] +
                    moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMaximum] +
                    moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMaximum] -
                    moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMaximum]) -

                   (moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMaximum, cube.BlueMinimum] -
                    moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMaximum, cube.BlueMinimum] -
                    moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMinimum] +
                    moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMinimum] -
                    moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMinimum] +
                    moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMinimum] +
                    moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum] -
                    moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum]);
        }

        private static Box[] SplitData(ref int colorCount, int maxColors, ColorMoment[, , ,] moments)
        {
            --colorCount;
            var next = 0;
            var volumeVariance = new float[maxColors];
            var cubes = new Box[maxColors];
            cubes[0].AlphaMaximum = MaxSideIndex;
            cubes[0].RedMaximum = MaxSideIndex;
            cubes[0].GreenMaximum = MaxSideIndex;
            cubes[0].BlueMaximum = MaxSideIndex;
            for (var cubeIndex = 1; cubeIndex < colorCount; ++cubeIndex)
            {
                if (Cut(moments, ref cubes[next], ref cubes[cubeIndex]))
                {
                    volumeVariance[next] = cubes[next].Size > 1 ? CalculateVariance(moments, cubes[next]) : 0.0f;
                    volumeVariance[cubeIndex] = cubes[cubeIndex].Size > 1 ? CalculateVariance(moments, cubes[cubeIndex]) : 0.0f;
                }
                else
                {
                    volumeVariance[next] = 0.0f;
                    cubeIndex--;
                }

                next = 0;
                var temp = volumeVariance[0];

                for (var index = 1; index <= cubeIndex; ++index)
                {
                    if (volumeVariance[index] <= temp) continue;
                    temp = volumeVariance[index];
                    next = index;
                }

                if (temp > 0.0) continue;
                colorCount = cubeIndex + 1;
                break;
            }
            return cubes.Take(colorCount).ToArray();
        }

        private static Pixel[] BuildLookups(Box[] cubes, ColorMoment[, , ,] moments)
        {
            Pixel[] lookups = new Pixel[cubes.Length];

            for (int cubeIndex = 0; cubeIndex < cubes.Length; cubeIndex++)
            {
                var volume = Volume(cubes[cubeIndex], moments);

                if (volume.Weight <= 0) continue;

                var lookup = new Pixel
                    {
                        Alpha = (byte)(volume.Alpha / volume.Weight),
                        Red = (byte)(volume.Red / volume.Weight),
                        Green = (byte)(volume.Green / volume.Weight),
                        Blue = (byte)(volume.Blue / volume.Weight)
                    };
                lookups[cubeIndex] = lookup;
            }
            return lookups;
        }

        internal abstract Image GetQuantizedImage(ImageBuffer image, int colorCount, int maxColors, Pixel[] lookups, int alphaThreshold);
    }
}