using System.Diagnostics;
using System.Runtime;
using System.Windows;

namespace MultibandCore;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
	public static bool IsSuiteControlMode { get; private set; }

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		IsSuiteControlMode = e.Args.Any(argument => string.Equals(argument, "--suite-control", StringComparison.OrdinalIgnoreCase));
		GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
		try
		{
			Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;
		}
		catch
		{
			// Some managed environments do not allow changing process priority.
		}
	}
}

