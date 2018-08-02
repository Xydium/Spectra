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
		static readonly int SAMPLE_COUNT = (int) Math.Pow(2, 14); //Change back to 14 later
		static readonly float FREQ_RES = (float)RATE / SAMPLE_COUNT;
		static readonly int SAMPLINGS_PER_SEC = 60;
		static readonly int BYTES_PER_SAMPLE = 4;
		static readonly int BUFFER_SIZE = 2 * RATE * BYTES_PER_SAMPLE;
		static readonly int SAMPLE_RES = 32;

		static SerialPort port;

		static bool ConnectSerial(string[] args)
		{
			if(args == null || args.Length == 0)
			{
				args = new string[]{ "COM3", "9600" };
			}

			string portID = args[0];
			int baudRate = Int32.Parse(args[1]);
			port = new SerialPort(portID, baudRate, Parity.None, 8, StopBits.One);
			port.Open();

			Console.WriteLine("Successfully opened {0} at {1} bps.", portID, baudRate);

			return true;
		}

		static bool WriteSerial(string[] args)
		{
			foreach(string data in args)
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
		static int lastIndex = indexMappings.Length;
		static int freqOffset = 6; //This will ensure that anything below 20hz is dropped
		static float maxFreqLog = (float)Math.Log(1 + lastIndex / logCoef, logBase);
		static bool last_send = true;
		static int LED_COUNT = 60;
		static byte[] sending = new byte[LED_COUNT + 2];
		static float[] buckets = new float[LED_COUNT];
		static int[] bucketCounts = new int[buckets.Length];
		static int[] indexMappings = new int[(int)(20000.0f / ((float)RATE / SAMPLE_COUNT))];
		static bool AudioSerial(string[] args)
		{
			if (bwp.circularBuffer == null)
			{
				Thread.Sleep(1000);
			}

			sending[0] = sending[61] = 1;

			for(int i = 0; i < indexMappings.Length; i++)
			{
				indexMappings[i] = (int)(buckets.Length * Math.Log(1 + i / logCoef, logBase) / maxFreqLog);
				bucketCounts[indexMappings[i]]++;
			}

			while (true)
			{
				long start = Environment.TickCount;
				//Console.WriteLine(bwp.BufferedBytes);
				bwp.circularBuffer.readPosition = (bwp.circularBuffer.writePosition - (SAMPLE_COUNT * BYTES_PER_SAMPLE));
				if (bwp.circularBuffer.readPosition < 0) bwp.circularBuffer.readPosition += BUFFER_SIZE;
				sp.Read(frames, 0, SAMPLE_COUNT);

				for (int i = 0; i < SAMPLE_COUNT; i++)
				{
					vals[i] = (double)frames[i];
				}

				fy = FFT(vals);

				//512 values to 6 buckets
				//freq resolution is ~90 hz

				int mult = 5000;
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
					buckets[indexMappings[i]] += (float)fy[i + freqOffset];
				}
				//foreach(float bucket in buckets)
				//{
				//	Console.Write("{0:0.#} ", bucket);
				//}
				//Console.WriteLine();
				for (int bk = 0; bk < buckets.Length; bk++)
				{
					//Console.WriteLine(bucketCounts[bk]);
					//sending[bk + 1] = (byte)((255.0 + 148.4) * Math.Exp((buckets[bk] / Math.Log(bucketCounts[bk] + 5, 2) * mult) / 255 - 1) - 148.4);
					sending[bk + 1] = (byte)((255.0 + 148.4) * Math.Exp((buckets[bk] / Math.Log(bucketCounts[bk] + 5, 6) * mult) / 255 - 1) - 148.4);
					if (sending[bk + 1] > 1) send = true;
				}

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
					port.Write(sending, 0, sending.Length);
				}
				else if (last_send)
				{
					sending[0] = 2;
					port.Write(sending, 0, sending.Length);
				}
				last_send = send;

				if (Console.KeyAvailable)
				{
					if (Console.ReadKey().KeyChar == 'q')
					{
						byte[] shutoff = new byte[62];
						shutoff[0] = 2;
						shutoff[shutoff.Length - 1] = 1;
						port.Write(shutoff, 0, shutoff.Length);
						Console.WriteLine();
						break;
					}
				}

				long delta = Environment.TickCount - start;
				Console.WriteLine(delta);
				if(delta < 16)
					Thread.Sleep((int)((1000.0 / SAMPLINGS_PER_SEC) - delta));
			}

			return true;
		}

		static bool CloseSerial(string[] args)
		{
			port.Close();

			return true;
		}

		static bool Run(string[] args)
		{
			InitRecord(null);
			ConnectSerial(null);
			AudioSerial(null);
			CloseSerial(null);
			StopRecord(null);
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
			Console.WriteLine("-- Spectra Commands --");
			foreach(string key in Commands.Keys)
			{
				Console.WriteLine(key);
			}
			return true;
		}

		static bool Echo(string[] args)
		{
			foreach(string a in args)
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
			Commands.Add("initRecord", InitRecord);
			Commands.Add("stopRecord", StopRecord);
			Commands.Add("printRecord", PrintRecord);
			Commands.Add("connectSerial", ConnectSerial);
			Commands.Add("writeSerial", WriteSerial);
			Commands.Add("closeSerial", CloseSerial);
			Commands.Add("audioSerial", AudioSerial);
			Commands.Add("r", Run);
		}

		static void Main(string[] args)
		{
			Console.WriteLine("------ Spectra Controller ------");
			InitializeCommands();
			while(!ShouldQuit)
			{
				Console.Write(">> ");
				var line = Console.ReadLine();
				var parts = line.Split(' ');
				var command = parts[0];
				if(!Commands.ContainsKey(command))
				{
					Console.WriteLine("Invalid Command Name.");
					continue;
				}
				var comargs = new string[parts.Length - 1];
				for(int i = 1; i < parts.Length; i++)
				{
					comargs[i - 1] = parts[i];
				}
				if(!Commands[command](comargs))
				{
					Console.WriteLine("Command Execution Terminated With an Error.");
				}
			}
		}
	}

	class OpenBufferedWaveProvider : IWaveProvider
	{
		public CircularBuffer circularBuffer;
		private readonly WaveFormat waveFormat;

		/// <summary>
		/// Creates a new buffered WaveProvider
		/// </summary>
		/// <param name="waveFormat">WaveFormat</param>
		public OpenBufferedWaveProvider(WaveFormat waveFormat)
		{
			this.waveFormat = waveFormat;
			BufferLength = waveFormat.AverageBytesPerSecond * 5;
			ReadFully = true;
		}

		/// <summary>
		/// If true, always read the amount of data requested, padding with zeroes if necessary
		/// By default is set to true
		/// </summary>
		public bool ReadFully { get; set; }

		/// <summary>
		/// Buffer length in bytes
		/// </summary>
		public int BufferLength { get; set; }

		/// <summary>
		/// Buffer duration
		/// </summary>
		public TimeSpan BufferDuration
		{
			get
			{
				return TimeSpan.FromSeconds((double)BufferLength / WaveFormat.AverageBytesPerSecond);
			}
			set
			{
				BufferLength = (int)(value.TotalSeconds * WaveFormat.AverageBytesPerSecond);
			}
		}

		/// <summary>
		/// If true, when the buffer is full, start throwing away data
		/// if false, AddSamples will throw an exception when buffer is full
		/// </summary>
		public bool DiscardOnBufferOverflow { get; set; }

		/// <summary>
		/// The number of buffered bytes
		/// </summary>
		public int BufferedBytes
		{
			get
			{
				return circularBuffer == null ? 0 : circularBuffer.Count;
			}
		}

		/// <summary>
		/// Buffered Duration
		/// </summary>
		public TimeSpan BufferedDuration
		{
			get { return TimeSpan.FromSeconds((double)BufferedBytes / WaveFormat.AverageBytesPerSecond); }
		}

		/// <summary>
		/// Gets the WaveFormat
		/// </summary>
		public WaveFormat WaveFormat
		{
			get { return waveFormat; }
		}

		/// <summary>
		/// Adds samples. Takes a copy of buffer, so that buffer can be reused if necessary
		/// </summary>
		public void AddSamples(byte[] buffer, int offset, int count)
		{
			// create buffer here to allow user to customise buffer length
			if (circularBuffer == null)
			{
				circularBuffer = new CircularBuffer(BufferLength);
			}

			var written = circularBuffer.Write(buffer, offset, count);
			if (written < count && !DiscardOnBufferOverflow)
			{
				throw new InvalidOperationException("Buffer full");
			}
		}

		/// <summary>
		/// Reads from this WaveProvider
		/// Will always return count bytes, since we will zero-fill the buffer if not enough available
		/// </summary>
		public int Read(byte[] buffer, int offset, int count)
		{
			int read = 0;
			if (circularBuffer != null) // not yet created
			{
				read = circularBuffer.Read(buffer, offset, count);
			}
			if (ReadFully && read < count)
			{
				// zero the end of the buffer
				Array.Clear(buffer, offset + read, count - read);
				read = count;
			}
			return read;
		}

		/// <summary>
		/// Discards all audio from the buffer
		/// </summary>
		public void ClearBuffer()
		{
			if (circularBuffer != null)
			{
				circularBuffer.Reset();
			}
		}
	}

	public class CircularBuffer
	{
		private readonly byte[] buffer;
		private readonly object lockObject;
		public int writePosition;
		public int readPosition;
		private int byteCount;

		/// <summary>
		/// Create a new circular buffer
		/// </summary>
		/// <param name="size">Max buffer size in bytes</param>
		public CircularBuffer(int size)
		{
			buffer = new byte[size];
			lockObject = new object();
		}

		/// <summary>
		/// Write data to the buffer
		/// </summary>
		/// <param name="data">Data to write</param>
		/// <param name="offset">Offset into data</param>
		/// <param name="count">Number of bytes to write</param>
		/// <returns>number of bytes written</returns>
		public int Write(byte[] data, int offset, int count)
		{
			lock (lockObject)
			{
				var bytesWritten = 0;
				if (count > buffer.Length - byteCount)
				{
					count = buffer.Length - byteCount;
					byteCount = 0;
				}
				// write to end
				int writeToEnd = Math.Min(buffer.Length - writePosition, count);
				Array.Copy(data, offset, buffer, writePosition, writeToEnd);
				writePosition += writeToEnd;
				writePosition %= buffer.Length;
				bytesWritten += writeToEnd;
				if (bytesWritten < count)
				{
					//Debug.Assert(writePosition == 0);
					// must have wrapped round. Write to start
					Array.Copy(data, offset + bytesWritten, buffer, writePosition, count - bytesWritten);
					writePosition += (count - bytesWritten);
					bytesWritten = count;
				}
				byteCount += bytesWritten;
				//Console.WriteLine(byteCount);
				return bytesWritten;
			}
		}

		/// <summary>
		/// Read from the buffer
		/// </summary>
		/// <param name="data">Buffer to read into</param>
		/// <param name="offset">Offset into read buffer</param>
		/// <param name="count">Bytes to read</param>
		/// <returns>Number of bytes actually read</returns>
		public int Read(byte[] data, int offset, int count)
		{
			lock (lockObject)
			{
				if (count > byteCount)
				{
					//count = byteCount;
				}
				int bytesRead = 0;
				int readToEnd = Math.Min(buffer.Length - readPosition, count);
				Array.Copy(buffer, readPosition, data, offset, readToEnd);
				bytesRead += readToEnd;
				readPosition += readToEnd;
				readPosition %= buffer.Length;

				if (bytesRead < count)
				{
					// must have wrapped round. Read from start
					//Debug.Assert(readPosition == 0);
					Array.Copy(buffer, readPosition, data, offset + bytesRead, count - bytesRead);
					readPosition += (count - bytesRead);
					bytesRead = count;
				}

				//byteCount -= bytesRead;
				//Debug.Assert(byteCount >= 0);
				return bytesRead;
			}
		}

		/// <summary>
		/// Maximum length of this circular buffer
		/// </summary>
		public int MaxLength => buffer.Length;

		/// <summary>
		/// Number of bytes currently stored in the circular buffer
		/// </summary>
		public int Count
		{
			get
			{
				lock (lockObject)
				{
					return byteCount;
				}
			}
		}

		/// <summary>
		/// Resets the buffer
		/// </summary>
		public void Reset()
		{
			lock (lockObject)
			{
				ResetInner();
			}
		}

		private void ResetInner()
		{
			byteCount = 0;
			readPosition = 0;
			writePosition = 0;
		}

		/// <summary>
		/// Advances the buffer, discarding bytes
		/// </summary>
		/// <param name="count">Bytes to advance</param>
		public void Advance(int count)
		{
			lock (lockObject)
			{
				if (count >= byteCount)
				{
					ResetInner();
				}
				else
				{
					byteCount -= count;
					readPosition += count;
					readPosition %= MaxLength;
				}
			}
		}
	}
}
