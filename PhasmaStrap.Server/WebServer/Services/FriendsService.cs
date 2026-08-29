using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PhasmaStrap.Server.Common;
using PhasmaStrap.Server.WebServer.Enums;
using PhasmaStrap.Server.WebServer.Models;

namespace PhasmaStrap.Server.WebServer.Services;

internal class FriendsService
{
	private const int MaxRelationships = 4096;
	private readonly ConcurrentDictionary<(int First, int Second), FriendStatus> _relationships = new();
	private readonly object _admissionGate = new();
	private readonly object _persistLock = new();

	public static FriendsService Instance { get; } = new FriendsService();

	private FriendsService()
	{
		Load();
	}

	public bool AreFriend(int user, int otherUser)
	{
		return _relationships.TryGetValue(Normalize(user, otherUser), out FriendStatus status) && status == FriendStatus.Friend;
	}

	public IEnumerable<int> AreFriends(int user, IEnumerable<int> users)
	{
		return users.Where(otherUser => AreFriend(user, otherUser)).ToArray();
	}

	public bool TryCreateFriend(int inviter, int invitee)
	{
		(int First, int Second) pair = Normalize(inviter, invitee);
		lock (_admissionGate)
		{
			if (!_relationships.ContainsKey(pair) && _relationships.Count >= MaxRelationships)
			{
				return false;
			}
			_relationships[pair] = FriendStatus.Friend;
		}
		Save();
		return true;
	}

	public void BreakFriend(int inviter, int invitee)
	{
		if (_relationships.TryRemove(Normalize(inviter, invitee), out _))
		{
			Save();
		}
	}

	public IEnumerable<FriendRequest> GetFriends(int user, int skip = 0)
	{
		return _relationships
			.Where(pair => pair.Value == FriendStatus.Friend && (pair.Key.First == user || pair.Key.Second == user))
			.Skip(skip)
			.Select(pair => new FriendRequest
			{
				Inviter = pair.Key.First,
				Invitee = pair.Key.Second,
				Status = pair.Value
			})
			.ToArray();
	}

	public IEnumerable<int> GetFriendIDs(int user, int skip = 0)
	{
		return GetFriends(user, skip).Select(request => request.Inviter != user ? request.Inviter : request.Invitee).ToArray();
	}

	private static (int First, int Second) Normalize(int user, int otherUser)
	{
		return user <= otherUser ? (user, otherUser) : (otherUser, user);
	}

	private void Load()
	{
		try
		{
			if (!File.Exists(PathHelper.Friends))
			{
				return;
			}
			string json = File.ReadAllText(PathHelper.Friends);
			List<FriendRequest>? entries = JsonSerializer.Deserialize<List<FriendRequest>>(json);
			if (entries == null)
			{
				return;
			}
			foreach (FriendRequest entry in entries.Take(MaxRelationships))
			{
				if (entry.Inviter <= 0 || entry.Invitee <= 0 || entry.Inviter == entry.Invitee)
				{
					continue;
				}
				_relationships[Normalize(entry.Inviter, entry.Invitee)] = entry.Status ?? FriendStatus.Friend;
			}
		}
		catch (Exception value)
		{
			Logger.Instance.Error($"Failed to load friends from disk: {value}");
		}
	}

	private void Save()
	{
		lock (_persistLock)
		{
			try
			{
				List<FriendRequest> entries = _relationships
					.Select(pair => new FriendRequest
					{
						Inviter = pair.Key.First,
						Invitee = pair.Key.Second,
						Status = pair.Value
					})
					.ToList();
				string contents = JsonSerializer.Serialize(entries);
				Directory.CreateDirectory(PathHelper.UserAppData);
				string temporary = PathHelper.Friends + "." + Guid.NewGuid().ToString("N") + ".tmp";
				try
				{
					File.WriteAllText(temporary, contents);
					File.Move(temporary, PathHelper.Friends, true);
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
				Logger.Instance.Error($"Failed to save friends to disk: {value}");
			}
		}
	}
}
