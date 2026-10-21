using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class DocumentActivity
{
    [Key]
    public int DocumentActivityId { get; set; }

    [Required]
    public int DocumentId { get; set; }

    [Required]
    [MaxLength(255)]
    public string DocumentTitle { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string FileType { get; set; } = string.Empty;

    [Required]
    public int UserId { get; set; }

    [Required]
    [MaxLength(20)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Details { get; set; }

    [Required]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserId))]
    public virtual User User { get; set; } = null!;
}

public static class DocumentActions
{
    public const string Upload = "Upload";
    public const string Download = "Download";
    public const string Preview = "Preview";
    public const string Replace = "Replace";
    public const string Delete = "Delete";
    public const string Share = "Share";
}
