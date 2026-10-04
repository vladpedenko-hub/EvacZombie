/// <summary>
/// External links shown in the Settings popup.
/// TODO(before store submission): fill these in. An empty value keeps the button visible
/// but it only logs a warning, so a missing URL is easy to spot in QA.
/// Google Play + App Store both REQUIRE a privacy policy URL (see Docs/mechanics/feedback-audio-vfx-haptics.md).
/// </summary>
public static class GameLinks
{
	public const string PrivacyPolicyUrl = "";   // e.g. https://yourdomain.com/evaczombie/privacy
	public const string TermsOfServiceUrl = "";  // optional
	public const string SupportEmail = "";       // e.g. support@yourdomain.com
}
