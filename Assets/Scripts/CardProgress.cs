[System.Serializable]
public class CardProgress
{
	public string cardId;      // Name of the CardData asset (e.g. "Card_Car")
	public int currentLevel = 1;
	public int collectedShards = 0;

	public CardProgress(string id)
	{
		cardId = id;
	}
}