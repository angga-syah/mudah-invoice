using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace InvoiceApp.Models
{
    [Table("settings")]
    public class Setting
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        [Column("setting_key")]
        public string SettingKey { get; set; } = string.Empty;

        [Required]
        [Column("setting_value")]
        public string SettingValue { get; set; } = string.Empty;

        [StringLength(20)]
        [Column("setting_type")]
        public string SettingType { get; set; } = "string";

        [StringLength(200)]
        [Column("description")]
        public string? Description { get; set; }

        [Column("is_system")]
        public bool IsSystem { get; set; } = false;

        [Column("updated_by")]
        public int? UpdatedBy { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("UpdatedBy")]
        public virtual User? UpdatedByUser { get; set; }

        // Display properties
        [NotMapped]
        public string DisplayName => SettingKey.Replace("_", " ").ToTitleCase();

        [NotMapped]
        public string TypeDisplay => SettingType.ToTitleCase();

        [NotMapped]
        public bool IsReadOnly => IsSystem;

        // Static class untuk setting types
        public static class SettingTypes
        {
            public const string String = "string";
            public const string Integer = "integer";
            public const string Decimal = "decimal";
            public const string Boolean = "boolean";
            public const string Json = "json";
            public const string DateTime = "datetime";

            public static List<string> GetAll() => new() { String, Integer, Decimal, Boolean, Json, DateTime };
        }

        // Static class untuk predefined setting keys
        public static class Keys
        {
            // Company Information
            public const string CompanyName = "company_name";
            public const string CompanyTagline = "company_tagline";
            public const string CompanyAddress = "company_address";
            public const string CompanyPhone = "company_phone";
            public const string CompanyPhone2 = "company_phone2";
            
            // Invoice Settings
            public const string DefaultVatPercentage = "default_vat_percentage";
            public const string InvoiceNumberPrefix = "invoice_number_prefix";
            public const string InvoicePlace = "invoice_place";
            public const string DefaultBankId = "default_bank_id";
            
            // Print Settings
            public const string PrintMarginTop = "print_margin_top";
            public const string PrintMarginBottom = "print_margin_bottom";
            public const string PrintMarginLeft = "print_margin_left";
            public const string PrintMarginRight = "print_margin_right";
            public const string ShowBankOnLastPageOnly = "show_bank_last_page_only";
            
            // UI Settings
            public const string DefaultPageSize = "default_page_size";
            public const string AutoSaveInterval = "auto_save_interval";
            public const string SearchDelay = "search_delay";
            
            // System Settings
            public const string DatabaseVersion = "database_version";
            public const string LastBackup = "last_backup";
            public const string MaintenanceMode = "maintenance_mode";
        }

        // Methods
        public void UpdateTimestamp(int? userId = null)
        {
            UpdatedAt = DateTime.UtcNow;
            if (userId.HasValue)
                UpdatedBy = userId.Value;
        }

        // Type conversion methods
        public T GetValue<T>()
        {
            try
            {
                return SettingType switch
                {
                    SettingTypes.String => (T)(object)SettingValue,
                    SettingTypes.Integer => (T)(object)int.Parse(SettingValue),
                    SettingTypes.Decimal => (T)(object)decimal.Parse(SettingValue),
                    SettingTypes.Boolean => (T)(object)bool.Parse(SettingValue),
                    SettingTypes.DateTime => (T)(object)DateTime.Parse(SettingValue),
                    SettingTypes.Json => JsonSerializer.Deserialize<T>(SettingValue)!,
                    _ => (T)(object)SettingValue
                };
            }
            catch
            {
                return default(T)!;
            }
        }

        public void SetValue<T>(T value)
        {
            SettingValue = SettingType switch
            {
                SettingTypes.Json => JsonSerializer.Serialize(value),
                _ => value?.ToString() ?? ""
            };
            UpdateTimestamp();
        }

        // Validation
        public bool IsValid(out List<string> errors)
        {
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(SettingKey))
                errors.Add("Setting Key tidak boleh kosong");

            if (string.IsNullOrWhiteSpace(SettingValue))
                errors.Add("Setting Value tidak boleh kosong");

            if (!SettingTypes.GetAll().Contains(SettingType))
                errors.Add("Setting Type tidak valid");

            // Validate value based on type
            if (!ValidateValueForType())
                errors.Add($"Setting Value tidak sesuai dengan tipe {SettingType}");

            return errors.Count == 0;
        }

        private bool ValidateValueForType()
        {
            try
            {
                return SettingType switch
                {
                    SettingTypes.Integer => int.TryParse(SettingValue, out _),
                    SettingTypes.Decimal => decimal.TryParse(SettingValue, out _),
                    SettingTypes.Boolean => bool.TryParse(SettingValue, out _),
                    SettingTypes.DateTime => DateTime.TryParse(SettingValue, out _),
                    SettingTypes.Json => IsValidJson(),
                    _ => true // String always valid
                };
            }
            catch
            {
                return false;
            }
        }

        private bool IsValidJson()
        {
            try
            {
                JsonDocument.Parse(SettingValue);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Helper method untuk default settings
        public static List<Setting> GetDefaultSettings()
        {
            return new List<Setting>
            {
                new() { SettingKey = Keys.CompanyName, SettingValue = "PT. FORTUNA SADA NIOGA", SettingType = SettingTypes.String, Description = "Nama perusahaan untuk header invoice", IsSystem = false },
                new() { SettingKey = Keys.CompanyTagline, SettingValue = "Spirit of Services", SettingType = SettingTypes.String, Description = "Tagline perusahaan", IsSystem = false },
                new() { SettingKey = Keys.CompanyAddress, SettingValue = "Jakarta", SettingType = SettingTypes.String, Description = "Alamat kantor", IsSystem = false },
                new() { SettingKey = Keys.CompanyPhone, SettingValue = "", SettingType = SettingTypes.String, Description = "Nomor telepon utama", IsSystem = false },
                new() { SettingKey = Keys.CompanyPhone2, SettingValue = "", SettingType = SettingTypes.String, Description = "Nomor telepon kedua", IsSystem = false },
                new() { SettingKey = Keys.DefaultVatPercentage, SettingValue = "11.00", SettingType = SettingTypes.Decimal, Description = "Persentase PPN default", IsSystem = false },
                new() { SettingKey = Keys.InvoiceNumberPrefix, SettingValue = "FSN", SettingType = SettingTypes.String, Description = "Prefix nomor invoice", IsSystem = false },
                new() { SettingKey = Keys.InvoicePlace, SettingValue = "Jakarta", SettingType = SettingTypes.String, Description = "Tempat untuk tanggal invoice", IsSystem = false },
                new() { SettingKey = Keys.ShowBankOnLastPageOnly, SettingValue = "true", SettingType = SettingTypes.Boolean, Description = "Tampilkan info bank hanya di halaman terakhir", IsSystem = false },
                new() { SettingKey = Keys.DefaultPageSize, SettingValue = "50", SettingType = SettingTypes.Integer, Description = "Jumlah record per halaman default", IsSystem = false },
                new() { SettingKey = Keys.AutoSaveInterval, SettingValue = "30", SettingType = SettingTypes.Integer, Description = "Interval auto save dalam detik", IsSystem = false },
                new() { SettingKey = Keys.SearchDelay, SettingValue = "300", SettingType = SettingTypes.Integer, Description = "Delay pencarian dalam milliseconds", IsSystem = false },
                new() { SettingKey = Keys.DatabaseVersion, SettingValue = "1.0.0", SettingType = SettingTypes.String, Description = "Versi database", IsSystem = true },
                new() { SettingKey = Keys.MaintenanceMode, SettingValue = "false", SettingType = SettingTypes.Boolean, Description = "Mode maintenance", IsSystem = true }
            };
        }
    }

    // Extension method untuk string formatting
    public static class StringExtensions
    {
        public static string ToTitleCase(this string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            
            var words = input.Split('_', ' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    words[i] = char.ToUpper(words[i][0]) + words[i][1..].ToLower();
                }
            }
            return string.Join(" ", words);
        }
    }
}