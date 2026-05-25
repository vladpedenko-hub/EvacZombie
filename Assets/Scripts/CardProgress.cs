[System.Serializable]
public class CardProgress
{
	public string cardId;      // Имя файла CardData (например, "Card_Car")
	public int currentLevel = 1;
	public int collectedShards = 0;

	public CardProgress(string id)
	{
		cardId = id;
	}
}