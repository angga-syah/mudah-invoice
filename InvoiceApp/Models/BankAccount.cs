using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InvoiceApp.Models
{
    [Table("bank_accounts")]
    public class BankAccount
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("bank_uuid")]
        public Guid BankUuid { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(100)]
        [Column("bank_name")]
        public string BankName { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        [Column("account_number")]
        public string AccountNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Column("account_name")]
        public string AccountName { get; set; } = string.Empty;

        [Column("is_default")]
        public bool IsDefault { get; set; } = false;

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("sort_order")]
        public int SortOrder { get; set; } = 0;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

        // Display properties
        [NotMapped]
        public string DisplayName => $"{BankName} - {AccountNumber}";

        [NotMapped]
        public string FullInfo => $"{BankName}\nNo. Rekening: {AccountNumber}\nA/n: {AccountName}";

        [NotMapped]
        public string ShortInfo => $"{BankName} ({AccountNumber})";

        // Methods
        public void UpdateTimestamp()
        {
            UpdatedAt = DateTime.UtcNow;
        }

        public void SetAsDefault()
        {
            IsDefault = true;
            UpdateTimestamp();
        }

        // Validation
        public bool IsValid(out List<string> errors)
        {
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(BankName))
                errors.Add("Nama Bank tidak boleh kosong");

            if (string.IsNullOrWhiteSpace(AccountNumber))
                errors.Add("Nomor Rekening tidak boleh kosong");

            if (string.IsNullOrWhiteSpace(AccountName))
                errors.Add("Nama Pemilik Rekening tidak boleh kosong");

            // Validate account number format (basic validation)
            if (!string.IsNullOrWhiteSpace(AccountNumber) && AccountNumber.Length < 8)
                errors.Add("Nomor Rekening minimal 8 digit");

            return errors.Count == 0;
        }

        // Helper method untuk format display di invoice
        public string FormatForInvoice()
        {
            return $"{BankName}\nNo. Rekening: {AccountNumber}\nA/n: {AccountName}";
        }

        // Clone method
        public BankAccount Clone()
        {
            return new BankAccount
            {
                BankName = this.BankName,
                AccountNumber = this.AccountNumber + "_copy",
                AccountName = this.AccountName,
                IsDefault = false,
                IsActive = this.IsActive,
                SortOrder = this.SortOrder + 1
            };
        }
    }
}