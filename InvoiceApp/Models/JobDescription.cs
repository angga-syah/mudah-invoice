using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InvoiceApp.Models
{
    [Table("job_descriptions")]
    public class JobDescription
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("job_uuid")]
        public Guid JobUuid { get; set; } = Guid.NewGuid();

        [Required]
        [Column("company_id")]
        public int CompanyId { get; set; }

        [Required]
        [StringLength(200)]
        [Column("job_name")]
        public string JobName { get; set; } = string.Empty;

        [Required]
        [Column("job_description")]
        public string JobDescriptionText { get; set; } = string.Empty;

        [Required]
        [Column("price", TypeName = "decimal(15,2)")]
        public decimal Price { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("sort_order")]
        public int SortOrder { get; set; } = 0;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("CompanyId")]
        public virtual Company Company { get; set; } = null!;
        
        public virtual ICollection<InvoiceLine> InvoiceLines { get; set; } = new List<InvoiceLine>();

        // Display properties
        [NotMapped]
        public string DisplayName => JobName;

        [NotMapped]
        public string FullInfo => $"{JobName} - {Price:C}";

        [NotMapped]
        public string FormattedPrice => $"Rp {Price:N0}";

        [NotMapped]
        public string CompanyJobName => $"{Company?.CompanyName} - {JobName}";

        // Method untuk update timestamp
        public void UpdateTimestamp()
        {
            UpdatedAt = DateTime.UtcNow;
        }

        // Helper method untuk search
        public bool MatchesSearch(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm)) return true;
            
            searchTerm = searchTerm.ToLower();
            return JobName.ToLower().Contains(searchTerm) ||
                   JobDescriptionText.ToLower().Contains(searchTerm);
        }

        // Validation method
        public bool IsValid(out List<string> errors)
        {
            errors = new List<string>();
            
            if (string.IsNullOrWhiteSpace(JobName))
                errors.Add("Job Name tidak boleh kosong");
                
            if (string.IsNullOrWhiteSpace(JobDescriptionText))
                errors.Add("Job Description tidak boleh kosong");
                
            if (Price < 0)
                errors.Add("Harga tidak boleh negatif");
                
            if (CompanyId <= 0)
                errors.Add("Company harus dipilih");
            
            return errors.Count == 0;
        }

        // Clone method untuk duplicate job
        public JobDescription Clone()
        {
            return new JobDescription
            {
                CompanyId = this.CompanyId,
                JobName = this.JobName + " (Copy)",
                JobDescriptionText = this.JobDescriptionText,
                Price = this.Price,
                IsActive = this.IsActive,
                SortOrder = this.SortOrder + 1
            };
        }
    }
}