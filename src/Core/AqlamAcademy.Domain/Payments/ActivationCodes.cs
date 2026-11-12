using System.Security.Cryptography;

namespace AqlamAcademy.Domain.Payments;

/// <summary>
/// A set of activation codes for one course, e.g. for one center desk or distributor (stories 3.1.2, 3.1.3).
/// Codes are free to make; students who join with one count toward the teacher's monthly fee.
/// </summary>
public class ActivationCodeBatch
{
    public const int MaxQuantity = 1000;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid CourseId { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    /// <summary>Who the codes are for, e.g. "مكتبة النور - فرع العليا".</summary>
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Unused codes of a cancelled batch stop working; used ones keep their access.</summary>
    public DateTime? CancelledAtUtc { get; set; }

    public ICollection<ActivationCode> Codes { get; set; } = new List<ActivationCode>();
}

/// <summary>
/// One code. Only a keyed hash is stored, so a database leak does not leak usable codes; the plain codes are shown
/// once, when the batch is made. The last four characters are kept to help staff recognise a card.
/// </summary>
public class ActivationCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BatchId { get; set; }
    public ActivationCodeBatch Batch { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public Guid CourseId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public string LastFour { get; set; } = string.Empty;
    public Guid? RedeemedByUserId { get; set; }
    public DateTime? RedeemedAtUtc { get; set; }
}

/// <summary>A platform key (e.g. for hashing activation codes), stored encrypted with the Data Protection key ring.</summary>
public class PlatformSecret
{
    public string Name { get; set; } = string.Empty;
    public string ProtectedValue { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class ActivationCodeFormat
{
    /// <summary>No 0/O, 1/I/L: codes are typed from printed cards.</summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 12;

    public static string Generate()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    /// <summary>Upper case, no spaces or dashes; Arabic-Indic digits typed on Arabic keyboards are accepted.</summary>
    public static string Clean(string input)
    {
        var chars = input.Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '_')
            .Select(c => c switch
            {
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                _ => char.ToUpperInvariant(c)
            });
        return new string(chars.ToArray());
    }

    /// <summary>XXXX-XXXX-XXXX for printing.</summary>
    public static string Display(string code) =>
        code.Length == Length ? $"{code[..4]}-{code[4..8]}-{code[8..]}" : code;

    public static string Hash(string cleanCode, byte[] key) =>
        Convert.ToHexString(HMACSHA256.HashData(key, System.Text.Encoding.UTF8.GetBytes(cleanCode)));
}
