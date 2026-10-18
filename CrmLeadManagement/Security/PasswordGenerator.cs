using System.Security.Cryptography;

namespace CrmLeadManagement.Security;

/// <summary>
/// Cryptographically secure password generator producing strong, random passwords.
/// Satisfies enterprise password complexity rules (uppercase, lowercase, numbers, symbols).
/// </summary>
public static class PasswordGenerator
{
    private const string UpperChars = "ABCDEFGHJKLMNPQRSTUVWXYZ";       // Excludes ambiguous I, O
    private const string LowerChars = "abcdefghijkmnopqrstuvwxyz";       // Excludes ambiguous l
    private const string DigitChars = "23456789";                       // Excludes ambiguous 0, 1
    private const string SymbolChars = "!@#$%^&*-_+=";
    private const string AllChars = UpperChars + LowerChars + DigitChars + SymbolChars;

    /// <summary>
    /// Generates a strong, random password of specified length (default: 14 characters).
    /// Guarantees at least 2 uppercase, 2 lowercase, 2 digits, and 2 special symbols.
    /// </summary>
    public static string Generate(int length = 14)
    {
        if (length < 10) length = 14;

        var password = new char[length];
        var pos = 0;

        // Guaranteed character sets
        password[pos++] = UpperChars[RandomNumberGenerator.GetInt32(UpperChars.Length)];
        password[pos++] = UpperChars[RandomNumberGenerator.GetInt32(UpperChars.Length)];
        password[pos++] = LowerChars[RandomNumberGenerator.GetInt32(LowerChars.Length)];
        password[pos++] = LowerChars[RandomNumberGenerator.GetInt32(LowerChars.Length)];
        password[pos++] = DigitChars[RandomNumberGenerator.GetInt32(DigitChars.Length)];
        password[pos++] = DigitChars[RandomNumberGenerator.GetInt32(DigitChars.Length)];
        password[pos++] = SymbolChars[RandomNumberGenerator.GetInt32(SymbolChars.Length)];
        password[pos++] = SymbolChars[RandomNumberGenerator.GetInt32(SymbolChars.Length)];

        // Fill remaining slots from combined set
        while (pos < length)
        {
            password[pos++] = AllChars[RandomNumberGenerator.GetInt32(AllChars.Length)];
        }

        // Fisher-Yates cryptographic shuffle
        for (var i = password.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (password[i], password[j]) = (password[j], password[i]);
        }

        return new string(password);
    }
}
