using System.Diagnostics;
using System.Runtime;
using System.Windows;

namespace DiscordMultiband;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
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

