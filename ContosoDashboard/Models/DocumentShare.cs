using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class DocumentShare
{
    [Key]
    public int DocumentShareId { get; set; }

    [Required]
    public int DocumentId { get; set; }

    public int? TargetUserId { get; set; }

    [MaxLength(100)]
    public string? TargetDepartment { get; set; }

    [Required]
    public int GrantedByUserId { get; set; }

    public DateTime GrantedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? RevokedAtUtc { get; set; }

    [NotMapped]
    public bool IsActive => !RevokedAtUtc.HasValue;

    [ForeignKey(nameof(DocumentId))]
    public virtual Document Document { get; set; } = null!;

    [ForeignKey(nameof(TargetUserId))]
    public virtual User? TargetUser { get; set; }

    [ForeignKey(nameof(GrantedByUserId))]
    public virtual User GrantedByUser { get; set; } = null!;
}
