using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class DocumentTag
{
    [Key]
    public int DocumentTagId { get; set; }

    [Required]
    public int DocumentId { get; set; }

    [Required]
    [MaxLength(100)]
    public string TagText { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string NormalizedTagText { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(DocumentId))]
    public virtual Document Document { get; set; } = null!;
}
