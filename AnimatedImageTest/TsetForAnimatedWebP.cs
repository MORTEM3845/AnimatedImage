using AnimatedImage;
using AnimatedImage.Formats;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using SkiaSharp;

namespace AnimatedImageTest
{
    public class TsetForAnimatedWebP
    {
        [TestCase("animated-webp-control-640x480.webp", 640, 480)]
        [TestCase("animated-webp-large-1920x2016.webp", 1920, 2016)]
        public void LargeFramePerformance(string filename, int expectedWidth, int expectedHeight)
        {
            using var imageStream = Open(filename);
            using var renderer = new WebpRenderer(imageStream, new BitmapFaceFactory());
            Assert.That(renderer.Width, Is.EqualTo(expectedWidth));
            Assert.That(renderer.Height, Is.EqualTo(expectedHeight));
            Assert.That(renderer.FrameCount, Is.EqualTo(24));

            for (int i = 0; i < renderer.FrameCount; i++) renderer.ProcessFrame(i);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            const int passes = 5;
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 0; i < renderer.FrameCount; i++) renderer.ProcessFrame(i);
            }
            stopwatch.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            int decodedFrames = passes * renderer.FrameCount;

            TestContext.Progress.WriteLine(
                $"{expectedWidth}x{expectedHeight}, {renderer.FrameCount} frames: " +
                $"{stopwatch.Elapsed.TotalMilliseconds / decodedFrames:F2} ms/frame, " +
                $"{allocated / decodedFrames:N0} managed bytes/frame");
            Assert.That(allocated, Is.LessThanOrEqualTo(4096), "Frame processing should reuse its full-canvas transfer buffer.");
        }

        [TestCase("animated-webp-control-640x480.webp", 640, 480)]
        [TestCase("animated-webp-large-1920x2016.webp", 1920, 2016)]
        public void LargeFrameSequencePreservesRenderedPixels(string filename, int expectedWidth, int expectedHeight)
        {
            using var imageStream = Open(filename);
            using var renderer = new WebpRenderer(imageStream, new BitmapFaceFactory());
            Assert.That(renderer.Width, Is.EqualTo(expectedWidth));
            Assert.That(renderer.Height, Is.EqualTo(expectedHeight));
            Assert.That(renderer.FrameCount, Is.EqualTo(24));

            var bitmap = ((BitmapFace)renderer.Current).Bitmap;
            int y = expectedHeight / 2;
            for (int frame = 0; frame < renderer.FrameCount; frame++)
            {
                renderer.ProcessFrame(frame);
                if (frame == 12 && Environment.GetEnvironmentVariable("ANIMATEDIMAGE_CAPTURE_DIR") is string captureDirectory)
                {
                    Directory.CreateDirectory(captureDirectory);
                    using var image = SKImage.FromBitmap(bitmap);
                    using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                    using var output = File.Create(Path.Combine(captureDirectory, $"{expectedWidth}x{expectedHeight}-frame-12.png"));
                    png.SaveTo(output);
                }
                int barX = (expectedWidth + 48 - ((frame * 29) % expectedWidth)) % expectedWidth;
                var pixel = bitmap.GetPixel(barX, y);
                Assert.That(pixel.Red, Is.EqualTo(245).Within(35), $"Frame {frame}");
                Assert.That(pixel.Green, Is.EqualTo(60).Within(35), $"Frame {frame}");
                Assert.That(pixel.Blue, Is.EqualTo(20).Within(35), $"Frame {frame}");
                Assert.That(pixel.Alpha, Is.EqualTo(255), $"Frame {frame}");
            }
        }

        [Test]
        [TestCase("BouncingBeachBallWebP.webp")]
        public void Sequence(string filename)
        {
            var imageStream = Open(filename);
            var renderer = new WebpRenderer(imageStream, new BitmapFaceFactory());

            for (int i = 0; i < renderer.FrameCount; ++i)
            {
                renderer.ProcessFrame(i);

                var imageName = Path.GetFileNameWithoutExtension(filename);
                if (!ImageMatcher.MatchImage((BitmapFace)renderer.Current, imageName, i))
                {
                    Assert.Fail($"Frame unmatch: '{filename}' frame {i}");
                }
            }

        }

        [Test]
        [TestCase("BouncingBeachBallWebP.webp")]
        public void Jump(string filename)
        {
            var imageStream = Open(filename);
            var renderer = new WebpRenderer(imageStream, new BitmapFaceFactory());

            var indics = new List<int>();

            foreach (var step in Enumerable.Range(1, renderer.FrameCount))
            {
                indics.Add(0);

                for (int start = 1; start < renderer.FrameCount; ++start)
                    for (int idx = start; idx < renderer.FrameCount; idx += step)
                        indics.Add(idx);
            }

            foreach (int i in indics)
            {
                renderer.ProcessFrame(i);

                var imageName = Path.GetFileNameWithoutExtension(filename);
                if (!ImageMatcher.MatchImage((BitmapFace)renderer.Current, imageName, i))
                {
                    Assert.Fail($"Frame unmatch: '{filename}' frame {i}");
                }
            }
        }

        public static Stream Open(string imagefilename)
        {
            var path = $"AnimatedImageTest.Inputs.{imagefilename}";

            return Assembly.GetCallingAssembly().GetManifestResourceStream(path)
                   ?? throw new ArgumentException($"image not found: '{imagefilename}'");
        }
    }
}
