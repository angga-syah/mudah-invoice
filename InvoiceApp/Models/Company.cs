using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InvoiceApp.Models
{
    [Table("companies")]
    public class Company
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("company_uuid")]
        public Guid CompanyUuid { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(200)]
        [Column("company_name")]
        public string CompanyName { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        [Column("npwp")]
        public string Npwp { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        [Column("idtku")]
        public string Idtku { get; set; } = string.Empty;

        [Required]
        [Column("address")]
        public string Address { get; set; } = string.Empty;

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual ICollection<JobDescription> JobDescriptions { get; set; } = new List<JobDescription>();
        public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

        // Display property for UI
        [NotMapped]
        public string DisplayName => CompanyName;

        // Method untuk update timestamp
        public void UpdateTimestamp()
        {
            UpdatedAt = DateTime.UtcNow;
        }
    }
}