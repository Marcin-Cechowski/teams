using System.Runtime.InteropServices;
using System.Text;

namespace AntiAway;

internal static class Program
{
	[Flags]
	private enum ExecutionState : uint
	{
		EsAwaymodeRequired = 0x00000040,
		EsContinuous = 0x80000000,
		EsDisplayRequired = 0x00000002,
		EsSystemRequired = 0x00000001
	}

	[DllImport("kernel32.dll")]
	private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

	[DllImport("user32.dll")]
	private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

	[DllImport("user32.dll")]
	private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

	[StructLayout(LayoutKind.Sequential)]
	private struct LASTINPUTINFO
	{
		public uint cbSize;
		public uint dwTime;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct INPUT
	{
		public uint type;
		public InputUnion U;
	}

	[StructLayout(LayoutKind.Explicit)]
	private struct InputUnion
	{
		[FieldOffset(0)] public MOUSEINPUT mi;
		[FieldOffset(0)] public KEYBDINPUT ki;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MOUSEINPUT
	{
		public int dx;
		public int dy;
		public uint mouseData;
		public uint dwFlags;
		public uint time;
		public nuint dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct KEYBDINPUT
	{
		public ushort wVk;
		public ushort wScan;
		public uint dwFlags;
		public uint time;
		public nuint dwExtraInfo;
	}

	private const uint INPUT_MOUSE = 0;
	private const uint INPUT_KEYBOARD = 1;
	private const uint MOUSEEVENTF_MOVE = 0x0001;
	private const uint MOUSEEVENTF_MOVE_NOCOALESCE = 0x2000;
	private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
	private const uint MOUSEEVENTF_LEFTUP = 0x0004;
	private const uint KEYEVENTF_KEYUP = 0x0002;
	private const ushort VK_SHIFT = 0x10;
	private const ushort VK_A = 0x41;
	private const ushort VK_BACK = 0x08;

	private enum Mode
	{
		ExecutionStateOnly,
		MouseJiggle,
		MouseClick,
		MouseRandom,
		KeyJiggle,
		Sequence
	}

	private sealed class Config
	{
		public Mode RunMode { get; init; } = Mode.ExecutionStateOnly;
		public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);
		public int JigglePixels { get; init; } = 10;
		public TimeSpan IdleThreshold { get; init; } = TimeSpan.FromSeconds(60);
		public int ActionsPerInterval { get; init; } = 1;
		public bool AuxMouseEnabled { get; init; }
		public TimeSpan AuxMouseInterval { get; init; } = TimeSpan.Zero;
		public int AuxMousePixels { get; init; } = 10;
		public bool AuxMouseRandom { get; init; } = true;
	}

	private static readonly ThreadLocal<Random> ThreadRandom = new(() => new Random(unchecked((int)DateTime.UtcNow.Ticks)));
	private static volatile bool _isStopping;

	private static int Main(string[] args)
	{
		try
		{
			var config = ParseArgs(args);

			Console.CancelKeyPress += (_, e) =>
			{
				e.Cancel = true;
				_isStopping = true;
			};

			// Keep system and display awake continuously
			SetThreadExecutionState(
				ExecutionState.EsContinuous |
				ExecutionState.EsSystemRequired |
				ExecutionState.EsDisplayRequired);

			Console.WriteLine($"AntiAway started. Mode={config.RunMode}, Interval={config.Interval}, IdleThreshold={config.IdleThreshold}, JigglePixels={config.JigglePixels}, ActionsPerInterval={config.ActionsPerInterval}");
			if (config.AuxMouseEnabled)
			{
				Console.WriteLine($"Aux mouse: every {config.AuxMouseInterval}, pixels={config.AuxMousePixels}, random={config.AuxMouseRandom}");
			}
			Console.WriteLine("Press Ctrl+C to stop.");

			Thread? auxMouseThread = null;
			if (config.AuxMouseEnabled && config.AuxMouseInterval > TimeSpan.Zero)
			{
				auxMouseThread = new Thread(() => AuxMouseLoop(config)) { IsBackground = true };
				auxMouseThread.Start();
			}

			var next = DateTime.UtcNow;
			while (!_isStopping)
			{
				var now = DateTime.UtcNow;
				if (now >= next)
				{
					bool shouldAct;
					if (config.RunMode == Mode.MouseRandom || config.RunMode == Mode.Sequence)
					{
						shouldAct = true; // ignore idle in random/sequence modes
					}
					else
					{
						var idle = GetIdleDuration();
						shouldAct = config.IdleThreshold <= TimeSpan.Zero || idle >= config.IdleThreshold;
					}

					if (shouldAct)
					{
						for (var i = 0; i < config.ActionsPerInterval && !_isStopping; i++)
						{
							PerformAction(config);
							Thread.Sleep(250);
						}
					}
					else if (config.RunMode == Mode.ExecutionStateOnly)
					{
						// Refresh execution state periodically even if user is active
						SetThreadExecutionState(
							ExecutionState.EsContinuous |
							ExecutionState.EsSystemRequired |
							ExecutionState.EsDisplayRequired);
					}

					next = now.Add(config.Interval);
				}

				Thread.Sleep(200);
			}

			auxMouseThread?.Join(1000);
			return 0;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Error: {ex.Message}");
			return 1;
		}
		finally
		{
			// Clear continuous flag so Windows can manage sleep normally again
			SetThreadExecutionState(ExecutionState.EsContinuous);
		}
	}

	private static void AuxMouseLoop(Config config)
	{
		try
		{
			var intervalMs = (int)Math.Max(50, config.AuxMouseInterval.TotalMilliseconds);
			while (!_isStopping)
			{
				if (config.AuxMouseRandom) RandomMove(config.AuxMousePixels); else JiggleMouse(config.AuxMousePixels);
				var slept = 0;
				while (!_isStopping && slept < intervalMs)
				{
					Thread.Sleep(100);
					slept += 100;
				}
			}
		}
		catch { /* ignore background errors */ }
	}

	private static TimeSpan GetIdleDuration()
	{
		var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
		if (!GetLastInputInfo(ref lii)) return TimeSpan.Zero;
		uint last = lii.dwTime;
		uint now = (uint)Environment.TickCount;
		uint elapsed = now - last; // uint arithmetic handles wraparound
		return TimeSpan.FromMilliseconds(elapsed);
	}

	private static void PerformAction(Config config)
	{
		switch (config.RunMode)
		{
			case Mode.ExecutionStateOnly:
				SetThreadExecutionState(
					ExecutionState.EsContinuous |
					ExecutionState.EsSystemRequired |
					ExecutionState.EsDisplayRequired);
				break;
			case Mode.MouseJiggle:
				JiggleMouse(config.JigglePixels);
				break;
			case Mode.MouseClick:
				LeftClick();
				break;
			case Mode.MouseRandom:
				RandomMove(config.JigglePixels);
				break;
			case Mode.KeyJiggle:
				TapShiftKey();
				break;
			case Mode.Sequence:
				RunSequence();
				break;
		}
	}

	private static void JiggleMouse(int pixels)
	{
		if (pixels < 1) pixels = 1;
		var inputs = new INPUT[2];
		inputs[0] = new INPUT
		{
			type = INPUT_MOUSE,
			U = new InputUnion
			{
				mi = new MOUSEINPUT
				{
					dx = pixels,
					dy = pixels,
					mouseData = 0,
					dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_MOVE_NOCOALESCE,
					time = 0,
					dwExtraInfo = 0
				}
			}
		};
		inputs[1] = new INPUT
		{
			type = INPUT_MOUSE,
			U = new InputUnion
			{
				mi = new MOUSEINPUT
				{
					dx = -pixels,
					dy = -pixels,
					mouseData = 0,
					dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_MOVE_NOCOALESCE,
					time = 0,
					dwExtraInfo = 0
				}
			}
		};
		SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
	}

	private static void RandomMove(int pixels)
	{
		if (pixels < 1) pixels = 1;
		var rnd = ThreadRandom.Value ?? new Random();
		// Random direction on a circle of radius 'pixels'
		double angle = rnd.NextDouble() * Math.PI * 2.0;
		int dx = (int)Math.Round(Math.Cos(angle) * pixels);
		int dy = (int)Math.Round(Math.Sin(angle) * pixels);

		var inputs = new INPUT[2];
		inputs[0] = new INPUT
		{
			type = INPUT_MOUSE,
			U = new InputUnion
			{
				mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = 0, dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_MOVE_NOCOALESCE, time = 0, dwExtraInfo = 0 }
			}
		};
		inputs[1] = new INPUT
		{
			type = INPUT_MOUSE,
			U = new InputUnion
			{
				mi = new MOUSEINPUT { dx = -dx, dy = -dy, mouseData = 0, dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_MOVE_NOCOALESCE, time = 0, dwExtraInfo = 0 }
			}
		};
		SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
	}

	private static void LeftClick()
	{
		var inputs = new INPUT[2];
		inputs[0] = new INPUT
		{
			type = INPUT_MOUSE,
			U = new InputUnion
			{
				mi = new MOUSEINPUT { dx = 0, dy = 0, mouseData = 0, dwFlags = MOUSEEVENTF_LEFTDOWN, time = 0, dwExtraInfo = 0 }
			}
		};
		inputs[1] = new INPUT
		{
			type = INPUT_MOUSE,
			U = new InputUnion
			{
				mi = new MOUSEINPUT { dx = 0, dy = 0, mouseData = 0, dwFlags = MOUSEEVENTF_LEFTUP, time = 0, dwExtraInfo = 0 }
			}
		};
		SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
	}

	private static void TapShiftKey()
	{
		SendKey(VK_SHIFT);
	}

	private static void RunSequence()
	{
		SendKey(VK_A);
		Thread.Sleep(2000);
		SendKey(VK_BACK);
	}

	private static void SendKey(ushort vk)
	{
		var inputs = new INPUT[2];
		inputs[0] = new INPUT
		{
			type = INPUT_KEYBOARD,
			U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = 0, time = 0, dwExtraInfo = 0 } }
		};
		inputs[1] = new INPUT
		{
			type = INPUT_KEYBOARD,
			U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = KEYEVENTF_KEYUP, time = 0, dwExtraInfo = 0 } }
		};
		SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
	}

	private static Config ParseArgs(string[] args)
	{
		if (args.Length == 0) return new Config();

		var mode = Mode.ExecutionStateOnly;
		var interval = TimeSpan.FromSeconds(30);
		var jiggle = 10;
		var idle = TimeSpan.FromSeconds(60);
		var actions = 1;
		bool auxOn = false;
		var auxInterval = TimeSpan.Zero;
		var auxPixels = 10;
		var auxRandom = true;

		foreach (var raw in args)
		{
			var (key, value) = SplitArg(raw);
			switch (key)
			{
				case "--help":
				case "-h":
					PrintHelpAndExit();
					break;
				case "--mode":
					mode = value?.ToLowerInvariant() switch
					{
						"es" => Mode.ExecutionStateOnly,
						"mouse" => Mode.MouseJiggle,
						"click" => Mode.MouseClick,
						"mouse-random" => Mode.MouseRandom,
						"key" => Mode.KeyJiggle,
						"sequence" => Mode.Sequence,
						_ => throw new ArgumentException("Invalid --mode. Use es|mouse|mouse-random|click|key|sequence")
					};
					break;
				case "--interval":
					if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("--interval requires seconds or timespan");
					interval = ParseInterval(value);
					break;
				case "--jiggle-pixels":
					if (!int.TryParse(value, out jiggle) || jiggle < 1 || jiggle > 1000)
						throw new ArgumentException("--jiggle-pixels must be 1-1000");
					break;
				case "--idle-threshold":
					if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("--idle-threshold requires seconds or timespan");
					idle = ParseInterval(value);
					break;
				case "--actions":
					if (!int.TryParse(value, out actions) || actions < 1 || actions > 10)
						throw new ArgumentException("--actions must be 1-10");
					break;
				case "--mouse-on":
					auxOn = true;
					break;
				case "--mouse-interval":
					if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("--mouse-interval requires seconds or timespan");
					auxInterval = ParseInterval(value);
					break;
				case "--mouse-pixels":
					if (!int.TryParse(value, out auxPixels) || auxPixels < 1 || auxPixels > 1000)
						throw new ArgumentException("--mouse-pixels must be 1-1000");
					break;
				case "--mouse-random":
					auxRandom = string.IsNullOrWhiteSpace(value) || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
					break;
				default:
					throw new ArgumentException($"Unknown argument: {raw}");
			}
		}

		return new Config
		{
			RunMode = mode,
			Interval = interval,
			JigglePixels = jiggle,
			IdleThreshold = idle,
			ActionsPerInterval = actions,
			AuxMouseEnabled = auxOn,
			AuxMouseInterval = auxInterval,
			AuxMousePixels = auxPixels,
			AuxMouseRandom = auxRandom
		};
	}

	private static (string key, string? value) SplitArg(string raw)
	{
		var idx = raw.IndexOf('=');
		if (idx < 0) return (raw, null);
		return (raw.Substring(0, idx), raw.Substring(idx + 1));
	}

	private static TimeSpan ParseInterval(string value)
	{
		// Accept 0 to mean zero
		if (int.TryParse(value, out var seconds))
			return TimeSpan.FromSeconds(Math.Max(0, seconds));

		if (TimeSpan.TryParse(value, out var ts))
			return ts < TimeSpan.Zero ? TimeSpan.Zero : ts;

		if (value.EndsWith("s", StringComparison.OrdinalIgnoreCase) && int.TryParse(value[..^1], out var s))
			return TimeSpan.FromSeconds(Math.Max(0, s));

		if (value.EndsWith("m", StringComparison.OrdinalIgnoreCase) && int.TryParse(value[..^1], out var m))
			return TimeSpan.FromMinutes(m);

		if (value.EndsWith("h", StringComparison.OrdinalIgnoreCase) && int.TryParse(value[..^1], out var h))
			return TimeSpan.FromHours(h);

		throw new ArgumentException("Invalid interval. Examples: 1, 00:00:01, 1s, 1m, 1h");
	}

	private static void PrintHelpAndExit()
	{
		var sb = new StringBuilder();
		sb.AppendLine("AntiAway - prevent Teams idle/away status by keeping system awake and optionally simulating minimal input.");
		sb.AppendLine();
		sb.AppendLine("Usage:");
		sb.AppendLine("  AntiAway [--mode=es|mouse|mouse-random|click|key|sequence] [--interval=VALUE] [--idle-threshold=VALUE] [--jiggle-pixels=N] [--actions=N] [--mouse-on] [--mouse-interval=VALUE] [--mouse-pixels=N] [--mouse-random=true|false]");
		sb.AppendLine();
		sb.AppendLine("Options:");
		sb.AppendLine("  --mode=es           Use Windows execution state only (default)");
		sb.AppendLine("  --mode=mouse        Subtle mouse jiggle every interval (relative, no cursor jump)");
		sb.AppendLine("  --mode=mouse-random Random 2-step move each interval (ignores idle)");
		sb.AppendLine("  --mode=click        Left click once per action");
		sb.AppendLine("  --mode=key          Tap SHIFT key per action");
		sb.AppendLine("  --mode=sequence     Type 'A', wait 2s, then press Backspace");
		sb.AppendLine("  --interval=1s       Main action interval");
		sb.AppendLine("  --idle-threshold=0  Only act if user idle exceeds this duration; 0 means always");
		sb.AppendLine("  --jiggle-pixels N   Pixels to move mouse (1-1000), default 10");
		sb.AppendLine("  --actions N         Number of actions per interval (1-10), default 1");
		sb.AppendLine("  --mouse-on          Enable auxiliary mouse mover in parallel");
		sb.AppendLine("  --mouse-interval T  Auxiliary mouse interval (e.g. 2s)");
		sb.AppendLine("  --mouse-pixels N    Auxiliary mouse pixels (1-1000), e.g. 400");
		sb.AppendLine("  --mouse-random B    Auxiliary mouse random movement (true/false), default true");
		sb.AppendLine("  -h, --help          Show help");
		Console.WriteLine(sb.ToString());
		Environment.Exit(0);
	}
} 