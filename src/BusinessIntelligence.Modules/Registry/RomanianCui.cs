namespace BusinessIntelligence.Modules.Registry;

public enum CuiState
{
    Valid,
    Missing,
    Zero,
    NonNumeric,
    TooShort,
    InvalidChecksum,

    /// <summary>11–13 digits: a personal numeric code (CNP) of an individual, never a CUI. The value is discarded.</summary>
    PersonalCode,
}

public static class RomanianCui
{
    private const string Key = "753217532";

    /// <summary>Classifies a registry CUI cell; returns the CUI only when its check digit is valid.</summary>
    public static (CuiState State, long? Value) Parse(ReadOnlySpan<char> text)
    {
        text = text.Trim();
        if (text.IsEmpty)
        {
            return (CuiState.Missing, null);
        }

        foreach (var character in text)
        {
            if (!char.IsAsciiDigit(character))
            {
                return (CuiState.NonNumeric, null);
            }
        }

        var digits = text.TrimStart('0');
        if (digits.IsEmpty)
        {
            return (CuiState.Zero, null);
        }

        if (digits.Length > 10)
        {
            return (CuiState.PersonalCode, null);
        }

        if (digits.Length < 2)
        {
            return (CuiState.TooShort, null);
        }

        var body = digits[..^1];
        var offset = Key.Length - body.Length;
        var sum = 0;
        for (var index = 0; index < body.Length; index++)
        {
            sum += (body[index] - '0') * (Key[offset + index] - '0');
        }

        var check = sum * 10 % 11 % 10;
        return check == digits[^1] - '0'
            ? (CuiState.Valid, long.Parse(digits, provider: System.Globalization.CultureInfo.InvariantCulture))
            : (CuiState.InvalidChecksum, null);
    }
}
