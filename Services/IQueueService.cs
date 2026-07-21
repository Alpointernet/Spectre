using Newtonsoft.Json.Linq;

namespace Spectre.Services;

public interface IQueueService
{
	JArray CurrentQueue { get; set; }

	int CurrentQueueIndex { get; set; }

	int OriginalQueueSize { get; set; }

	bool IsShuffleOn { get; set; }

	void InitQueueAndShuffle(JArray newQueue, int newIndex);

	void ShuffleRemainingQueue();

	void UnshuffleRemainingQueue();

	int GetNextQueueIndex();

	int FindQueueIndexByVideoId(string videoId);
}
