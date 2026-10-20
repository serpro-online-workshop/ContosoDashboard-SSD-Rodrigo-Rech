using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class DocumentActivity
{
    [Key]
    public int DocumentActivityId { get; set; }

    public int? DocumentId { get; set; }

    public int? ActorUserId { get; set; }

    [Required]
    public DocumentActivityType ActionType { get; set; }

    [Required]
    [MaxLength(255)]
    public string DocumentTitleSnapshot { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? FileType { get; set; }

    [MaxLength(255)]
    public string? UploaderDisplayNameSnapshot { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(1000)]
    public string? Details { get; set; }

    [ForeignKey(nameof(DocumentId))]
    public virtual Document? Document { get; set; }

    [ForeignKey(nameof(ActorUserId))]
    public virtual User? ActorUser { get; set; }
}
