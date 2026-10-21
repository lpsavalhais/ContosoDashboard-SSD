using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class DocumentShare
{
    [Key]
    public int DocumentShareId { get; set; }

    [Required]
    public int DocumentId { get; set; }

    public int? SharedWithUserId { get; set; }

    [MaxLength(100)]
    public string? SharedWithDepartment { get; set; }

    [Required]
    public int SharedByUserId { get; set; }

    [Required]
    public DateTime SharedDate { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(DocumentId))]
    public virtual Document Document { get; set; } = null!;

    [ForeignKey(nameof(SharedWithUserId))]
    public virtual User? SharedWithUser { get; set; }

    [ForeignKey(nameof(SharedByUserId))]
    public virtual User SharedByUser { get; set; } = null!;
}
