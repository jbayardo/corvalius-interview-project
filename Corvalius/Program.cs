using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using System.Drawing;

namespace Corvalius
{
    class SingleSobelRunner
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

        public static Bitmap Slow(Bitmap inputBitmap)
        {
            // Create output, a (W - kernelW + 1)x(H - kernelH + 1) bitmap
            Bitmap OutputBitmap = new Bitmap(inputBitmap.Width - 2, inputBitmap.Height - 2);

            for (var x = 1; x < inputBitmap.Width - 1; ++x)
            {
                for (var y = 1; y < inputBitmap.Height - 1; ++y)
                {
                    int VRed = 0, VGreen = 0, VBlue = 0;
                    int HRed = 0, HGreen = 0, HBlue = 0;

                    // Calculate the horizontal and vertical convolutions for this pixel
                    for (var dx = 0; dx < 3; ++dx)
                    {
                        for (var dy = 0; dy < 3; ++dy)
                        {
                            Color CurrentPixel = inputBitmap.GetPixel(x + dx - 1, y + dy - 1);

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

        public static void FastBoundedProcessChunk(
            byte[] inputMatrix,
            long inputWidth,
            int inputStride,
            byte[] outputMatrix,
            int outputStride,
            long lowerBound,
            long upperBound)
        {
            for (var x = 1; x < inputWidth - 1; ++x)
            {
                for (var y = 1; y < upperBound - lowerBound + 1; ++y)
                {
                    int VRed = 0, VGreen = 0, VBlue = 0;
                    int HRed = 0, HGreen = 0, HBlue = 0;

                    // Calculate the horizontal and vertical convolutions for this pixel
                    for (var dx = 0; dx < 3; ++dx)
                    {
                        for (var dy = 0; dy < 3; ++dy)
                        {
                            long InputBaseIndex = (y + dy - 1) * inputStride + 3 * (x + dx - 1) + (lowerBound - 1) * inputStride;
                            byte Blue = inputMatrix[InputBaseIndex];
                            byte Green = inputMatrix[InputBaseIndex + 1];
                            byte Red = inputMatrix[InputBaseIndex + 2];

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
                    long OutputBaseIndex = outputStride * (y - 1) + 3 * (x - 1) + outputStride * (lowerBound - 1);

                    // Blue channel
                    outputMatrix[OutputBaseIndex] = Convert.ToByte(BlueGradient);
                    // Green channel
                    outputMatrix[OutputBaseIndex + 1] = Convert.ToByte(GreenGradient);
                    // Red channel
                    outputMatrix[OutputBaseIndex + 2] = Convert.ToByte(RedGradient);
                }
            }
        }

        public static Bitmap Fast(Bitmap inputBitmap)
        {
            // Create output, a (W - kernelW + 1)x(H - kernelH + 1) bitmap
            Bitmap OutputBitmap = new Bitmap(inputBitmap.Width - 2, inputBitmap.Height - 2);

            Rectangle InputRectangle = new Rectangle(0, 0, inputBitmap.Width, inputBitmap.Height);

            System.Drawing.Imaging.BitmapData InputData = inputBitmap.LockBits(
                InputRectangle,
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            // Load the input data
            int InputSize = InputData.Stride * InputData.Height;
            byte[] InputMatrix = new byte[InputSize];
            Marshal.Copy(InputData.Scan0, InputMatrix, 0, InputSize);
            // Turn the input memory back into managed
            inputBitmap.UnlockBits(InputData);

            // Create our actual output matrix

            // We are using a 3-byte per pixel representation for the output bitmap. This
            // assumption is necessary to calculate this value here and avoid locking
            // bits for writing here, and being able to do so at the end.
            int OutputStride = OutputBitmap.Width * 3;
            // Align stride to four byte boundary.
            // See https://msdn.microsoft.com/en-us/library/system.drawing.imaging.bitmapdata.stride(v=vs.110).aspx
            OutputStride += OutputStride % 4;

            int OutputSize = OutputBitmap.Height * OutputStride;
            byte[] OutputMatrix = new byte[OutputSize];

            FastBoundedProcessChunk(
                InputMatrix,
                InputData.Width,
                InputData.Stride,
                OutputMatrix,
                OutputStride,
                1,
                inputBitmap.Height - 1);

            // Copy our memory into managed memory
            Rectangle OutputRectangle = new Rectangle(
                0,
                0,
                OutputBitmap.Width,
                OutputBitmap.Height);

            System.Drawing.Imaging.BitmapData OutputData = OutputBitmap.LockBits(
                OutputRectangle,
                System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            // Copy our matrix
            Marshal.Copy(OutputMatrix, 0, OutputData.Scan0, OutputSize);

            // Turn the output memory back into managed
            OutputBitmap.UnlockBits(OutputData);

            return OutputBitmap;
        }
    }

    class MultiSobelRunner
    {
        class MultiSobelProcessor
        {
            public byte[] InputMatrix { set; get; }
            public long InputWidth { set; get; }
            public int InputStride { set; get; }
            public byte[] OutputMatrix { set; get; }
            public int OutputStride { set; get; }
            public long LowerBound { set; get; }
            public long UpperBound { set; get; }
            private ManualResetEvent _doneEvent;

            public MultiSobelProcessor(ManualResetEvent doneEvent)
            {
                _doneEvent = doneEvent;
            }

            public void Run(Object threadContext)
            {
                int ThreadId = (int)threadContext;

                //Console.WriteLine("Thread {0}, Lower: {1}", ThreadId, LowerBound);
                //Console.WriteLine("Thread {0}, Upper: {1}", ThreadId, UpperBound);
                //Console.ReadKey();

                SingleSobelRunner.FastBoundedProcessChunk(
                    InputMatrix,
                    InputWidth,
                    InputStride,
                    OutputMatrix,
                    OutputStride,
                    LowerBound,
                    UpperBound);

                _doneEvent.Set();
            }
        }

        public static Bitmap Process(Bitmap inputBitmap, uint threadNum = 5)
        {
            // Set up thread pool
            long Workload = (inputBitmap.Height - 2) / threadNum;
            ManualResetEvent[] DoneEvents = new ManualResetEvent[threadNum];

            // Load up input bitmap data
            Rectangle InputRectangle = new Rectangle(0, 0, inputBitmap.Width, inputBitmap.Height);

            System.Drawing.Imaging.BitmapData InputData = inputBitmap.LockBits(
                InputRectangle,
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            
            int InputSize = InputData.Stride * InputData.Height;
            byte[] InputMatrix = new byte[InputSize];
            Marshal.Copy(InputData.Scan0, InputMatrix, 0, InputSize);

            // Turn the input memory back into managed
            inputBitmap.UnlockBits(InputData);

            // Create the output bitmap
            Bitmap OutputBitmap = new Bitmap(inputBitmap.Width - 2, inputBitmap.Height - 2);

            // We are using a 3-byte per pixel representation for the output bitmap. This
            // assumption is necessary to calculate this value here and avoid locking
            // bits for writing here, and being able to do so at the end.
            int OutputStride = OutputBitmap.Width * 3;
            // Align stride to four byte boundary.
            // See https://msdn.microsoft.com/en-us/library/system.drawing.imaging.bitmapdata.stride(v=vs.110).aspx
            OutputStride += OutputStride % 4;

            int OutputSize = OutputBitmap.Height * OutputStride;
            byte[] OutputMatrix = new byte[OutputSize];

            // Launch threads
            for (var ThreadId = 0; ThreadId < threadNum; ++ThreadId)
            {
                DoneEvents[ThreadId] = new ManualResetEvent(false);

                MultiSobelProcessor Processor = new MultiSobelProcessor(DoneEvents[ThreadId]);
                Processor.InputMatrix = InputMatrix;
                Processor.InputWidth = InputData.Width;
                Processor.InputStride = InputData.Stride;
                Processor.OutputMatrix = OutputMatrix;
                Processor.OutputStride = OutputStride;
                Processor.LowerBound = Math.Max(1, ThreadId * Workload);
                Processor.UpperBound = Math.Min(InputData.Height - 1, (ThreadId + 1) * Workload + 1);

                ThreadPool.QueueUserWorkItem(Processor.Run, ThreadId);
            }

            WaitHandle.WaitAll(DoneEvents);

            // Copy our memory into managed memory
            Rectangle OutputRectangle = new Rectangle(0, 0, OutputBitmap.Width, OutputBitmap.Height);

            System.Drawing.Imaging.BitmapData OutputData = OutputBitmap.LockBits(
                OutputRectangle,
                System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            // Copy our matrix
            Marshal.Copy(OutputMatrix, 0, OutputData.Scan0, OutputSize);

            // Turn the output memory back into managed
            OutputBitmap.UnlockBits(OutputData);

            return OutputBitmap;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Image original = Image.FromFile(args[0]);
            Bitmap bitmap = new Bitmap(original);

            Image outputSlow = SingleSobelRunner.Slow(bitmap);
            outputSlow.Save(args[1]);

            Image outputFast = SingleSobelRunner.Fast(bitmap);
            outputFast.Save(args[2]);

            Image outputMulti = MultiSobelRunner.Process(bitmap, 2);
            outputMulti.Save(args[3]);
        }
    }
}
