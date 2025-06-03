using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace InvoiceApp.Models
{
    [Table("invoices")]
    public class Invoice
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("invoice_uuid")]
        public Guid InvoiceUuid { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(50)]
        [Column("invoice_number")]
        public string InvoiceNumber { get; set; } = string.Empty;

        [Required]
        [Column("company_id")]
        public int CompanyId { get; set; }

        [Required]
        [Column("invoice_date")]
        public DateTime InvoiceDate { get; set; } = DateTime.Today;

        [Column("subtotal", TypeName = "decimal(15,2)")]
        public decimal Subtotal { get; set; } = 0;

        [Column("vat_percentage", TypeName = "decimal(5,2)")]
        public decimal VatPercentage { get; set; } = 11.00m;

        [Column("vat_amount", TypeName = "decimal(15,2)")]
        public decimal VatAmount { get; set; } = 0;

        [Column("total_amount", TypeName = "decimal(15,2)")]
        public decimal TotalAmount { get; set; } = 0;

        [Required]
        [StringLength(20)]
        [Column("status")]
        public string Status { get; set; } = InvoiceStatus.Draft;

        [Column("notes")]
        public string? Notes { get; set; }

        [Column("bank_account_id")]
        public int? BankAccountId { get; set; }

        [Column("printed_count")]
        public int PrintedCount { get; set; } = 0;

        [Column("last_printed_at")]
        public DateTime? LastPrintedAt { get; set; }

        [Column("imported_from")]
        public string? ImportedFrom { get; set; }

        [Column("import_batch_id")]
        public string? ImportBatchId { get; set; }

        [Required]
        [Column("created_by")]
        public int CreatedBy { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("CompanyId")]
        public virtual Company Company { get; set; } = null!;

        [ForeignKey("CreatedBy")]
        public virtual User CreatedByUser { get; set; } = null!;

        [ForeignKey("BankAccountId")]
        public virtual BankAccount? BankAccount { get; set; }

        public virtual ICollection<InvoiceLine> InvoiceLines { get; set; } = new List<InvoiceLine>();

        // Display properties
        [NotMapped]
        public string DisplayName => $"{InvoiceNumber} - {Company?.CompanyName}";

        [NotMapped]
        public string StatusDisplay => GetStatusDisplay();

        [NotMapped]
        public string FormattedTotal => $"Rp {TotalAmount:N0}";

        [NotMapped]
        public string FormattedSubtotal => $"Rp {Subtotal:N0}";

        [NotMapped]
        public string FormattedVat => $"Rp {VatAmount:N0}";

        [NotMapped]
        public bool CanEdit => Status == InvoiceStatus.Draft;

        [NotMapped]
        public bool CanFinalize => Status == InvoiceStatus.Draft && InvoiceLines.Any();

        [NotMapped]
        public bool CanPrint => Status == InvoiceStatus.Finalized;

        // Static class untuk status constants
        public static class InvoiceStatus
        {
            public const string Draft = "draft";
            public const string Finalized = "finalized";
            public const string Paid = "paid";
            public const string Cancelled = "cancelled";

            public static List<string> GetAll() => new() { Draft, Finalized, Paid, Cancelled };

            public static Dictionary<string, string> GetDisplayNames() => new()
            {
                { Draft, "Draft" },
                { Finalized, "Finalized" },
                { Paid, "Paid" },
                { Cancelled, "Cancelled" }
            };
        }

        // Methods
        public void UpdateTimestamp()
        {
            UpdatedAt = DateTime.UtcNow;
        }

        public string GetStatusDisplay()
        {
            var displayNames = InvoiceStatus.GetDisplayNames();
            return displayNames.TryGetValue(Status, out var display) ? display : Status;
        }

        public void CalculateTotals()
        {
            Subtotal = InvoiceLines.Sum(line => line.LineTotal);
            
            // Aturan pembulatan: 18.000,49 → 18.000 | 18.000,50 → 18.001
            var rounded = Math.Round(Subtotal);
            var difference = Subtotal - rounded;
            
            if (difference >= 0.50m)
                Subtotal = rounded + 1;
            else
                Subtotal = rounded;

            VatAmount = Math.Round(Subtotal * VatPercentage / 100, 0);
            TotalAmount = Subtotal + VatAmount;
            
            UpdateTimestamp();
        }

        public string GenerateInvoiceNumber()
        {
            var year = InvoiceDate.ToString("yy");
            var month = InvoiceDate.ToString("MM");
            // Format: FSN/YY/MM/NNN - angka akan di-generate di service layer
            return $"FSN/{year}/{month}/001";
        }

        public void Finalize()
        {
            if (Status == InvoiceStatus.Draft)
            {
                Status = InvoiceStatus.Finalized;
                UpdateTimestamp();
            }
        }

        public void MarkAsPaid()
        {
            if (Status == InvoiceStatus.Finalized)
            {
                Status = InvoiceStatus.Paid;
                UpdateTimestamp();
            }
        }

        public void Cancel()
        {
            if (Status != InvoiceStatus.Paid)
            {
                Status = InvoiceStatus.Cancelled;
                UpdateTimestamp();
            }
        }

        public void IncrementPrintCount()
        {
            PrintedCount++;
            LastPrintedAt = DateTime.UtcNow;
            UpdateTimestamp();
        }

        // Validation
        public bool IsValid(out List<string> errors)
        {
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(InvoiceNumber))
                errors.Add("Invoice Number tidak boleh kosong");

            if (CompanyId <= 0)
                errors.Add("Company harus dipilih");

            if (InvoiceDate == default)
                errors.Add("Invoice Date harus diisi");

            if (VatPercentage < 0 || VatPercentage > 100)
                errors.Add("VAT Percentage harus antara 0-100%");

            return errors.Count == 0;
        }
    }
}