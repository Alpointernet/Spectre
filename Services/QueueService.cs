using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Spectre.Services;

public class QueueService : IQueueService
{
	private Random _rand = new Random();

	public JsonArray CurrentQueue { get; set; } = new JsonArray();

	public int CurrentQueueIndex { get; set; } = -1;

	public int OriginalQueueSize { get; set; }

	public bool IsShuffleOn { get; set; }

	public void UnshuffleRemainingQueue()
	{
		if (CurrentQueue == null || CurrentQueueIndex < 0)
		{
			return;
		}
		int startIndex = CurrentQueueIndex + 1;
		int endIndex = ((OriginalQueueSize > 1) ? (Math.Min(OriginalQueueSize, CurrentQueue.Count) - 1) : (CurrentQueue.Count - 1));
		if (endIndex > startIndex)
		{
			List<JsonNode> listToSort = new List<JsonNode>();
			for (int i = startIndex; i <= endIndex; i++)
			{
				listToSort.Add(CurrentQueue[i]);
			}
			listToSort.Sort(delegate(JsonNode a, JsonNode b)
			{
				int num = a?["originalIndex"]?.GetValue<int>() ?? int.MaxValue;
				int value = b?["originalIndex"]?.GetValue<int>() ?? int.MaxValue;
				return num.CompareTo(value);
			});
			for (int i = startIndex; i <= endIndex; i++)
			{
				CurrentQueue[i] = null;
			}
			for (int i2 = 0; i2 < listToSort.Count; i2++)
			{
				CurrentQueue[startIndex + i2] = listToSort[i2];
			}
		}
	}

	public void InitQueueAndShuffle(JsonArray newQueue, int newIndex)
	{
		for (int i = 0; i < newQueue.Count; i++)
		{
			if (newQueue[i] is JsonObject obj)
			{
				obj["originalIndex"] = i;
			}
		}
		CurrentQueue = newQueue;
		CurrentQueueIndex = newIndex;
		OriginalQueueSize = newQueue.Count;
		if (IsShuffleOn)
		{
			ShuffleRemainingQueue();
		}
	}

	public void ShuffleRemainingQueue()
	{
		if (CurrentQueue == null || CurrentQueueIndex < 0)
		{
			return;
		}
		int startIndex = CurrentQueueIndex + 1;
		int endIndex = ((OriginalQueueSize > 1) ? (Math.Min(OriginalQueueSize, CurrentQueue.Count) - 1) : (CurrentQueue.Count - 1));
		if (endIndex > startIndex)
		{
			for (int i = endIndex; i > startIndex; i--)
			{
				int j = _rand.Next(startIndex, i + 1);
				JsonNode tempI = CurrentQueue[i];
				JsonNode tempJ = CurrentQueue[j];
				CurrentQueue[i] = null;
				if (i != j) {
					CurrentQueue[j] = null;
				}
				CurrentQueue[i] = tempJ;
				CurrentQueue[j] = tempI;
			}
		}
	}

	public int GetNextQueueIndex()
	{
		if (CurrentQueue == null || CurrentQueue.Count == 0)
		{
			return -1;
		}
		int sequentialIndex = CurrentQueueIndex + 1;
		if (sequentialIndex >= CurrentQueue.Count)
		{
			return -1;
		}
		return sequentialIndex;
	}

	public int FindQueueIndexByVideoId(string videoId)
	{
		if (CurrentQueue == null)
		{
			return -1;
		}
		for (int i = 0; i < CurrentQueue.Count; i++)
		{
			if ((string?)CurrentQueue[i]?["videoId"] == videoId)
			{
				return i;
			}
		}
		return -1;
	}
}
