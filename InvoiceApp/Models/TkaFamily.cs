using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InvoiceApp.Models
{
    [Table("tka_family_members")]
    public class TkaFamily
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("family_uuid")]
        public Guid FamilyUuid { get; set; } = Guid.NewGuid();

        [Required]
        [Column("tka_id")]
        public int TkaId { get; set; }

        [Required]
        [StringLength(100)]
        [Column("nama")]
        public string Nama { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        [Column("passport")]
        public string Passport { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        [Column("jenis_kelamin")]
        public string JenisKelamin { get; set; } = "Laki-laki";

        [Required]
        [StringLength(20)]
        [Column("relationship")]
        public string Relationship { get; set; } = "spouse";

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("TkaId")]
        public virtual TkaWorker TkaWorker { get; set; } = null!;

        // Display properties
        [NotMapped]
        public string DisplayName => $"{Nama} ({Passport})";

        [NotMapped]
        public string FullInfo => $"{Nama} - {Passport} - {GetRelationshipDisplay()}";

        // Enum untuk relationship types
        public static class RelationshipTypes
        {
            public const string Spouse = "spouse";
            public const string Parent = "parent";
            public const string Child = "child";
            
            public static List<string> GetAll() => new() { Spouse, Parent, Child };
            
            public static Dictionary<string, string> GetDisplayNames() => new()
            {
                { Spouse, "Suami/Istri" },
                { Parent, "Orang Tua" },
                { Child, "Anak" }
            };
        }

        // Helper methods
        public string GetRelationshipDisplay()
        {
            var displayNames = RelationshipTypes.GetDisplayNames();
            return displayNames.TryGetValue(Relationship, out var display) ? display : Relationship;
        }

        public bool MatchesSearch(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm)) return true;
            
            searchTerm = searchTerm.ToLower();
            return Nama.ToLower().Contains(searchTerm) ||
                   Passport.ToLower().Contains(searchTerm) ||
                   GetRelationshipDisplay().ToLower().Contains(searchTerm);
        }
    }
}