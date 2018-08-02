using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Accord;
using Accord.Math;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using System.Numerics;

namespace Spectra
{

	class Spectra
	{
		static Dictionary<String, Func<string[], bool>> Commands = new Dictionary<String, Func<String[], bool>>();
		static bool ShouldQuit = false;

		static WasapiLoopbackCapture wi;
		static WaveFileWriter wfw;
		static OpenBufferedWaveProvider bwp;
		static ISampleProvider sp;
		static Int32 envelopeMax;

		static readonly int RATE = 48000;
		static readonly int SAMPLE_COUNT = (int)Math.Pow(2, 14); //Change back to 14 later
		static readonly float FREQ_RES = (float)RATE / SAMPLE_COUNT;
		static readonly int SAMPLINGS_PER_SEC = 60;
		static readonly int BYTES_PER_SAMPLE = 4;
		static readonly int BUFFER_SIZE = 2 * RATE * BYTES_PER_SAMPLE;
		static readonly int SAMPLE_RES = 32;

		static SerialPort port;

		static bool ConnectSerial(string[] args)
		{
			if (args == null || args.Length == 0)
			{
				args = new string[] { "COM3", "230400" };
			}

			if (port != null)
			{
				Console.WriteLine("Connection already open on Port {0}", port.PortName);
				return true;
			}

			string portID = args[0];
			int baudRate = Int32.Parse(args[1]);
			port = new SerialPort(portID, baudRate, Parity.None, 8, StopBits.One);
			port.Open();

			Thread.Sleep(100);

			Console.WriteLine("Successfully opened Port {0} at {1} bps.", portID, baudRate);

			return true;
		}

		static bool WriteSerial(string[] args)
		{
			foreach (string data in args)
			{
				port.Write(data);
				Console.WriteLine("Wrote '{0}' to Serial Port.", data);
			}

			return true;
		}

		static byte[] processed = new byte[SAMPLE_COUNT / 2];
		static float freq = 0.0f;
		static int freqCap = (int)(FREQ_RES * SAMPLE_COUNT / 2);
		static float logCoef = 6.7f;
		static float logBase = 4.0f;
		static int[] indexMappings = new int[(int)(8000.0f / ((float)RATE / SAMPLE_COUNT))];
		static int lastIndex = indexMappings.Length;
		static int freqOffset = 6; //This will ensure that anything below 20hz is dropped
		static float maxFreqLog = (float)Math.Log(1 + lastIndex / logCoef, logBase);
		static bool last_send = true;
		static int LED_COUNT = 60;
		static byte[] sending = new byte[LED_COUNT + 2];
		static float[] buckets = new float[LED_COUNT];
		static float[] bucketMaxes = new float[LED_COUNT];
		static float[] bucketsBuffer = new float[buckets.Length];
		static int[] bucketCounts = new int[buckets.Length];
		static bool AudioSerial(string[] args)
		{
			if (wi == null) InitRecord(null);
			if (port == null) ConnectSerial(null);
			if (args.Length > 0)
			{
				CommandSerial(args);
			}

			if (bwp.circularBuffer == null)
			{
				Thread.Sleep(1000);
			}

			for (int i = 0; i < indexMappings.Length; i++)
			{
				indexMappings[i] = (int)(buckets.Length * Math.Log(1 + i / logCoef, logBase) / maxFreqLog);
				bucketCounts[indexMappings[i]]++;
			}

			while (true)
			{
				//long start = Environment.TickCount;
				//Console.WriteLine(bwp.BufferedBytes);
				bwp.circularBuffer.readPosition = (bwp.circularBuffer.writePosition - (SAMPLE_COUNT * BYTES_PER_SAMPLE));
				if (bwp.circularBuffer.readPosition < 0) bwp.circularBuffer.readPosition += BUFFER_SIZE;
				sp.Read(frames, 0, SAMPLE_COUNT);

				for (int i = 0; i < SAMPLE_COUNT; i++)
				{
					vals[i] = (double)frames[i] * 1000; //10^5
				}

				fy = FFT(vals);

				//512 values to 6 buckets
				//freq resolution is ~90 hz

				int mult = 10;
				bool send = false;

				//float fsum = 0;
				//foreach (float f in fy)
				//{
				//	fsum += f;
				//}
				//float mean = fsum / fy.Length;

				//This configuration works well for the six bucket design
				//Will likely need to retune values for 60 buckets

				buckets = new float[LED_COUNT];

				for (int i = 0; i < lastIndex; i++)
				{
					//int idx = (int)(buckets.Length * Math.Log(1 + i / logCoef, logBase) / maxFreqLog);
					if(fy[i + freqOffset] > bucketMaxes[indexMappings[i]])
						bucketMaxes[indexMappings[i]] = (float)fy[i + freqOffset];
					buckets[indexMappings[i]] += (float)fy[i + freqOffset];
				}

				float blend = 0f;
				for (int bk = 0; bk < buckets.Length; bk++)
				{
					bucketMaxes[bk] = 0;
					//if(bucketMaxes[bk] > buckets[bk] / bucketCounts[bk])
					//{
						//buckets[bk] += bucketMaxes[bk] * bucketCounts[bk];
					//}

					float sum = 0.0f;
					blend = 0.1f + 0.1f * (bk / buckets.Length);
					if (bk != 0) sum += blend * buckets[bk - 1];
					sum += buckets[bk];
					if (bk != buckets.Length - 1) sum += blend * buckets[bk + 1];
					bucketsBuffer[bk] = sum / (1 + 2 * blend);
				}
				float[] temp = buckets;
				buckets = bucketsBuffer;
				bucketsBuffer = temp;

				float a = 148.4f; //148.4 125.7 116.8 121.1
				float max = 25.0f;

				for (int bk = 0; bk < buckets.Length; bk++)
				{
					//Console.WriteLine(bucketCounts[bk]);
					//sending[bk + 1] = (byte)((255.0 + 148.4) * Math.Exp((buckets[bk] / Math.Log(bucketCounts[bk] + 5, 2) * mult) / 255 - 1) - 148.4);
					//if(buckets[bk] >= 0.001 && buckets[bk] <= 0.05)
					//{
					//	buckets[bk] *= (0.05f - buckets[bk]) / buckets[bk];
					//}
					double x = (buckets[bk] / Math.Log(bucketCounts[bk] + 5, 6) * mult);
					//double x = (buckets[bk] / bucketCounts[bk] * mult);
					//double x = buckets[bk] * mult;
					//buckets[bk] = (float)((255.0 + 148.4) * Math.Exp((buckets[bk] / Math.Log(bucketCounts[bk] + 5, 6) * mult) / 255 - 1) - 148.4);
					buckets[bk] = (float)((255.0 + a) * Math.Exp(x / 255 - 1) - a);
					//buckets[bk] = (float)(127.0 * (Math.Pow(Math.Tan((buckets[bk] / Math.Log(bucketCounts[bk] + 5, 6) * mult) / 160.0 - 0.79), 1.8) + 1.0));
					//buckets[bk] = (float)((255.0 / (4 - x / 85.0)) * Math.Log10((0.9 * x + 25.5) / 25.5));
					//if (nonzero && buckets[bk] < 20) buckets[bk] = 20.0f;
					if (buckets[bk] > 10) send = true;
					//buckets[bk] = (byte) Math.Min(buckets[bk], 255);
					if (buckets[bk] > max) max = buckets[bk];
				}

				for(int bk = 0; bk < buckets.Length; bk++)
				{
					float d = (float) Math.Log(max / 51, 2.0f);
					if (d < 0.5f) d = 0.5f;
					float x = (float) (Math.Log(5f, 2.0f) * buckets[bk] / d);
					if (x <= buckets[bk]) buckets[bk] = x;
				}

				//Get rid of this? I'm averaging before and after the exp function
				for (int bk = 1; bk < buckets.Length - 1; bk++)
				{
					float sum = 0.5f * buckets[bk-1] + buckets[bk] + 0.5f * buckets[bk + 1];
					//sending[bk + 1] = (byte) Math.Min(sum / 2.0f, 255);
					sending[bk + 1] = (byte) Math.Min(buckets[bk], 255);
				}
				sending[1] = (byte) ((buckets[0] + buckets[1] * 0.5) / 1.5);
				sending[60] = (byte) ((buckets[59] + buckets[58] * 0.5) / 1.5);

				/*
				for(int bucket = 0; bucket < 6; bucket++)
				{
					float sum = 0.0f;
					int start = indices[bucket];
					int end = indices[bucket + 1];
					for (int i = start; i < end; i++)
					{
						if (fy[i] > mean)
						{
							sum += (float)fy[i];
						}
					}
					if (sum < 0.0) sum = 0.0f;
					if (sum * mult > 255) Console.WriteLine("Overflow! {0}", sum * mult);
					if (sum * mult > 1) send = true;
					//sending[bucket + 1] = (byte) (Math.Pow(sum * mult, 2) / 255.0); /// (end - start)
					sending[bucket + 1] = (byte)((255.0 + 148.4) * Math.Exp((sum * mult) / 255 - 1) - 148.4);
				}*/

				//foreach (byte b in sending)
				//{
				//	Console.Write(b + " ");
				//}
				//Console.WriteLine();

				if (send)
				{
					//foreach(byte s in sending)
					//{
					//	Console.Write(s + " ");
					//}
					//Console.WriteLine();
					//Console.WriteLine();
					sending[0] = sending[61] = 1;
					port.Write(sending, 0, sending.Length);
				}
				else if (last_send)
				{
					sending = new byte[62];
					sending[0] = 2; sending[61] = 1;
					port.Write(sending, 0, sending.Length);
				}
				last_send = send;

				Thread.Sleep(1 + 1 * (int)((1000.0 / SAMPLINGS_PER_SEC)));
				//long delta = Environment.TickCount - start;
				//Console.WriteLine(delta);

				if (Console.KeyAvailable)
				{
					if (Console.ReadKey().Key == ConsoleKey.Escape)
					{
						//sending[0] = 0b11000000;
						//port.Write(sending, 0, 1);
						//Thread.Sleep(100); 
						Console.WriteLine();
						CommandSerial(new String[] { "0" });
						StopRecord(null);
						break;
					}
				}
			}

			return true;
		}

		static bool CommandSerial(string[] args)
		{
			if (port == null)
			{
				ConnectSerial(null);
			}

			byte[] data = new byte[args.Length];

			for(int i = 0; i < args.Length; i++)
			{
				data[i] = Convert.ToByte(args[i]);
			}
			data[0] += 192;

			Console.WriteLine("Sending Command: {0} ({1}) Metadata Length: {2}", data[0] & 63, data[0], data.Length - 1);
			port.Write(data, 0, data.Length);
			Thread.Sleep(100);
			return true;
		}

		static bool CloseSerial(string[] args)
		{
			if(port == null)
			{
				Console.WriteLine("No serial connection is open.");
				return true;
			}
			Console.WriteLine("Closing serial connection on Port {0}.", port.PortName);

			port.Close();
			port = null;

			return true;
		}

		static bool InitRecord(string[] args)
		{
			wi = new WasapiLoopbackCapture();

			wi.DataAvailable += new EventHandler<WaveInEventArgs>(OnDataAvailable);
			Console.WriteLine("Bits per sample: {0}", wi.WaveFormat.BitsPerSample);
			Console.WriteLine("Bits per second: {0}", wi.WaveFormat.AverageBytesPerSecond * 8);
			Console.WriteLine("Sample Rate: {0}", wi.WaveFormat.SampleRate);
			bwp = new OpenBufferedWaveProvider(wi.WaveFormat);
			bwp.BufferLength = BUFFER_SIZE;
			bwp.DiscardOnBufferOverflow = true;
			sp = bwp.ToSampleProvider();

			wi.StartRecording();

			Console.WriteLine("Now Recording on WASAPI Loopback Capture.");

			return true;
		}

		static bool StopRecord(string[] args)
		{
			wi.StopRecording();
			wi.Dispose();
			wi = null;

			Console.WriteLine("Stopped Recording on WASAPI Loopback Capture.");

			return true;
		}

		static float[] frames = new float[SAMPLE_COUNT];
		static double[] vals = new double[SAMPLE_COUNT];
		static double[] fy = new double[SAMPLE_COUNT];
		static bool PrintRecord(string[] args)
		{
			sp.Read(frames, 0, SAMPLE_COUNT);

			for (int i = 0; i < SAMPLE_COUNT; i++)
			{
				vals[i] = (double)frames[i];
			}

			fy = FFT(vals);

			return true;
		}

		static void OnDataAvailable(object sender, WaveInEventArgs e)
		{
			bwp.AddSamples(e.Buffer, 0, e.BytesRecorded);
		}

		static bool Quit(string[] args)
		{
			if (wi != null && wi.CaptureState != CaptureState.Stopped)
			{
				Console.WriteLine("Auto-Stopping WASAPI Loopback Capture.");
				StopRecord(null);
			}
			if (port != null && port.IsOpen)
			{
				Console.WriteLine("Auto-Closing Serial Port.");
				CloseSerial(null);
			}
			ShouldQuit = true;
			return true;
		}

		static bool Help(string[] args)
		{
			Console.WriteLine("--  Spectra Commands  --");
			foreach (string key in Commands.Keys)
			{
				Console.WriteLine(key);
			}

			Console.WriteLine("-- Spectra Operations --");
			Console.WriteLine(
@"CLEAR                           0
DRAW_STATIC_HUE_AUDIO           1 staticHue:(0-255)
DRAW_RAINBOW_HUE_AUDIO          2 
DRAW_SCROLLING_HUE_AUDIO        3 scrollSlow:(0-255)
DRAW_STATIC_HUE_WAVEFORM        4 staticHue:(0-255) mix:(0-100) minimum:(0-255)
DRAW_ROTATING_HUE_WAVEFORM      5 rotatingHue:(0-255) mix:(0-100) minimum:(0-255)

DRAW_STATIC_HUE_BREATHING      10 staticHue:(0-255) frequency:(0-255) brightness:(0-255)
DRAW_ROTATING_HUE_BREATHING    11 rotatingHue:(0-255) frequency:(0-255) brightness:(0-255)"
			);

			return true;
		}

		static bool Echo(string[] args)
		{
			foreach (string a in args)
			{
				Console.Write(a + ' ');
			}
			Console.WriteLine();
			return true;
		}

		static double[] fft = new double[SAMPLE_COUNT];
		static Complex[] fftComplex = new Complex[SAMPLE_COUNT];
		static double[] FFT(double[] data)
		{
			for (int i = 0; i < SAMPLE_COUNT; i++)
			{
				fftComplex[i] = new Complex(data[i], 0.0);
			}

			FourierTransform.FFT(fftComplex, FourierTransform.Direction.Forward);

			for (int i = 0; i < SAMPLE_COUNT; i++)
			{
				fft[i] = fftComplex[i].Magnitude;
			}

			return fft;
		}

		static void InitializeCommands()
		{
			Commands.Add("quit", Quit);
			Commands.Add("help", Help);
			Commands.Add("echo", Echo);
			//Commands.Add("initRecord", InitRecord);
			//Commands.Add("stopRecord", StopRecord);
			//Commands.Add("printRecord", PrintRecord);
			Commands.Add("cnc", ConnectSerial);
			//Commands.Add("writeSerial", WriteSerial);
			Commands.Add("cse", CloseSerial);
			Commands.Add("ads", AudioSerial);
			Commands.Add("cmd", CommandSerial);
			//Commands.Add("r", Run);
		}

		static void Main(string[] args)
		{
            var c = new SpectraController();
            c.RunSpectraCommandLine();          
			Console.WriteLine("------ Spectra Controller ------");
			InitializeCommands();
			while (!ShouldQuit)
			{
				Console.Write(">> ");
				var line = Console.ReadLine();
				var parts = line.Split(' ');
				var command = parts[0];
				if (!Commands.ContainsKey(command))
				{
					Console.WriteLine("Invalid Command Name.");
					continue;
				}
				var comargs = new string[parts.Length - 1];
				for (int i = 1; i < parts.Length; i++)
				{
					comargs[i - 1] = parts[i];
				}
				if (!Commands[command](comargs))
				{
					Console.WriteLine("Command Execution Terminated With an Error.");
				}
			}
		}
	}

}
