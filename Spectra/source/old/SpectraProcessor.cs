using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO.Ports;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using System.Numerics;
using Accord.Math;

namespace Spectra
{
	class SpectraProcessor
	{
        private static readonly int RATE = 48000;
        private static readonly int SAMPLE_COUNT = 1 << 13; //Use 13 later
        private static readonly float FREQUENCY_RESOLUTION = (float)RATE / SAMPLE_COUNT;
        private static readonly int FRAME_RATE = 60;
        private static readonly int FRAME_TIME = 1000 / FRAME_RATE;
        private static readonly int BYTES_PER_SAMPLE = 4;
        private static readonly int BITS_PER_SAMPLE = 32;
        private static readonly int BUFFER_SIZE = 20 * RATE * BYTES_PER_SAMPLE;
        private static readonly int LED_COUNT = 60;
        private static readonly int MIN_FREQUENCY = 20;
        private static readonly int MAX_FREQUENCY = 16000;
        public static int PRE_GAIN = 1000;
        public static int POST_GAIN = 10;
        private static readonly int GAIN_CLIP = 255;
        private static readonly int COUNT_NORMALIZATION_BASE = 6;
        private static readonly double NORMALIZATION_LIMIT = 0.5;
        public static double BLEND = 0.1;

        private SerialPort Port;
        private Thread Thread;
        protected bool Running;
        private WasapiLoopbackCapture Capture;
        private OpenBufferedWaveProvider WaveProvider;
        private ISampleProvider SampleProvider;

        /*
         * Constructs a SpectraProcessor with an open SerialPort.
         * Throws an exception if a closed serial port is used.
         */
        public SpectraProcessor(SerialPort port)
        {
            Port = port ?? throw new ArgumentNullException("SerialPort 'port' cannot be null.");
            if (!Port.IsOpen) {
                throw new ArgumentException("SerialPort 'port' must be open.");
            }
        }

        /*
         * Starts the SpectraProcessor's 'Process' function on a new thread.
         */
        public void Start()
        {
            Running = true;
            Thread = new Thread(Process);
            Thread.Start();
        }

        /*
         * Instructs the SpectraProcessor's 'Process' inner loop to terminate
         * at the end of the current frame, then sleeps the 'Stop' function's
         * caller for 100 milliseconds.
         */
        public void Stop()
        {
            Running = false;
            Thread.Sleep(100);
        }

        /*
         * Records system output, processes it, and controls the serial port.
         */
        virtual protected void Process()
        {
            BeginRecording();
            
            var indices = GenerateIndexMap();
            var bucketCounts = GenerateBucketCounts(indices);
            var sampleBuffer = new float[SAMPLE_COUNT];
            var fourierBuffer = new double[SAMPLE_COUNT];
            var fourierBufferComplex = new Complex[SAMPLE_COUNT];
            var buckets = new double[LED_COUNT];
            var bucketsBackBuffer = new double[buckets.Length];
            var streamBuffer = new byte[buckets.Length + 2];

            while (Running)
            {
                Sample(sampleBuffer);
                Fourier(sampleBuffer, fourierBuffer, fourierBufferComplex);
                Bucketize(fourierBuffer, indices, buckets);
                Blend(buckets, bucketsBackBuffer);
                SoftNormalizeByCount(buckets, bucketCounts);
                ExponentialAttenuation(buckets);
                //SoftNormalizeByMaximum(buckets); //Don't do this later
                Stream(buckets, streamBuffer);
                WaitForNextFrame();
            }

            StopRecording();
        }

        /*
         * Instantiates objects for recording, then sleeps for 1 second
         * to allow the circular audio buffer to partially fill up.
         */
        protected void BeginRecording()
        {
            Capture = new WasapiLoopbackCapture();

            Capture.DataAvailable += new EventHandler<WaveInEventArgs>(OnDataAvailable);
            WaveProvider = new OpenBufferedWaveProvider(Capture.WaveFormat);
            WaveProvider.BufferLength = BUFFER_SIZE;
            WaveProvider.DiscardOnBufferOverflow = true;
            SampleProvider = WaveProvider.ToSampleProvider();

            Capture.StartRecording();

            Console.WriteLine("Successfully started recording.");

            Thread.Sleep(1000);
        }

        /*
         * Stops, discards, and nullifies recording objects.
         */
        protected void StopRecording()
        {
            Capture.StopRecording();
            Capture.Dispose();
            Capture = null;
            WaveProvider = null;
            SampleProvider = null;
            Console.WriteLine("Successfully terminated recording.");
        }

        /*
         * Maps the first (unmirrored) half of the Fourier transform's
         * output to the correct buckets logarithmically by frequency.
         */
        protected int[] GenerateIndexMap()
        {
            double logDenom = 6.7;
            double logBase = 4.0;
            int lastIndex = (int) ((MAX_FREQUENCY / 2) / FREQUENCY_RESOLUTION);
            double logMax = Math.Log(1 + lastIndex / logDenom, logBase);
            int[] indices = new int[lastIndex];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = (int)(LED_COUNT * Math.Log(1 + i / logDenom, logBase) / logMax);
            }
            return indices;
        }

        /*
         * Determines how many frequency chunks are allocated to each bucket.
         */
        protected int[] GenerateBucketCounts(int[] indices)
        {
            int[] bucketCounts = new int[LED_COUNT];
            for(int i = 0; i < indices.Length; i++)
            {
                bucketCounts[indices[i]]++;
            }
            return bucketCounts;
        }

        /*
         * Samples the last SAMPLE_COUNT samples from the circular buffer
         * without deleting what was read.
         */
        protected void Sample(float[] samples)
        {
            var recordBuffer = WaveProvider.circularBuffer;
            recordBuffer.readPosition = recordBuffer.writePosition - samples.Length * BYTES_PER_SAMPLE;
            if (recordBuffer.readPosition < 0) recordBuffer.readPosition += BUFFER_SIZE;
            SampleProvider.Read(samples, 0, samples.Length);
        }

        /*
         * Generates a frequency distribution for the given audio samples.
         * Applies PRE_GAIN to the samples before evaluating the Fourier transform.
         */
        protected void Fourier(float[] samples, double[] fourier, Complex[] fourierComplex)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                fourierComplex[i] = new Complex(samples[i] * PRE_GAIN, 0.0);
            }

            FourierTransform.FFT(fourierComplex, FourierTransform.Direction.Forward);

            for (int i = 0; i < fourierComplex.Length; i++)
            {
                fourier[i] = fourierComplex[i].Magnitude;
            }
        }

        /*
         * Clears the buckets array, then sums the fourier transform into those
         * buckets using the indices array to map frequencies to buckets.
         */
        protected void Bucketize(double[] fourier, int[] indices, double[] buckets)
        {
            int frequencyOffset = (int) (MIN_FREQUENCY / FREQUENCY_RESOLUTION);
            Array.Clear(buckets, 0, buckets.Length);

            for (int i = 0; i < indices.Length; i++)
            {
                buckets[indices[i]] += fourier[i + frequencyOffset];
            }
        }

        /*
         * Applies a horizontal blur effect to the buckets array
         * based on the BLEND factor. The blur intensity increases
         * as the index increases, up to double the BLEND factor
         * at the last index in the array. Note, the arrays passed
         * to this function must be of the same length.
         */
        protected void Blend(double[] buckets, double[] backBuffer)
        {
            Array.Copy(buckets, backBuffer, buckets.Length);

            double blend;

            for(int i = 0; i < backBuffer.Length; i++)
            {
                blend = BLEND;// + BLEND * (i / backBuffer.Length);
                if (i != 0) backBuffer[i] += buckets[i - 1] * blend;
                if (i != backBuffer.Length - 1) backBuffer[i] += buckets[i + 1] * blend;
                backBuffer[i] /= (1 + 2 * blend);
            }

            Array.Copy(backBuffer, buckets, backBuffer.Length);
        }

        /*
         * Divides each bucket by the logarithm of it's frequency chunk count.
         * Then multiplies each bucket by POST_GAIN.
         */
        protected void SoftNormalizeByCount(double[] buckets, int[] bucketCounts)
        {
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] /= Math.Log(bucketCounts[i] + COUNT_NORMALIZATION_BASE, COUNT_NORMALIZATION_BASE);
                buckets[i] *= POST_GAIN;
            }
        }

        /*
         * Divides each bucket by the logarithm of the maximum bucket
         * value across the entire array. This compresses the 
         * display to allow for better peak contrast, but does not
         * scale up values when the maximum is low.
         */
        protected void SoftNormalizeByMaximum(double[] buckets)
        {
            double denom = Math.Max(Math.Log(buckets.Max() / 51, 2), NORMALIZATION_LIMIT);
            double result;

            for(int i = 0; i < buckets.Length; i++)
            {
                result = Math.Log(5, 2) * buckets[i] / denom;
                if(result < buckets[i])
                {
                    buckets[i] = result;
                }
            }
        }

        /*
         * Applies an ABe^(x/B) - A transformation to the bucket values
         * to improve contrast between high and low values.
         */
        protected void ExponentialAttenuation(double[] buckets)
        {
            double a = 148.4;

            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] = (GAIN_CLIP + a) * Math.Exp(buckets[i] / GAIN_CLIP - 1) - a;
            }
        }

        /*
         * Writes the buckets to the SerialPort as a byte array,
         * with 1s being appended and prepended.
         */
        protected void Stream(double[] buckets, byte[] stream)
        {
            stream[0] = stream[stream.Length - 1] = 1;

            for (int i = 0; i < buckets.Length; i++)
            {
                stream[i + 1] = (byte)(Math.Min(buckets[i], GAIN_CLIP));
            }

            Port.Write(stream, 0, stream.Length);
        }

        /*
         * Sleeps the 'Process' thread for FRAME_TIME milliseconds.
         */
        protected void WaitForNextFrame()
        {
            Thread.Sleep(FRAME_TIME);
        }

        /*
         * Prints an array's elements.
         */
        protected void PrintArray(Array a)
        {
            foreach(var e in a)
            {
                Console.Write(e.ToString().Substring(0, Math.Min(e.ToString().Length, 3)) + " ");
            }
            Console.WriteLine();
        }

        /*
         * Callback that accepts new audio samples from WASAPI.
         */
        protected void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            WaveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }

	}

    class WaveformProcessor: SpectraProcessor
    {
        public WaveformProcessor(SerialPort port) : base(port) { }

        override protected void Process()
        {
            BeginRecording();

            Thread.Sleep(1000);

            var buckets = new double[60];
            var buffer = new double[60];
            var factor = 1000;
            var scale = 1000;
            var sampleBuffer = new float[factor * buckets.Length];
            var stream = new byte[buckets.Length + 2];

            while(Running)
            {
                Sample(sampleBuffer);
                for (int i = 0; i < sampleBuffer.Length; i++)
                {
                    sampleBuffer[i] *= scale;

                    sampleBuffer[i] = Math.Max(sampleBuffer[i], 0);
                    if (i % factor == 0)
                    {
                        buckets[buckets.Length - 1 - i / factor] = 0;
                    }
                    buckets[buckets.Length - 1 - i / factor] += sampleBuffer[i] / factor;
                }

                //for(int i = 0; i < buckets.Length / 2; i++)
                //{
                //    buckets[29 - i] = buffer[i];
                //    buckets[30 + i] = buffer[i];
                //}
                //PrintArray(stream);
                Stream(buckets, stream);
                WaitForNextFrame();
            }

            StopRecording();
        }
    }

    class PerlinProcessor: SpectraProcessor
    {
        public static int OCTAVES = 4;
        public static float PERSISTENCE = 0.65f;
        public static float X_SCALE = 1f;
        public static float T_SCALE = 1f;

        private float time = 0.0f;

        public PerlinProcessor(SerialPort port) : base(port) { }

        override protected void Process()
        {
            var buckets = new double[60];
            var buffer = new double[60];
            var stream = new byte[buckets.Length + 2];

            var perlin = new PerlinNoise(OCTAVES, PERSISTENCE);

            while (Running)
            {
                for (int i = 0; i < buckets.Length; i++)
                {
                    buckets[i] = perlin.Function2D(i * X_SCALE, time * T_SCALE);
                    buckets[i] += Math.Sqrt(2) / 2;
                    buckets[i] *= buckets[i] * buckets[i];
                    buckets[i] = ((buckets[i] + 1) / 2) * 255;
                }

                Stream(buckets, stream);
                WaitForNextFrame();

                time += 1 / 60f;
            }
        }
    }
}
