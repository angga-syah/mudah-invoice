using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InvoiceApp.Models
{
    [Table("invoice_lines")]
    public class InvoiceLine
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("line_uuid")]
        public Guid LineUuid { get; set; } = Guid.NewGuid();

        [Required]
        [Column("invoice_id")]
        public int InvoiceId { get; set; }

        [Required]
        [Column("baris")]
        public int Baris { get; set; }

        [Required]
        [Column("line_order")]
        public int LineOrder { get; set; }

        [Required]
        [Column("tka_id")]
        public int TkaId { get; set; }

        [Required]
        [Column("job_description_id")]
        public int JobDescriptionId { get; set; }

        [StringLength(200)]
        [Column("custom_job_name")]
        public string? CustomJobName { get; set; }

        [Column("custom_job_description")]
        public string? CustomJobDescription { get; set; }

        [Column("custom_price", TypeName = "decimal(15,2)")]
        public decimal? CustomPrice { get; set; }

        [Required]
        [Column("quantity")]
        public int Quantity { get; set; } = 1;

        [Required]
        [Column("unit_price", TypeName = "decimal(15,2)")]
        public decimal UnitPrice { get; set; }

        [Required]
        [Column("line_total", TypeName = "decimal(15,2)")]
        public decimal LineTotal { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("InvoiceId")]
        public virtual Invoice Invoice { get; set; } = null!;

        [ForeignKey("TkaId")]
        public virtual TkaWorker TkaWorker { get; set; } = null!;

        [ForeignKey("JobDescriptionId")]
        public virtual JobDescription JobDescription { get; set; } = null!;

        // Display properties
        [NotMapped]
        public string TkaName => TkaWorker?.Nama ?? "";

        [NotMapped]
        public string JobName => !string.IsNullOrEmpty(CustomJobName) ? CustomJobName : JobDescription?.JobName ?? "";

        [NotMapped]
        public string JobDescriptionText => !string.IsNullOrEmpty(CustomJobDescription) ? CustomJobDescription : JobDescription?.JobDescriptionText ?? "";

        [NotMapped]
        public decimal EffectivePrice => CustomPrice ?? JobDescription?.Price ?? UnitPrice;

        [NotMapped]
        public string FormattedUnitPrice => $"Rp {UnitPrice:N0}";

        [NotMapped]
        public string FormattedLineTotal => $"Rp {LineTotal:N0}";

        [NotMapped]
        public string DisplayInfo => $"{TkaName} - {JobName}";

        [NotMapped]
        public bool HasCustomData => !string.IsNullOrEmpty(CustomJobName) || !string.IsNullOrEmpty(CustomJobDescription) || CustomPrice.HasValue;

        // Methods
        public void CalculateLineTotal()
        {
            UnitPrice = EffectivePrice;
            LineTotal = UnitPrice * Quantity;
        }

        public void SetCustomJob(string jobName, string description, decimal price)
        {
            CustomJobName = jobName;
            CustomJobDescription = description;
            CustomPrice = price;
            CalculateLineTotal();
        }

        public void ClearCustomJob()
        {
            CustomJobName = null;
            CustomJobDescription = null;
            CustomPrice = null;
            CalculateLineTotal();
        }

        // Clone method untuk duplicate line
        public InvoiceLine Clone()
        {
            return new InvoiceLine
            {
                InvoiceId = this.InvoiceId,
                Baris = this.Baris,
                LineOrder = this.LineOrder + 1,
                TkaId = this.TkaId,
                JobDescriptionId = this.JobDescriptionId,
                CustomJobName = this.CustomJobName,
                CustomJobDescription = this.CustomJobDescription,
                CustomPrice = this.CustomPrice,
                Quantity = this.Quantity,
                UnitPrice = this.UnitPrice,
                LineTotal = this.LineTotal
            };
        }

        // Validation
        public bool IsValid(out List<string> errors)
        {
            errors = new List<string>();

            if (InvoiceId <= 0)
                errors.Add("Invoice ID tidak valid");

            if (TkaId <= 0)
                errors.Add("TKA harus dipilih");

            if (JobDescriptionId <= 0)
                errors.Add("Job Description harus dipilih");

            if (Quantity <= 0)
                errors.Add("Quantity harus lebih dari 0");

            if (UnitPrice < 0)
                errors.Add("Unit Price tidak boleh negatif");

            if (Baris <= 0)
                errors.Add("Nomor baris harus lebih dari 0");

            if (LineOrder <= 0)
                errors.Add("Line order harus lebih dari 0");

            // Validate custom price if provided
            if (CustomPrice.HasValue && CustomPrice.Value < 0)
                errors.Add("Custom Price tidak boleh negatif");

            return errors.Count == 0;
        }

        // Helper method untuk sorting
        public static int Compare(InvoiceLine x, InvoiceLine y)
        {
            var barisComparison = x.Baris.CompareTo(y.Baris);
            if (barisComparison != 0) return barisComparison;
            
            return x.LineOrder.CompareTo(y.LineOrder);
        }

        // Format untuk export
        public Dictionary<string, object> ToExportDictionary()
        {
            return new Dictionary<string, object>
            {
                ["invoice_number"] = Invoice?.InvoiceNumber ?? "",
                ["baris"] = Baris,
                ["tka_name"] = TkaName,
                ["job_name"] = JobName,
                ["job_description"] = JobDescriptionText,
                ["price"] = UnitPrice,
                ["quantity"] = Quantity,
                ["line_total"] = LineTotal
            };
        }
    }
}