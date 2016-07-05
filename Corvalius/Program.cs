using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Drawing;

namespace Corvalius
{
    class Program
    {

        // These are our convolutions
        static sbyte[,] VerticalKernel =
        {
                { -1, 0, 1 },
                { -2, 0, 2 },
                { -1, 0, 1 }
            };

        static sbyte[,] HorizontalKernel =
        {
                { -1, -2, -1 },
                { 0, 0, 0 },
                { 1, 2, 1 }
            };

        private static Bitmap SlowSobel(Bitmap InputBitmap)
        {
            // Create output, a (W - kernelW + 1)x(H - kernelH + 1) bitmap
            Bitmap OutputBitmap = new Bitmap(InputBitmap.Width - 2, InputBitmap.Height - 2);

            for (var x = 1; x < InputBitmap.Width - 1; ++x)
            {
                for (var y = 1; y < InputBitmap.Height - 1; ++y)
                {
                    int VRed = 0, VGreen = 0, VBlue = 0;
                    int HRed = 0, HGreen = 0, HBlue = 0;
                    
                    // Calculate the horizontal and vertical convolutions for this pixel
                    for (var dx = 0; dx < 3; ++dx)
                    {
                        for (var dy = 0; dy < 3; ++dy)
                        {
                            Color CurrentPixel = InputBitmap.GetPixel(x + dx - 1, y + dy - 1);

                            // Calculate vertical convolution
                            sbyte VerticalFactor = VerticalKernel[dx, dy];
                            VRed += CurrentPixel.R * VerticalFactor;
                            VGreen += CurrentPixel.G * VerticalFactor;
                            VBlue += CurrentPixel.B * VerticalFactor;

                            // Calculate horizontal convolution
                            sbyte HorizontalFactor = HorizontalKernel[dx, dy];
                            HRed += CurrentPixel.R * HorizontalFactor;
                            HGreen += CurrentPixel.G * HorizontalFactor;
                            HBlue += CurrentPixel.B * HorizontalFactor;
                        }
                    }

                    // Compute the final values for the gradient
                    // We saturate pixels in case they go over the representable values
                    // We have also used a common optimization consisting of using Abs rather than Norm.
                    int RedGradient = Math.Min(255, Math.Abs(VRed) + Math.Abs(HRed));
                    int GreenGradient = Math.Min(255, Math.Abs(VGreen) + Math.Abs(HGreen));
                    int BlueGradient = Math.Min(255, Math.Abs(VBlue) + Math.Abs(HBlue));

                    // Set the corresponding pixel in the output file
                    OutputBitmap.SetPixel(x - 1, y - 1, Color.FromArgb(RedGradient, GreenGradient, BlueGradient));
                }
            }

            return OutputBitmap;
        }

        private static Bitmap FastSobel(Bitmap InputBitmap)
        {
            Rectangle InputRectangle = new Rectangle(0, 0, InputBitmap.Width, InputBitmap.Height);

            System.Drawing.Imaging.BitmapData InputData = InputBitmap.LockBits(
                InputRectangle,
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            // Load the input data
            int InputSize = InputData.Stride * InputData.Height;
            byte[] InputMatrix = new byte[InputSize];
            Marshal.Copy(InputData.Scan0, InputMatrix, 0, InputSize);

            // Create output, a (W - kernelW + 1)x(H - kernelH + 1) bitmap
            Bitmap OutputBitmap = new Bitmap(InputBitmap.Width - 2, InputBitmap.Height - 2);

            // This is C# slang for "Allow me to pump data into it"
            Rectangle OutputRectangle = new Rectangle(0, 0, OutputBitmap.Width, OutputBitmap.Height);

            System.Drawing.Imaging.BitmapData OutputData = OutputBitmap.LockBits(
                OutputRectangle,
                System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            // Create our actual output matrix
            int OutputSize = OutputData.Stride * OutputData.Height;
            byte[] OutputMatrix = new byte[OutputSize];

            for (var x = 1; x < InputBitmap.Width - 1; ++x)
            {
                for (var y = 1; y < InputBitmap.Height - 1; ++y)
                {
                    int VRed = 0, VGreen = 0, VBlue = 0;
                    int HRed = 0, HGreen = 0, HBlue = 0;

                    // Calculate the horizontal and vertical convolutions for this pixel
                    for (var dx = 0; dx < 3; ++dx)
                    {
                        for (var dy = 0; dy < 3; ++dy)
                        {
                            int InputBaseIndex = (y + dy - 1) * InputData.Stride + 3 * (x + dx - 1);
                            byte Blue = InputMatrix[InputBaseIndex];
                            byte Green = InputMatrix[InputBaseIndex + 1];
                            byte Red = InputMatrix[InputBaseIndex + 2];

                            // Calculate vertical convolution
                            sbyte VerticalFactor = VerticalKernel[dx, dy];
                            VRed += Red * VerticalFactor;
                            VGreen += Green * VerticalFactor;
                            VBlue += Blue * VerticalFactor;

                            // Calculate horizontal convolution
                            sbyte HorizontalFactor = HorizontalKernel[dx, dy];
                            HRed += Red * HorizontalFactor;
                            HGreen += Green * HorizontalFactor;
                            HBlue += Blue * HorizontalFactor;
                        }
                    }

                    // Compute the final values for the gradient
                    // We saturate pixels in case they go over the representable values
                    // We have also used a common optimization consisting of using Abs rather than Norm.
                    int RedGradient = Math.Min(255, Math.Abs(VRed) + Math.Abs(HRed));
                    int GreenGradient = Math.Min(255, Math.Abs(VGreen) + Math.Abs(HGreen));
                    int BlueGradient = Math.Min(255, Math.Abs(VBlue) + Math.Abs(HBlue));

                    // Set the corresponding pixel in the output file
                    int OutputBaseIndex = OutputData.Stride * (y - 1) + 3 * (x - 1);
                    
                    // Blue channel
                    OutputMatrix[OutputBaseIndex] = Convert.ToByte(BlueGradient);
                    // Green channel
                    OutputMatrix[OutputBaseIndex + 1] = Convert.ToByte(GreenGradient);
                    // Red channel
                    OutputMatrix[OutputBaseIndex + 2] = Convert.ToByte(RedGradient);
                }
            }

            // Copy our memory into managed memory
            Marshal.Copy(OutputMatrix, 0, OutputData.Scan0, OutputSize);

            // Turn the memory back into managed
            InputBitmap.UnlockBits(InputData);
            OutputBitmap.UnlockBits(OutputData);

            return OutputBitmap;
        }

        static void Main(string[] args)
        {
            Image original = Image.FromFile(args[0]);
            Bitmap bitmap = new Bitmap(original);

            Image outputSlow = SlowSobel(bitmap);
            outputSlow.Save(args[1]);

            Image outputFast = FastSobel(bitmap);
            outputFast.Save(args[2]);
        }
    }
}
