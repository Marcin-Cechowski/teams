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
	private static extern bool GetCursorPos(out POINT lpPoint);

	[DllImport("user32.dll")]
	private static extern bool SetCursorPos(int X, int Y);

	[DllImport("user32.dll")]
	private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

	[StructLayout(LayoutKind.Sequential)]
	private struct POINT
	{
		public int X;
		public int Y;
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
	private const uint KEYEVENTF_KEYUP = 0x0002;
	private const ushort VK_SHIFT = 0x10;

	private enum Mode
	{
		ExecutionStateOnly,
		MouseJiggle,
		KeyJiggle
	}

	private sealed class Config
	{
		public Mode RunMode { get; init; } = Mode.ExecutionStateOnly;
		public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(2);
		public int JigglePixels { get; init; } = 2;
	}

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

			Console.WriteLine($"AntiAway started. Mode={config.RunMode}, Interval={config.Interval}, JigglePixels={config.JigglePixels}");
			Console.WriteLine("Press Ctrl+C to stop.");

			var next = DateTime.UtcNow;
			while (!_isStopping)
			{
				var now = DateTime.UtcNow;
				if (now >= next)
				{
					PerformAction(config);
					next = now.Add(config.Interval);
				}

				Thread.Sleep(200);
			}

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

	private static void PerformAction(Config config)
	{
		switch (config.RunMode)
		{
			case Mode.ExecutionStateOnly:
				// Refresh the request to be safe (though EsContinuous holds it)
				SetThreadExecutionState(
					ExecutionState.EsContinuous |
					ExecutionState.EsSystemRequired |
					ExecutionState.EsDisplayRequired);
				break;
			case Mode.MouseJiggle:
				JiggleMouse(config.JigglePixels);
				break;
			case Mode.KeyJiggle:
				TapShiftKey();
				break;
		}
	}

	private static void JiggleMouse(int pixels)
	{
		if (!GetCursorPos(out var pt)) return;
		var x1 = pt.X + pixels;
		var y1 = pt.Y;
		SetCursorPos(x1, y1);
		Thread.Sleep(20);
		SetCursorPos(pt.X, pt.Y);

		// Also send a minimal mouse move event to ensure input is registered
		var inputs = new INPUT[1];
		inputs[0] = new INPUT
		{
			type = INPUT_MOUSE,
			U = new InputUnion
			{
				mi = new MOUSEINPUT
				{
					dx = 0,
					dy = 0,
					mouseData = 0,
					dwFlags = MOUSEEVENTF_MOVE,
					time = 0,
					dwExtraInfo = 0
				}
			}
		};
		SendInput(1, inputs, Marshal.SizeOf<INPUT>());
	}

	private static void TapShiftKey()
	{
		var inputs = new List<INPUT>
		{
			new INPUT
			{
				type = INPUT_KEYBOARD,
				U = new InputUnion
				{
					ki = new KEYBDINPUT
					{
						wVk = VK_SHIFT,
						wScan = 0,
						dwFlags = 0,
						time = 0,
						dwExtraInfo = 0
					}
				}
			},
			new INPUT
			{
				type = INPUT_KEYBOARD,
				U = new InputUnion
				{
					ki = new KEYBDINPUT
					{
						wVk = VK_SHIFT,
						wScan = 0,
						dwFlags = KEYEVENTF_KEYUP,
						time = 0,
						dwExtraInfo = 0
					}
				}
			}
		};
		SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
	}

	private static Config ParseArgs(string[] args)
	{
		if (args.Length == 0) return new Config();

		var mode = Mode.ExecutionStateOnly;
		var interval = TimeSpan.FromMinutes(2);
		var jiggle = 2;

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
						"key" => Mode.KeyJiggle,
						_ => throw new ArgumentException("Invalid --mode. Use es|mouse|key")
					};
					break;
				case "--interval":
					if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("--interval requires seconds or timespan");
					interval = ParseInterval(value);
					break;
				case "--jiggle-pixels":
					if (!int.TryParse(value, out jiggle) || jiggle < 1 || jiggle > 20)
						throw new ArgumentException("--jiggle-pixels must be 1-20");
					break;
				default:
					throw new ArgumentException($"Unknown argument: {raw}");
			}
		}

		return new Config { RunMode = mode, Interval = interval, JigglePixels = jiggle };
	}

	private static (string key, string? value) SplitArg(string raw)
	{
		var idx = raw.IndexOf('=');
		if (idx < 0) return (raw, null);
		return (raw.Substring(0, idx), raw.Substring(idx + 1));
	}

	private static TimeSpan ParseInterval(string value)
	{
		// Accept numbers as seconds, or TimeSpan formats like 00:02:00, or suffixed forms like 30s, 5m
		if (int.TryParse(value, out var seconds) && seconds > 0)
			return TimeSpan.FromSeconds(seconds);

		if (TimeSpan.TryParse(value, out var ts) && ts > TimeSpan.Zero)
			return ts;

		if (value.EndsWith("s", StringComparison.OrdinalIgnoreCase) && int.TryParse(value[..^1], out var s))
			return TimeSpan.FromSeconds(s);

		if (value.EndsWith("m", StringComparison.OrdinalIgnoreCase) && int.TryParse(value[..^1], out var m))
			return TimeSpan.FromMinutes(m);

		if (value.EndsWith("h", StringComparison.OrdinalIgnoreCase) && int.TryParse(value[..^1], out var h))
			return TimeSpan.FromHours(h);

		throw new ArgumentException("Invalid interval. Examples: 120, 00:02:00, 90s, 5m, 1h");
	}

	private static void PrintHelpAndExit()
	{
		var sb = new StringBuilder();
		sb.AppendLine("AntiAway - prevent Teams idle/away status by keeping system awake and optionally simulating minimal input.");
		sb.AppendLine();
		sb.AppendLine("Usage:");
		sb.AppendLine("  AntiAway [--mode=es|mouse|key] [--interval=VALUE] [--jiggle-pixels=N]");
		sb.AppendLine();
		sb.AppendLine("Options:");
		sb.AppendLine("  --mode=es       Use Windows execution state only (default)");
		sb.AppendLine("  --mode=mouse    Subtle mouse jiggle every interval");
		sb.AppendLine("  --mode=key      Tap SHIFT key every interval");
		sb.AppendLine("  --interval=120  Interval in seconds, timespan (00:02:00), or suffix (90s, 5m)");
		sb.AppendLine("  --jiggle-pixels N  Pixels to move mouse (1-20), default 2");
		sb.AppendLine("  -h, --help      Show help");
		Console.WriteLine(sb.ToString());
		Environment.Exit(0);
	}
} 