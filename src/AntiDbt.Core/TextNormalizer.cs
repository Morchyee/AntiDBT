namespace AntiDbt.Core;

public static class TextNormalizer
{
    public static string NormalizeAsrText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var trimmed = text.Trim();

        // Count letters, uppercase letters, and detect CJK
        int letterCount = 0;
        int upperCount = 0;
        bool hasCjk = false;

        foreach (char c in trimmed)
        {
            if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3040 && c <= 0x30FF))
            {
                hasCjk = true;
                break;
            }
            if (char.IsLetter(c))
            {
                letterCount++;
                if (char.IsUpper(c))
                    upperCount++;
            }
        }

        // Keep as-is if CJK, very short (e.g. acronym like "AI" or "OK"), or already has mixed case
        if (hasCjk || letterCount < 3 || upperCount != letterCount)
            return trimmed;

        // Convert pure ALL-CAPS ASCII/Latin stream to natural sentence case
        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return trimmed;

        var sb = new System.Text.StringBuilder();
        bool newSentence = true;

        for (int i = 0; i < words.Length; i++)
        {
            var word = words[i].ToLowerInvariant();
            if (newSentence && word.Length > 0)
            {
                word = char.ToUpperInvariant(word[0]) + (word.Length > 1 ? word[1..] : "");
                newSentence = false;
            }
            else if (word is "i" or "i'm" or "i'll" or "i'd" or "i've")
            {
                word = char.ToUpperInvariant(word[0]) + (word.Length > 1 ? word[1..] : "");
            }

            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(word);

            if (word.EndsWith('.') || word.EndsWith('!') || word.EndsWith('?'))
                newSentence = true;
        }

        return sb.ToString();
    }
}
