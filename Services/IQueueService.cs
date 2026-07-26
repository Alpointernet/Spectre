using System.Text.Json.Nodes;

namespace Spectre.Services;

public interface IQueueService
{
	JsonArray CurrentQueue { get; set; }

	int CurrentQueueIndex { get; set; }

	int OriginalQueueSize { get; set; }

	bool IsShuffleOn { get; set; }

	void InitQueueAndShuffle(JsonArray newQueue, int newIndex);

	void ShuffleRemainingQueue();

	void UnshuffleRemainingQueue();

	int GetNextQueueIndex();

	int FindQueueIndexByVideoId(string videoId);
}
