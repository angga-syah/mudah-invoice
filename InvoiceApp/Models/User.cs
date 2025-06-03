using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using System.Text;

namespace InvoiceApp.Models
{
    [Table("users")]
    public class User
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("user_uuid")]
        public Guid UserUuid { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(50)]
        [Column("username")]
        public string Username { get; set; } = string.Empty;

        [Required]
        [StringLength(255)]
        [Column("password_hash")]
        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        [Column("role")]
        public string Role { get; set; } = UserRole.Viewer;

        [Required]
        [StringLength(100)]
        [Column("full_name")]
        public string FullName { get; set; } = string.Empty;

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("last_login")]
        public DateTime? LastLogin { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual ICollection<Invoice> CreatedInvoices { get; set; } = new List<Invoice>();
        public virtual ICollection<Setting> UpdatedSettings { get; set; } = new List<Setting>();

        // Display properties
        [NotMapped]
        public string DisplayName => FullName;

        [NotMapped]
        public string RoleDisplay => GetRoleDisplay();

        [NotMapped]
        public string StatusDisplay => IsActive ? "Active" : "Inactive";

        [NotMapped]
        public bool IsAdmin => Role == UserRole.Admin;

        [NotMapped]
        public bool IsViewer => Role == UserRole.Viewer;

        [NotMapped]
        public bool CanExport => IsAdmin || IsViewer; // Both roles can export

        [NotMapped]
        public bool CanEdit => IsAdmin; // Only admin can edit

        [NotMapped]
        public string LastLoginDisplay => LastLogin?.ToString("dd/MM/yyyy HH:mm") ?? "Never";

        // Static class untuk role constants
        public static class UserRole
        {
            public const string Admin = "admin";
            public const string Viewer = "viewer";

            public static List<string> GetAll() => new() { Admin, Viewer };

            public static Dictionary<string, string> GetDisplayNames() => new()
            {
                { Admin, "Administrator" },
                { Viewer, "Viewer" }
            };
        }

        // Methods
        public void UpdateTimestamp()
        {
            UpdatedAt = DateTime.UtcNow;
        }

        public string GetRoleDisplay()
        {
            var displayNames = UserRole.GetDisplayNames();
            return displayNames.TryGetValue(Role, out var display) ? display : Role;
        }

        public void UpdateLastLogin()
        {
            LastLogin = DateTime.UtcNow;
            UpdateTimestamp();
        }

        // Password management
        public void SetPassword(string password)
        {
            PasswordHash = HashPassword(password);
            UpdateTimestamp();
        }

        public bool VerifyPassword(string password)
        {
            return VerifyHashedPassword(PasswordHash, password);
        }

        // Static password hashing methods
        public static string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var salt = "InvoiceApp2024"; // Simple salt for this application
            var saltedPassword = password + salt;
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(saltedPassword));
            return Convert.ToBase64String(hashedBytes);
        }

        public static bool VerifyHashedPassword(string hashedPassword, string providedPassword)
        {
            var newHash = HashPassword(providedPassword);
            return string.Equals(hashedPassword, newHash, StringComparison.Ordinal);
        }

        // Validation
        public bool IsValid(out List<string> errors)
        {
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(Username))
                errors.Add("Username tidak boleh kosong");
            else if (Username.Length < 3)
                errors.Add("Username minimal 3 karakter");

            if (string.IsNullOrWhiteSpace(FullName))
                errors.Add("Nama Lengkap tidak boleh kosong");

            if (string.IsNullOrWhiteSpace(PasswordHash))
                errors.Add("Password tidak boleh kosong");

            if (!UserRole.GetAll().Contains(Role))
                errors.Add("Role tidak valid");

            return errors.Count == 0;
        }

        // Permission methods
        public bool HasPermission(string permission)
        {
            return permission.ToLower() switch
            {
                "read" => IsActive,
                "create" => IsActive && IsAdmin,
                "edit" => IsActive && IsAdmin,
                "delete" => IsActive && IsAdmin,
                "export" => IsActive,
                "import" => IsActive && IsAdmin,
                "settings" => IsActive && IsAdmin,
                _ => false
            };
        }

        public List<string> GetAllPermissions()
        {
            var permissions = new List<string> { "read" };
            
            if (IsViewer || IsAdmin)
                permissions.Add("export");
                
            if (IsAdmin)
            {
                permissions.AddRange(new[] { "create", "edit", "delete", "import", "settings" });
            }
            
            return permissions;
        }

        // Clone method (untuk admin purposes)
        public User Clone()
        {
            return new User
            {
                Username = this.Username + "_copy",
                FullName = this.FullName + " (Copy)",
                Role = this.Role,
                IsActive = false // New user starts inactive
            };
        }
    }
}