using System.ComponentModel.DataAnnotations;

namespace TransferApi.Domain.Requests;

public record CreateTransferRequest
{
    [Required]
    [StringLength(34, MinimumLength = 1)]
    public string FromAccount { get; init; } = null!;

    [Required]
    [StringLength(34, MinimumLength = 1)]
    public string ToAccount { get; init; } = null!;

    [Range(typeof(decimal), "0.01", "9999999999999999.99")]
    public decimal Amount { get; init; }

    [Required]
    [RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Currency must be a 3-letter ISO code.")]
    public string Currency { get; init; } = null!;
}
