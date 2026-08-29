using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using PhasmaStrap.Server.Common;
using PhasmaStrap.Server.Auth.Proxy;
using PhasmaStrap.Server.Auth.Web;

namespace PhasmaStrap.Server.Auth;

// Ported from Voidstrap's ServerAuthInterface console app (VoidstrapClient.ServerAuthInterface/Program.cs).
// In Voidstrap this subsystem is a standalone process. It is NOT currently wired into
// PhasmaStrap.Server's single Program.Main - it is ported here as a callable host
// (call AuthHost.Run(args) from a background thread/Task) for future integration,
// since PhasmaStrap.Server's WebServer/Program.cs already owns the process entry point.
public class AuthHost
{
	private static Process? _RobloxProcess;
	private static readonly CancellationTokenSource Lifetime = new CancellationTokenSource();

	public static void Run(string[] args)
	{
		AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
		Console.CancelKeyPress += OnCancelKeyPress;
		Config.Load(args);
		Logger.Instance.Info($"PhasmaStrap Server Authorization Interface {Assembly.GetExecutingAssembly().GetName().Version}");
		Logger.Instance.Info($"Base port: {Config.Default.BasePort}");
		List<Task> list = new List<Task>();
		CheckProcessId();
		Logger.Instance.Info("Starting public Roblox proxy");
		string localIP = Utils.GetLocalIP();
		PhasmaStrap.Server.Auth.Proxy.Server server = new PhasmaStrap.Server.Auth.Proxy.Server();
		list.Add(server.Start(localIP, Config.Default.RobloxPort, Config.Default.ProxyPort, token: Lifetime.Token));
		Logger.Instance.Info("Starting web server");
		list.Add(PhasmaStrap.Server.Auth.Web.Server.Start(Lifetime.Token));
		Logger.Instance.Info($"Public Roblox proxy started on {Config.Default.ProxyPort} (UDP)");
		Logger.Instance.Info($"Web server started on {Config.Default.WebServerPort} (TCP)");
		Logger.Instance.Info($"Private Roblox server started on {Config.Default.RobloxPort} (UDP) (keep this port closed)");
		if (!Console.IsInputRedirected)
		{
			_ = Task.Run(delegate
			{
				try
				{
					InputMonitor.Start(Lifetime.Token);
				}
				catch (Exception value)
				{
					Logger.Instance.Warn("Console input stopped: " + value.Message);
				}
			});
			InputMonitor.DisplayHelpMenu();
		}
		Task.WaitAny(list.ToArray());
		Lifetime.Cancel();
		Logger.Instance.Info("A task closed, exiting!");
	}

	private static void CheckProcessId()
	{
		if (Config.Default.ClientProcessId == -1)
		{
			Logger.Instance.Warn("Client process id is not defined, disabling process closing checks.");
			return;
		}
		try
		{
			_RobloxProcess = Process.GetProcessById(Config.Default.ClientProcessId);
		}
		catch (Exception value)
		{
			Logger.Instance.Error($"Failed to find process by id {Config.Default.ClientProcessId}: {value}");
			Logger.Instance.Warn("Closing!");
			Environment.Exit(1001);
			return;
		}
		_ = ClientProcessClosureCheckAsync(Lifetime.Token);
	}

	private static async Task ClientProcessClosureCheckAsync(CancellationToken token)
	{
		Logger.Instance.Info("Starting client process closure check");
		await _RobloxProcess!.WaitForExitAsync(token).ConfigureAwait(false);
		Logger.Instance.Warn("Client process closed, exiting!");
		Environment.Exit(1002);
	}

	private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
	{
		e.Cancel = true;
		Lifetime.Cancel();
	}

	private static void OnProcessExit(object? sender, EventArgs e)
	{
		AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
		AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
		Console.CancelKeyPress -= OnCancelKeyPress;
		Lifetime.Cancel();
		Logger.Instance.Info("Got process exit event.");
		if (_RobloxProcess != null)
		{
			Logger.Instance.Warn("Closing Roblox client");
			try
			{
				if (!_RobloxProcess.HasExited && (!_RobloxProcess.CloseMainWindow() || !_RobloxProcess.WaitForExit(3000)))
				{
					_RobloxProcess.Kill(entireProcessTree: true);
				}
			}
			catch
			{
			}
			_RobloxProcess.Dispose();
			_RobloxProcess = null;
		}
		Lifetime.Dispose();
	}

	private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
	{
		Exception ex = (Exception)e.ExceptionObject;
		try
		{
			Logger.Instance.Error("Unhandled exception!");
			Logger.Instance.Error(ex.ToString());
		}
		catch
		{
		}
	}
}
