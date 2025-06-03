using Microsoft.EntityFrameworkCore;
using Npgsql;
using InvoiceApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InvoiceApp.Database
{
    public class AppDbContext : DbContext
    {
        // DbSets untuk semua entity
        public DbSet<User> Users { get; set; } = null!;
        public DbSet<Company> Companies { get; set; } = null!;
        public DbSet<TkaWorker> TkaWorkers { get; set; } = null!;
        public DbSet<TkaFamily> TkaFamilyMembers { get; set; } = null!;
        public DbSet<JobDescription> JobDescriptions { get; set; } = null!;
        public DbSet<Invoice> Invoices { get; set; } = null!;
        public DbSet<InvoiceLine> InvoiceLines { get; set; } = null!;
        public DbSet<BankAccount> BankAccounts { get; set; } = null!;
        public DbSet<Setting> Settings { get; set; } = null!;

        // PERFORMANCE OPTIMIZATIONS - Connection string configuration
        public static string GetOptimizedConnectionString()
        {
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = "localhost",
                Database = "invoice_management",
                Username = "postgres", // Sesuaikan dengan setup database Anda
                Password = "fsn285712", // Sesuaikan dengan setup database Anda
                Port = 5432,
                
                // PERFORMANCE OPTIMIZATIONS
                Pooling = true,
                MinPoolSize = 5,
                MaxPoolSize = 20,
                ConnectionLifetime = 300,
                CommandTimeout = 30,
                Multiplexing = true,
                MaxAutoPrepare = 20
            };
            return builder.ToString();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .UseNpgsql(GetOptimizedConnectionString())
                .EnableServiceProviderCaching()
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking); // Default no tracking untuk performa
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure entity relationships dan constraints
            ConfigureUserEntity(modelBuilder);
            ConfigureCompanyEntity(modelBuilder);
            ConfigureTkaWorkerEntity(modelBuilder);
            ConfigureTkaFamilyEntity(modelBuilder);
            ConfigureJobDescriptionEntity(modelBuilder);
            ConfigureInvoiceEntity(modelBuilder);
            ConfigureInvoiceLineEntity(modelBuilder);
            ConfigureBankAccountEntity(modelBuilder);
            ConfigureSettingEntity(modelBuilder);

            // Configure indexes untuk performance
            ConfigureIndexes(modelBuilder);
        }

        private void ConfigureUserEntity(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(e => e.Username).IsUnique();
                entity.HasIndex(e => e.UserUuid).IsUnique();
                
                entity.Property(e => e.Role)
                    .HasConversion(
                        v => v,
                        v => v)
                    .HasDefaultValue(User.UserRole.Viewer);

                // Configure relationships
                entity.HasMany(e => e.CreatedInvoices)
                    .WithOne(e => e.CreatedByUser)
                    .HasForeignKey(e => e.CreatedBy)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasMany(e => e.UpdatedSettings)
                    .WithOne(e => e.UpdatedByUser)
                    .HasForeignKey(e => e.UpdatedBy)
                    .OnDelete(DeleteBehavior.SetNull);
            });
        }

        private void ConfigureCompanyEntity(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Company>(entity =>
            {
                entity.HasIndex(e => e.CompanyUuid).IsUnique();
                entity.HasIndex(e => e.Npwp);
                
                // Configure relationships
                entity.HasMany(e => e.JobDescriptions)
                    .WithOne(e => e.Company)
                    .HasForeignKey(e => e.CompanyId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.Invoices)
                    .WithOne(e => e.Company)
                    .HasForeignKey(e => e.CompanyId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }

        private void ConfigureTkaWorkerEntity(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TkaWorker>(entity =>
            {
                entity.HasIndex(e => e.TkaUuid).IsUnique();
                entity.HasIndex(e => e.Passport).IsUnique();
                
                // Configure relationships
                entity.HasMany(e => e.FamilyMembers)
                    .WithOne(e => e.TkaWorker)
                    .HasForeignKey(e => e.TkaId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.InvoiceLines)
                    .WithOne(e => e.TkaWorker)
                    .HasForeignKey(e => e.TkaId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }

        private void ConfigureTkaFamilyEntity(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TkaFamily>(entity =>
            {
                entity.HasIndex(e => e.FamilyUuid).IsUnique();
                entity.HasIndex(e => e.Passport);
            });
        }

        private void ConfigureJobDescriptionEntity(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<JobDescription>(entity =>
            {
                entity.HasIndex(e => e.JobUuid).IsUnique();
                entity.HasIndex(e => new { e.CompanyId, e.IsActive });
                
                entity.Property(e => e.Price)
                    .HasPrecision(15, 2);

                // Configure relationships
                entity.HasMany(e => e.InvoiceLines)
                    .WithOne(e => e.JobDescription)
                    .HasForeignKey(e => e.JobDescriptionId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }

        private void ConfigureInvoiceEntity(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(entity =>
            {
                entity.HasIndex(e => e.InvoiceUuid).IsUnique();
                entity.HasIndex(e => e.InvoiceNumber).IsUnique();
                entity.HasIndex(e => new { e.CompanyId, e.InvoiceDate });
                
                entity.Property(e => e.Subtotal).HasPrecision(15, 2);
                entity.Property(e => e.VatPercentage).HasPrecision(5, 2);
                entity.Property(e => e.VatAmount).HasPrecision(15, 2);
                entity.Property(e => e.TotalAmount).HasPrecision(15, 2);

                // Configure relationships
                entity.HasMany(e => e.InvoiceLines)
                    .WithOne(e => e.Invoice)
                    .HasForeignKey(e => e.InvoiceId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.BankAccount)
                    .WithMany(e => e.Invoices)
                    .HasForeignKey(e => e.BankAccountId)
                    .OnDelete(DeleteBehavior.SetNull);
            });
        }

        private void ConfigureInvoiceLineEntity(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<InvoiceLine>(entity =>
            {
                entity.HasIndex(e => e.LineUuid).IsUnique();
                entity.HasIndex(e => new { e.InvoiceId, e.Baris, e.LineOrder });
                
                entity.Property(e => e.UnitPrice).HasPrecision(15, 2);
                entity.Property(e => e.LineTotal).HasPrecision(15, 2);
                entity.Property(e => e.CustomPrice).HasPrecision(15, 2);
            });
        }

        private void ConfigureBankAccountEntity(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BankAccount>(entity =>
            {
                entity.HasIndex(e => e.BankUuid).IsUnique();
                entity.HasIndex(e => new { e.IsDefault, e.IsActive });
            });
        }

        private void ConfigureSettingEntity(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Setting>(entity =>
            {
                entity.HasIndex(e => e.SettingKey).IsUnique();
            });
        }

        private void ConfigureIndexes(ModelBuilder modelBuilder)
        {
            // Indexes sudah dikonfigurasi di atas, tetapi bisa ditambahkan lebih detail disini
            // Index untuk performance akan dibuat melalui migration atau SQL script terpisah
        }

        // SIMPLE CACHE IMPLEMENTATION untuk performance
        private static readonly Dictionary<string, (object Data, DateTime Expiry)> _simpleCache = new();
        
        public T? GetFromCache<T>(string key) where T : class
        {
            if (_simpleCache.TryGetValue(key, out var cached) && DateTime.UtcNow < cached.Expiry)
            {
                return cached.Data as T;
            }
            _simpleCache.Remove(key);
            return null;
        }
        
        public void SetCache<T>(string key, T data, int minutes = 5)
        {
            _simpleCache[key] = (data!, DateTime.UtcNow.AddMinutes(minutes));
        }

        public void ClearCache(string pattern = "")
        {
            if (string.IsNullOrEmpty(pattern))
            {
                _simpleCache.Clear();
            }
            else
            {
                var keysToRemove = _simpleCache.Keys.Where(k => k.Contains(pattern)).ToList();
                foreach (var key in keysToRemove)
                {
                    _simpleCache.Remove(key);
                }
            }
        }

        // Helper methods untuk common operations
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                return await Database.CanConnectAsync();
            }
            catch
            {
                return false;
            }
        }

        public async Task InitializeDatabaseAsync()
        {
            try
            {
                // Ensure database is created
                await Database.EnsureCreatedAsync();

                // Seed default data jika belum ada
                await SeedDefaultDataAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize database: {ex.Message}", ex);
            }
        }

        private async Task SeedDefaultDataAsync()
        {
            // Seed default settings jika belum ada
            if (!await Settings.AnyAsync())
            {
                var defaultSettings = Setting.GetDefaultSettings();
                await Settings.AddRangeAsync(defaultSettings);
                await SaveChangesAsync();
            }

            // Seed default admin user jika belum ada
            if (!await Users.AnyAsync())
            {
                var adminUser = new User
                {
                    Username = "admin",
                    FullName = "Administrator",
                    Role = User.UserRole.Admin,
                    IsActive = true
                };
                adminUser.SetPassword("admin123"); // Default password
                
                await Users.AddAsync(adminUser);
                await SaveChangesAsync();
            }
        }

        // Cleanup method untuk maintenance
        public async Task CleanupOldDataAsync(int daysToKeep = 365)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
            
            // Cleanup old draft invoices
            var oldDrafts = await Invoices
                .Where(i => i.Status == Invoice.InvoiceStatus.Draft && i.CreatedAt < cutoffDate)
                .ToListAsync();
                
            if (oldDrafts.Any())
            {
                Invoices.RemoveRange(oldDrafts);
                await SaveChangesAsync();
            }
        }

        // Override SaveChanges untuk automatic timestamp updates
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            UpdateTimestamps();
            return await base.SaveChangesAsync(cancellationToken);
        }

        public override int SaveChanges()
        {
            UpdateTimestamps();
            return base.SaveChanges();
        }

        private void UpdateTimestamps()
        {
            var entries = ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Modified)
                .ToList();

            foreach (var entry in entries)
            {
                if (entry.Entity is Company company)
                    company.UpdateTimestamp();
                else if (entry.Entity is TkaWorker tka)
                    tka.UpdateTimestamp();
                else if (entry.Entity is JobDescription job)
                    job.UpdateTimestamp();
                else if (entry.Entity is Invoice invoice)
                    invoice.UpdateTimestamp();
                else if (entry.Entity is BankAccount bank)
                    bank.UpdateTimestamp();
                else if (entry.Entity is User user)
                    user.UpdateTimestamp();
                else if (entry.Entity is Setting setting)
                    setting.UpdateTimestamp();
            }
        }
    }
}