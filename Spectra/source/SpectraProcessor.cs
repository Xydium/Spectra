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
        private static readonly int SAMPLE_COUNT = 1 << 14;
        private static readonly float FREQUENCY_RESOLUTION = (float)RATE / SAMPLE_COUNT;
        private static readonly int FRAME_RATE = 60;
        private static readonly int FRAME_TIME = 1000 / FRAME_RATE;
        private static readonly int BYTES_PER_SAMPLE = 4;
        private static readonly int BITS_PER_SAMPLE = 32;
        private static readonly int BUFFER_SIZE = 4 * RATE * BYTES_PER_SAMPLE;
        private static readonly int LED_COUNT = 60;
        private static readonly int MIN_FREQUENCY = 20;
        private static readonly int MAX_FREQUENCY = 16000;
        private static readonly int PRE_GAIN = 1000;
        private static readonly int POST_GAIN = 10;
        private static readonly int GAIN_CLIP = 255;
        private static readonly int COUNT_NORMALIZATION_BASE = 6;
        private static readonly double NORMALIZATION_LIMIT = 0.5;
        private static readonly double BLEND = 0.1;

        private SerialPort Port;
        private Thread Thread;
        private bool Running;
        private WasapiLoopbackCapture Capture;
        private OpenBufferedWaveProvider WaveProvider;
        private ISampleProvider SampleProvider;

        public SpectraProcessor(SerialPort port)
        {
            Port = port;
        }

        public void Start()
        {
            Running = true;
            Thread = new Thread(Process);
            Thread.Start();
        }

        public void Stop()
        {
            Running = false;
            Thread.Sleep(100);
        }

        private void Process()
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
                SoftNormalizeByMaximum(buckets);
                Stream(buckets, streamBuffer);
                WaitForNextFrame();
            }

            StopRecording();
        }

        private void BeginRecording()
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

        private void StopRecording()
        {
            Capture.StopRecording();
            Capture.Dispose();
            Capture = null;
            WaveProvider = null;
            SampleProvider = null;
            Console.WriteLine("Successfully terminated recording.");
        }

        private int[] GenerateIndexMap()
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

        private int[] GenerateBucketCounts(int[] indices)
        {
            int[] bucketCounts = new int[LED_COUNT];
            for(int i = 0; i < indices.Length; i++)
            {
                bucketCounts[indices[i]]++;
            }
            return bucketCounts;
        }

        private void Sample(float[] samples)
        {
            var recordBuffer = WaveProvider.circularBuffer;
            recordBuffer.readPosition = recordBuffer.writePosition - SAMPLE_COUNT * BYTES_PER_SAMPLE;
            if (recordBuffer.readPosition < 0) recordBuffer.readPosition += BUFFER_SIZE;
            SampleProvider.Read(samples, 0, SAMPLE_COUNT);
        }

        private void Fourier(float[] samples, double[] fourier, Complex[] fourierComplex)
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

        private void Bucketize(double[] fourier, int[] indices, double[] buckets)
        {
            int frequencyOffset = (int) (MIN_FREQUENCY / FREQUENCY_RESOLUTION);
            Array.Clear(buckets, 0, buckets.Length);

            for (int i = 0; i < indices.Length; i++)
            {
                buckets[indices[i]] += fourier[i + frequencyOffset];
            }
        }

        private void Blend(double[] buckets, double[] backBuffer)
        {
            Array.Copy(buckets, backBuffer, buckets.Length);

            double blend;

            for(int i = 0; i < backBuffer.Length; i++)
            {
                blend = BLEND + BLEND * (i / backBuffer.Length);
                if (i != 0) backBuffer[i] += buckets[i - 1] * blend;
                if (i != backBuffer.Length - 1) backBuffer[i] += buckets[i + 1] * blend;
                backBuffer[i] /= (1 + 2 * blend);
            }

            Array.Copy(backBuffer, buckets, backBuffer.Length);
        }

        private void SoftNormalizeByCount(double[] buckets, int[] bucketCounts)
        {
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] /= Math.Log(bucketCounts[i] + COUNT_NORMALIZATION_BASE, COUNT_NORMALIZATION_BASE);
                buckets[i] *= POST_GAIN;
            }
        }

        private void SoftNormalizeByMaximum(double[] buckets)
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

        private void ExponentialAttenuation(double[] buckets)
        {
            double a = 148.4;

            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] = (GAIN_CLIP + a) * Math.Exp(buckets[i] / GAIN_CLIP - 1) - a;
            }
        }

        private void Stream(double[] buckets, byte[] stream)
        {
            stream[0] = stream[stream.Length - 1] = 1;

            for (int i = 0; i < buckets.Length; i++)
            {
                stream[i + 1] = (byte)(Math.Min(buckets[i], GAIN_CLIP));
            }

            Port.Write(stream, 0, stream.Length);
        }

        private void WaitForNextFrame()
        {
            Thread.Sleep(FRAME_TIME);
        }

        private void PrintArray(Array a)
        {
            foreach(var e in a)
            {
                Console.Write(e.ToString().Substring(0, Math.Min(e.ToString().Length, 3)) + " ");
            }
            Console.WriteLine();
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            WaveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }

	}
}
