using UnityEngine;

public static class CardVisuals
{
	public static Color GetRarityColor(CardRarity rarity)
	{
		switch (rarity)
		{
			case CardRarity.Common: return Color.white;
			case CardRarity.Rare: return new Color(0.2f, 0.6f, 1f);
			case CardRarity.Epic: return new Color(0.7f, 0.2f, 1f);
			case CardRarity.Legendary: return new Color(1f, 0.6f, 0f);
			default: return Color.white;
		}
	}
}
