using Microsoft.EntityFrameworkCore;
using InvoiceApp.Database;
using InvoiceApp.Models;
using InvoiceApp.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InvoiceApp.Services
{
    /// <summary>
    /// Service untuk mengelola operasi TKA Workers dan Family Members
    /// </summary>
    public class TkaService
    {
        private readonly AppDbContext _context;

        public TkaService(AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #region TKA Worker CRUD Operations

        /// <summary>
        /// Get all TKA workers dengan pagination dan filtering
        /// </summary>
        /// <param name="pageNumber">Nomor halaman (1-based)</param>
        /// <param name="pageSize">Ukuran halaman</param>
        /// <param name="searchTerm">Term pencarian (optional)</param>
        /// <param name="includeInactive">Include inactive TKA</param>
        /// <param name="companyFilter">Filter berdasarkan company (optional)</param>
        /// <returns>TKA workers dengan total count</returns>
        public async Task<(List<TkaWorker> TkaWorkers, int TotalCount)> GetTkaWorkersAsync(
            int pageNumber = 1, int pageSize = 50, string? searchTerm = null, bool includeInactive = false, int? companyFilter = null)
        {
            var cacheKey = $"tka_workers_page_{pageNumber}_{pageSize}_{searchTerm ?? "all"}_{includeInactive}_{companyFilter}";
            var cached = _context.GetFromCache<(List<TkaWorker>, int)>(cacheKey);
            if (cached.TkaWorkers != null) return cached;

            var query = _context.TkaWorkers.AsNoTracking();

            // Filter active/inactive
            if (!includeInactive)
                query = query.Where(t => t.IsActive);

            // Filter by company (melalui invoice history)
            if (companyFilter.HasValue)
            {
                var tkaIdsFromCompany = await _context.InvoiceLines
                    .Where(il => il.Invoice.CompanyId == companyFilter.Value)
                    .Select(il => il.TkaId)
                    .Distinct()
                    .ToListAsync();

                query = query.Where(t => tkaIdsFromCompany.Contains(t.Id));
            }

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                searchTerm = searchTerm.Trim().ToLower();
                query = query.Where(t => 
                    t.Nama.ToLower().Contains(searchTerm) ||
                    t.Passport.ToLower().Contains(searchTerm) ||
                    (t.Divisi != null && t.Divisi.ToLower().Contains(searchTerm)));
            }

            var totalCount = await query.CountAsync();
            var tkaWorkers = await query
                .OrderBy(t => t.Nama)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var result = (tkaWorkers, totalCount);
            _context.SetCache(cacheKey, result, 5);
            return result;
        }

        /// <summary>
        /// Get TKA worker by ID dengan caching
        /// </summary>
        /// <param name="id">TKA worker ID</param>
        /// <param name="includeFamilyMembers">Include family members</param>
        /// <returns>TKA worker atau null jika tidak ditemukan</returns>
        public async Task<TkaWorker?> GetTkaWorkerByIdAsync(int id, bool includeFamilyMembers = false)
        {
            var cacheKey = $"tka_worker_{id}_{includeFamilyMembers}";
            var cached = _context.GetFromCache<TkaWorker>(cacheKey);
            if (cached != null) return cached;

            var query = _context.TkaWorkers.AsNoTracking();

            if (includeFamilyMembers)
                query = query.Include(t => t.FamilyMembers.Where(f => f.IsActive));

            var tkaWorker = await query.FirstOrDefaultAsync(t => t.Id == id);
            
            if (tkaWorker != null)
                _context.SetCache(cacheKey, tkaWorker, 10);

            return tkaWorker;
        }

        /// <summary>
        /// Get TKA worker by passport number
        /// </summary>
        /// <param name="passport">Passport number</param>
        /// <returns>TKA worker atau null</returns>
        public async Task<TkaWorker?> GetTkaWorkerByPassportAsync(string passport)
        {
            if (string.IsNullOrWhiteSpace(passport))
                return null;

            return await _context.TkaWorkers
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Passport == passport.Trim());
        }

        /// <summary>
        /// Create new TKA worker
        /// </summary>
        /// <param name="tkaWorker">TKA worker data</param>
        /// <returns>Created TKA worker dengan ID</returns>
        public async Task<TkaWorker> CreateTkaWorkerAsync(TkaWorker tkaWorker)
        {
            // Validate TKA worker data
            var errors = ValidationHelper.ValidateTkaWorker(
                tkaWorker.Nama, tkaWorker.Passport, tkaWorker.Divisi);

            if (errors.Any())
                throw new ArgumentException($"Validation failed: {string.Join(", ", errors)}");

            // Check for duplicate passport
            var existingPassport = await _context.TkaWorkers
                .AnyAsync(t => t.Passport == tkaWorker.Passport && t.Id != tkaWorker.Id);
            if (existingPassport)
                throw new InvalidOperationException($"Passport {tkaWorker.Passport} sudah digunakan oleh TKA lain");

            // Also check in family members
            var passportInFamily = await _context.TkaFamilyMembers
                .AnyAsync(f => f.Passport == tkaWorker.Passport);
            if (passportInFamily)
                throw new InvalidOperationException($"Passport {tkaWorker.Passport} sudah digunakan oleh family member");

            // Clean and format data
            tkaWorker.Nama = ValidationHelper.ToProperCase(tkaWorker.Nama);
            tkaWorker.Passport = tkaWorker.Passport.ToUpper().Trim();
            tkaWorker.Divisi = ValidationHelper.ToProperCase(tkaWorker.Divisi);

            // Set creation time
            tkaWorker.CreatedAt = DateTime.UtcNow;
            tkaWorker.UpdatedAt = DateTime.UtcNow;

            _context.TkaWorkers.Add(tkaWorker);
            await _context.SaveChangesAsync();

            // Clear related cache
            ClearTkaCache();

            return tkaWorker;
        }

        /// <summary>
        /// Update existing TKA worker
        /// </summary>
        /// <param name="tkaWorker">Updated TKA worker data</param>
        /// <returns>Updated TKA worker</returns>
        public async Task<TkaWorker> UpdateTkaWorkerAsync(TkaWorker tkaWorker)
        {
            // Validate TKA worker data
            var errors = ValidationHelper.ValidateTkaWorker(
                tkaWorker.Nama, tkaWorker.Passport, tkaWorker.Divisi);

            if (errors.Any())
                throw new ArgumentException($"Validation failed: {string.Join(", ", errors)}");

            // Check if TKA worker exists
            var existingTka = await _context.TkaWorkers.FindAsync(tkaWorker.Id);
            if (existingTka == null)
                throw new InvalidOperationException($"TKA Worker dengan ID {tkaWorker.Id} tidak ditemukan");

            // Check for duplicate passport
            var duplicatePassport = await _context.TkaWorkers
                .AnyAsync(t => t.Passport == tkaWorker.Passport && t.Id != tkaWorker.Id);
            if (duplicatePassport)
                throw new InvalidOperationException($"Passport {tkaWorker.Passport} sudah digunakan oleh TKA lain");

            // Check in family members (excluding family of this TKA)
            var passportInFamily = await _context.TkaFamilyMembers
                .AnyAsync(f => f.Passport == tkaWorker.Passport && f.TkaId != tkaWorker.Id);
            if (passportInFamily)
                throw new InvalidOperationException($"Passport {tkaWorker.Passport} sudah digunakan oleh family member");

            // Clean and format data
            tkaWorker.Nama = ValidationHelper.ToProperCase(tkaWorker.Nama);
            tkaWorker.Passport = tkaWorker.Passport.ToUpper().Trim();
            tkaWorker.Divisi = ValidationHelper.ToProperCase(tkaWorker.Divisi);

            // Update fields
            existingTka.Nama = tkaWorker.Nama;
            existingTka.Passport = tkaWorker.Passport;
            existingTka.Divisi = tkaWorker.Divisi;
            existingTka.JenisKelamin = tkaWorker.JenisKelamin;
            existingTka.IsActive = tkaWorker.IsActive;
            existingTka.UpdateTimestamp();

            await _context.SaveChangesAsync();

            // Clear related cache
            ClearTkaCache();

            return existingTka;
        }

        /// <summary>
        /// Soft delete TKA worker (set IsActive = false)
        /// </summary>
        /// <param name="id">TKA worker ID</param>
        /// <returns>True jika berhasil</returns>
        public async Task<bool> DeleteTkaWorkerAsync(int id)
        {
            var tkaWorker = await _context.TkaWorkers.FindAsync(id);
            if (tkaWorker == null)
                return false;

            // Check if TKA is used in invoice lines
            var usedInInvoices = await _context.InvoiceLines.AnyAsync(il => il.TkaId == id);
            if (usedInInvoices)
                throw new InvalidOperationException("Tidak dapat menghapus TKA yang sudah digunakan dalam invoice");

            // Soft delete TKA and family members
            tkaWorker.IsActive = false;
            tkaWorker.UpdateTimestamp();

            var familyMembers = await _context.TkaFamilyMembers
                .Where(f => f.TkaId == id)
                .ToListAsync();

            foreach (var family in familyMembers)
            {
                family.IsActive = false;
            }

            await _context.SaveChangesAsync();

            // Clear related cache
            ClearTkaCache();

            return true;
        }

        /// <summary>
        /// Restore deleted TKA worker
        /// </summary>
        /// <param name="id">TKA worker ID</param>
        /// <returns>True jika berhasil</returns>
        public async Task<bool> RestoreTkaWorkerAsync(int id)
        {
            var tkaWorker = await _context.TkaWorkers.FindAsync(id);
            if (tkaWorker == null)
                return false;

            tkaWorker.IsActive = true;
            tkaWorker.UpdateTimestamp();

            await _context.SaveChangesAsync();

            // Clear related cache
            ClearTkaCache();

            return true;
        }

        #endregion

        #region Family Members Operations

        /// <summary>
        /// Get family members by TKA worker ID
        /// </summary>
        /// <param name="tkaId">TKA worker ID</param>
        /// <param name="includeInactive">Include inactive family members</param>
        /// <returns>List of family members</returns>
        public async Task<List<TkaFamily>> GetFamilyMembersByTkaIdAsync(int tkaId, bool includeInactive = false)
        {
            var cacheKey = $"tka_family_{tkaId}_{includeInactive}";
            var cached = _context.GetFromCache<List<TkaFamily>>(cacheKey);
            if (cached != null) return cached;

            var query = _context.TkaFamilyMembers.AsNoTracking()
                .Include(f => f.TkaWorker)
                .Where(f => f.TkaId == tkaId);

            if (!includeInactive)
                query = query.Where(f => f.IsActive);

            var familyMembers = await query
                .OrderBy(f => f.Relationship)
                .ThenBy(f => f.Nama)
                .ToListAsync();

            _context.SetCache(cacheKey, familyMembers, 10);
            return familyMembers;
        }

        /// <summary>
        /// Get family member by ID
        /// </summary>
        /// <param name="id">Family member ID</param>
        /// <param name="includeTkaWorker">Include TKA worker data</param>
        /// <returns>Family member atau null</returns>
        public async Task<TkaFamily?> GetFamilyMemberByIdAsync(int id, bool includeTkaWorker = false)
        {
            var query = _context.TkaFamilyMembers.AsNoTracking();

            if (includeTkaWorker)
                query = query.Include(f => f.TkaWorker);

            return await query.FirstOrDefaultAsync(f => f.Id == id);
        }

        /// <summary>
        /// Get family member by passport
        /// </summary>
        /// <param name="passport">Passport number</param>
        /// <returns>Family member atau null</returns>
        public async Task<TkaFamily?> GetFamilyMemberByPassportAsync(string passport)
        {
            if (string.IsNullOrWhiteSpace(passport))
                return null;

            return await _context.TkaFamilyMembers
                .AsNoTracking()
                .Include(f => f.TkaWorker)
                .FirstOrDefaultAsync(f => f.Passport == passport.Trim());
        }

        /// <summary>
        /// Create new family member
        /// </summary>
        /// <param name="familyMember">Family member data</param>
        /// <returns>Created family member</returns>
        public async Task<TkaFamily> CreateFamilyMemberAsync(TkaFamily familyMember)
        {
            // Validate basic data
            var errors = new List<string>();
            
            var namaError = ValidationHelper.ValidateRequired(familyMember.Nama, "Nama");
            if (namaError != null) errors.Add(namaError);

            var passportError = ValidationHelper.ValidateRequired(familyMember.Passport, "Passport");
            if (passportError != null) errors.Add(passportError);

            var passportFormatError = ValidationHelper.ValidatePassport(familyMember.Passport);
            if (passportFormatError != null) errors.Add(passportFormatError);

            if (errors.Any())
                throw new ArgumentException($"Validation failed: {string.Join(", ", errors)}");

            // Verify TKA worker exists
            var tkaExists = await _context.TkaWorkers
                .AnyAsync(t => t.Id == familyMember.TkaId && t.IsActive);
            if (!tkaExists)
                throw new InvalidOperationException("TKA Worker tidak ditemukan atau tidak aktif");

            // Check for duplicate passport in TKA workers
            var passportInTka = await _context.TkaWorkers
                .AnyAsync(t => t.Passport == familyMember.Passport);
            if (passportInTka)
                throw new InvalidOperationException($"Passport {familyMember.Passport} sudah digunakan oleh TKA worker");

            // Check for duplicate passport in family members
            var duplicatePassport = await _context.TkaFamilyMembers
                .AnyAsync(f => f.Passport == familyMember.Passport && f.Id != familyMember.Id);
            if (duplicatePassport)
                throw new InvalidOperationException($"Passport {familyMember.Passport} sudah digunakan oleh family member lain");

            // Clean and format data
            familyMember.Nama = ValidationHelper.ToProperCase(familyMember.Nama);
            familyMember.Passport = familyMember.Passport.ToUpper().Trim();

            // Set creation time
            familyMember.CreatedAt = DateTime.UtcNow;

            _context.TkaFamilyMembers.Add(familyMember);
            await _context.SaveChangesAsync();

            // Clear related cache
            ClearFamilyCache(familyMember.TkaId);

            return familyMember;
        }

        /// <summary>
        /// Update family member
        /// </summary>
        /// <param name="familyMember">Updated family member data</param>
        /// <returns>Updated family member</returns>
        public async Task<TkaFamily> UpdateFamilyMemberAsync(TkaFamily familyMember)
        {
            // Validate basic data
            var errors = new List<string>();
            
            var namaError = ValidationHelper.ValidateRequired(familyMember.Nama, "Nama");
            if (namaError != null) errors.Add(namaError);

            var passportError = ValidationHelper.ValidateRequired(familyMember.Passport, "Passport");
            if (passportError != null) errors.Add(passportError);

            var passportFormatError = ValidationHelper.ValidatePassport(familyMember.Passport);
            if (passportFormatError != null) errors.Add(passportFormatError);

            if (errors.Any())
                throw new ArgumentException($"Validation failed: {string.Join(", ", errors)}");

            var existingFamily = await _context.TkaFamilyMembers.FindAsync(familyMember.Id);
            if (existingFamily == null)
                throw new InvalidOperationException($"Family member dengan ID {familyMember.Id} tidak ditemukan");

            // Check for duplicate passport in TKA workers
            var passportInTka = await _context.TkaWorkers
                .AnyAsync(t => t.Passport == familyMember.Passport);
            if (passportInTka)
                throw new InvalidOperationException($"Passport {familyMember.Passport} sudah digunakan oleh TKA worker");

            // Check for duplicate passport in family members
            var duplicatePassport = await _context.TkaFamilyMembers
                .AnyAsync(f => f.Passport == familyMember.Passport && f.Id != familyMember.Id);
            if (duplicatePassport)
                throw new InvalidOperationException($"Passport {familyMember.Passport} sudah digunakan oleh family member lain");

            // Clean and format data
            familyMember.Nama = ValidationHelper.ToProperCase(familyMember.Nama);
            familyMember.Passport = familyMember.Passport.ToUpper().Trim();

            // Update fields
            existingFamily.Nama = familyMember.Nama;
            existingFamily.Passport = familyMember.Passport;
            existingFamily.JenisKelamin = familyMember.JenisKelamin;
            existingFamily.Relationship = familyMember.Relationship;
            existingFamily.IsActive = familyMember.IsActive;

            await _context.SaveChangesAsync();

            // Clear related cache
            ClearFamilyCache(existingFamily.TkaId);

            return existingFamily;
        }

        /// <summary>
        /// Delete family member (soft delete)
        /// </summary>
        /// <param name="id">Family member ID</param>
        /// <returns>True jika berhasil</returns>
        public async Task<bool> DeleteFamilyMemberAsync(int id)
        {
            var familyMember = await _context.TkaFamilyMembers.FindAsync(id);
            if (familyMember == null)
                return false;

            // Soft delete
            familyMember.IsActive = false;

            await _context.SaveChangesAsync();

            // Clear related cache
            ClearFamilyCache(familyMember.TkaId);

            return true;
        }

        #endregion

        #region TKA Statistics and Analytics

        /// <summary>
        /// Get TKA statistics
        /// </summary>
        /// <param name="tkaId">TKA worker ID (optional, untuk specific TKA)</param>
        /// <returns>Dictionary dengan statistik</returns>
        public async Task<Dictionary<string, object>> GetTkaStatsAsync(int? tkaId = null)
        {
            var cacheKey = $"tka_stats_{tkaId ?? 0}";
            var cached = _context.GetFromCache<Dictionary<string, object>>(cacheKey);
            if (cached != null) return cached;

            var stats = new Dictionary<string, object>();

            if (tkaId.HasValue)
            {
                // Statistics untuk specific TKA
                stats["TotalInvoiceLines"] = await _context.InvoiceLines
                    .CountAsync(il => il.TkaId == tkaId.Value);

                stats["TotalRevenue"] = await _context.InvoiceLines
                    .Where(il => il.TkaId == tkaId.Value)
                    .SumAsync(il => il.LineTotal);

                stats["UniqueCompanies"] = await _context.InvoiceLines
                    .Where(il => il.TkaId == tkaId.Value)
                    .Select(il => il.Invoice.CompanyId)
                    .Distinct()
                    .CountAsync();

                stats["FamilyMembersCount"] = await _context.TkaFamilyMembers
                    .CountAsync(f => f.TkaId == tkaId.Value && f.IsActive);

                var lastInvoiceDate = await _context.InvoiceLines
                    .Where(il => il.TkaId == tkaId.Value)
                    .Select(il => il.Invoice.InvoiceDate)
                    .MaxAsync(d => (DateTime?)d);
                stats["LastInvoiceDate"] = lastInvoiceDate;
            }
            else
            {
                // General TKA statistics
                stats["TotalActiveTka"] = await _context.TkaWorkers
                    .CountAsync(t => t.IsActive);

                stats["TotalInactiveTka"] = await _context.TkaWorkers
                    .CountAsync(t => !t.IsActive);

                stats["TotalFamilyMembers"] = await _context.TkaFamilyMembers
                    .CountAsync(f => f.IsActive);

                // Gender distribution
                var genderStats = await _context.TkaWorkers
                    .Where(t => t.IsActive)
                    .GroupBy(t => t.JenisKelamin)
                    .Select(g => new { Gender = g.Key, Count = g.Count() })
                    .ToListAsync();

                foreach (var genderStat in genderStats)
                {
                    stats[$"TkaBy{genderStat.Gender}"] = genderStat.Count;
                }

                // Division distribution
                var divisionStats = await _context.TkaWorkers
                    .Where(t => t.IsActive && !string.IsNullOrEmpty(t.Divisi))
                    .GroupBy(t => t.Divisi)
                    .Select(g => new { Division = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(10)
                    .ToListAsync();

                stats["TopDivisions"] = divisionStats;
            }

            _context.SetCache(cacheKey, stats, 30);
            return stats;
        }

        /// <summary>
        /// Get TKA performance ranking berdasarkan revenue
        /// </summary>
        /// <param name="topCount">Jumlah top TKA</param>
        /// <param name="companyId">Filter by company (optional)</param>
        /// <returns>List TKA dengan ranking data</returns>
        public async Task<List<TkaPerformance>> GetTopTkaPerformanceAsync(int topCount = 20, int? companyId = null)
        {
            var cacheKey = $"top_tka_performance_{topCount}_{companyId}";
            var cached = _context.GetFromCache<List<TkaPerformance>>(cacheKey);
            if (cached != null) return cached;

            var query = from t in _context.TkaWorkers
                        where t.IsActive
                        join il in _context.InvoiceLines on t.Id equals il.TkaId into invoiceLines
                        from line in invoiceLines.DefaultIfEmpty()
                        where line == null || (companyId == null || line.Invoice.CompanyId == companyId)
                        group new { t, line } by t into g
                        select new TkaPerformance
                        {
                            TkaWorker = g.Key,
                            TotalInvoiceLines = g.Count(x => x.line != null),
                            TotalRevenue = g.Sum(x => x.line != null ? x.line.LineTotal : 0),
                            UniqueCompanies = g.Where(x => x.line != null).Select(x => x.line.Invoice.CompanyId).Distinct().Count(),
                            LastInvoiceDate = g.Max(x => x.line != null ? (DateTime?)x.line.Invoice.InvoiceDate : null)
                        };

            var results = await query
                .OrderByDescending(p => p.TotalRevenue)
                .Take(topCount)
                .ToListAsync();

            // Set rankings
            for (int i = 0; i < results.Count; i++)
            {
                results[i].Rank = i + 1;
            }

            _context.SetCache(cacheKey, results, 30);
            return results;
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Check if passport sudah exists
        /// </summary>
        /// <param name="passport">Passport number</param>
        /// <param name="excludeTkaId">TKA ID to exclude</param>
        /// <param name="excludeFamilyId">Family ID to exclude</param>
        /// <returns>True jika sudah exists</returns>
        public async Task<bool> IsPassportExistsAsync(string passport, int excludeTkaId = 0, int excludeFamilyId = 0)
        {
            passport = passport.ToUpper().Trim();

            var existsInTka = await _context.TkaWorkers
                .AnyAsync(t => t.Passport == passport && t.Id != excludeTkaId);

            var existsInFamily = await _context.TkaFamilyMembers
                .AnyAsync(f => f.Passport == passport && f.Id != excludeFamilyId);

            return existsInTka || existsInFamily;
        }

        /// <summary>
        /// Get TKA workers yang belum pernah ada di invoice
        /// </summary>
        /// <returns>List TKA workers yang belum digunakan</returns>
        public async Task<List<TkaWorker>> GetUnusedTkaWorkersAsync()
        {
            var usedTkaIds = await _context.InvoiceLines
                .Select(il => il.TkaId)
                .Distinct()
                .ToListAsync();

            return await _context.TkaWorkers
                .Where(t => t.IsActive && !usedTkaIds.Contains(t.Id))
                .OrderBy(t => t.Nama)
                .ToListAsync();
        }

        /// <summary>
        /// Bulk import TKA workers dari list
        /// </summary>
        /// <param name="tkaWorkers">List TKA workers untuk import</param>
        /// <param name="skipDuplicates">Skip duplicate passport numbers</param>
        /// <returns>Import result dengan statistics</returns>
        public async Task<TkaImportResult> BulkImportTkaWorkersAsync(List<TkaWorker> tkaWorkers, bool skipDuplicates = true)
        {
            var result = new TkaImportResult();
            var existingPassports = await _context.TkaWorkers
                .Select(t => t.Passport)
                .ToListAsync();

            var familyPassports = await _context.TkaFamilyMembers
                .Select(f => f.Passport)
                .ToListAsync();

            var allExistingPassports = existingPassports.Concat(familyPassports).ToHashSet();

            foreach (var tka in tkaWorkers)
            {
                try
                {
                    // Validate data
                    var errors = ValidationHelper.ValidateTkaWorker(tka.Nama, tka.Passport, tka.Divisi);
                    if (errors.Any())
                    {
                        result.Errors.Add($"Row {result.ProcessedCount + 1}: {string.Join(", ", errors)}");
                        result.ErrorCount++;
                        result.ProcessedCount++;
                        continue;
                    }

                    // Check duplicate
                    if (allExistingPassports.Contains(tka.Passport))
                    {
                        if (skipDuplicates)
                        {
                            result.SkippedCount++;
                            result.ProcessedCount++;
                            continue;
                        }
                        else
                        {
                            result.Errors.Add($"Row {result.ProcessedCount + 1}: Passport {tka.Passport} sudah exists");
                            result.ErrorCount++;
                            result.ProcessedCount++;
                            continue;
                        }
                    }

                    // Clean data
                    tka.Nama = ValidationHelper.ToProperCase(tka.Nama);
                    tka.Passport = tka.Passport.ToUpper().Trim();
                    tka.Divisi = ValidationHelper.ToProperCase(tka.Divisi);
                    tka.CreatedAt = DateTime.UtcNow;
                    tka.UpdatedAt = DateTime.UtcNow;

                    _context.TkaWorkers.Add(tka);
                    allExistingPassports.Add(tka.Passport); // Add to prevent duplicates within batch
                    result.SuccessCount++;
                    result.ProcessedCount++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Row {result.ProcessedCount + 1}: {ex.Message}");
                    result.ErrorCount++;
                    result.ProcessedCount++;
                }
            }

            if (result.SuccessCount > 0)
            {
                await _context.SaveChangesAsync();
                ClearTkaCache();
            }

            return result;
        }

        /// <summary>
        /// Clear TKA related cache
        /// </summary>
        private void ClearTkaCache()
        {
            _context.ClearCache("tka_workers");
            _context.ClearCache("tka_worker");
            _context.ClearCache("tka_stats");
            _context.ClearCache("tka_search");
            _context.ClearCache("top_tka");
        }

        /// <summary>
        /// Clear family cache for specific TKA
        /// </summary>
        private void ClearFamilyCache(int tkaId)
        {
            _context.ClearCache($"tka_family_{tkaId}");
            _context.ClearCache($"family_search");
        }

        #endregion
    }

    #region Support Classes

    /// <summary>
    /// Class untuk TKA performance data
    /// </summary>
    public class TkaPerformance
    {
        public TkaWorker TkaWorker { get; set; } = null!;
        public int TotalInvoiceLines { get; set; }
        public decimal TotalRevenue { get; set; }
        public int UniqueCompanies { get; set; }
        public DateTime? LastInvoiceDate { get; set; }
        public int Rank { get; set; }
    }

    /// <summary>
    /// Class untuk result bulk import TKA
    /// </summary>
    public class TkaImportResult
    {
        public int ProcessedCount { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public int SkippedCount { get; set; }
        public List<string> Errors { get; set; } = new();
        
        public bool HasErrors => ErrorCount > 0;
        public string Summary => $"Processed: {ProcessedCount}, Success: {SuccessCount}, Errors: {ErrorCount}, Skipped: {SkippedCount}";
    }

    #endregion
}