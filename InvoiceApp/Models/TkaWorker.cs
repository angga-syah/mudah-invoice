using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InvoiceApp.Models
{
    [Table("tka_workers")]
    public class TkaWorker
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("tka_uuid")]
        public Guid TkaUuid { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(100)]
        [Column("nama")]
        public string Nama { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        [Column("passport")]
        public string Passport { get; set; } = string.Empty;

        [StringLength(100)]
        [Column("divisi")]
        public string? Divisi { get; set; }

        [Required]
        [StringLength(20)]
        [Column("jenis_kelamin")]
        public string JenisKelamin { get; set; } = "Laki-laki";

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual ICollection<TkaFamily> FamilyMembers { get; set; } = new List<TkaFamily>();
        public virtual ICollection<InvoiceLine> InvoiceLines { get; set; } = new List<InvoiceLine>();

        // Display properties for UI
        [NotMapped]
        public string DisplayName => $"{Nama} ({Passport})";

        [NotMapped]
        public string FullInfo => $"{Nama} - {Passport}" + (string.IsNullOrEmpty(Divisi) ? "" : $" - {Divisi}");

        // Enum untuk jenis kelamin
        public static class GenderTypes
        {
            public const string Male = "Laki-laki";
            public const string Female = "Perempuan";
            
            public static List<string> GetAll() => new() { Male, Female };
        }

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
            return Nama.ToLower().Contains(searchTerm) ||
                   Passport.ToLower().Contains(searchTerm) ||
                   (Divisi?.ToLower().Contains(searchTerm) ?? false);
        }
    }
}