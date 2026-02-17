using System;
using System.ComponentModel.DataAnnotations;

namespace Storarr.Models
{
    public class ActivityLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int MediaItemId { get; set; }

        public MediaItem? MediaItem { get; set; }

        [Required]
        [MaxLength(50)]
        public string Action { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string FromState { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string ToState { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Details { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
