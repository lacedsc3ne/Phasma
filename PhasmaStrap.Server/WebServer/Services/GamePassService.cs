using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PhasmaStrap.Server.Common;

namespace PhasmaStrap.Server.WebServer.Services;

internal class GamePassService
{
	private const int MaxGamePasses = 4096;
	private readonly HashSet<long> _owned = new();
	private readonly object _lock = new();

	public static GamePassService Instance { get; } = new GamePassService();

	private GamePassService()
	{
		Load();
	}

	public bool Owns(long gamePassId)
	{
		lock (_lock)
		{
			return _owned.Contains(gamePassId);
		}
	}

	public bool Grant(long gamePassId)
	{
		lock (_lock)
		{
			if (!_owned.Contains(gamePassId) && _owned.Count >= MaxGamePasses)
			{
				return false;
			}
			_owned.Add(gamePassId);
		}
		Save();
		return true;
	}

	private void Load()
	{
		try
		{
			if (!File.Exists(PathHelper.GamePasses))
			{
				return;
			}
			string json = File.ReadAllText(PathHelper.GamePasses);
			List<long>? entries = JsonSerializer.Deserialize<List<long>>(json);
			if (entries == null)
			{
				return;
			}
			foreach (long id in entries)
			{
				if (id <= 0 || _owned.Count >= MaxGamePasses)
				{
					continue;
				}
				_owned.Add(id);
			}
		}
		catch (Exception value)
		{
			Logger.Instance.Error($"Failed to load gamepasses from disk: {value}");
		}
	}

	private void Save()
	{
		try
		{
			List<long> entries;
			lock (_lock)
			{
				entries = new List<long>(_owned);
			}
			string contents = JsonSerializer.Serialize(entries);
			Directory.CreateDirectory(PathHelper.UserAppData);
			string temporary = PathHelper.GamePasses + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				File.WriteAllText(temporary, contents);
				File.Move(temporary, PathHelper.GamePasses, true);
			}
			finally
			{
				if (File.Exists(temporary))
				{
					File.Delete(temporary);
				}
			}
		}
		catch (Exception value)
		{
			Logger.Instance.Error($"Failed to save gamepasses to disk: {value}");
		}
	}
}
