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
    /// Service untuk mengelola operasi Company dan Job Descriptions
    /// </summary>
    public class CompanyService
    {
        private readonly AppDbContext _context;

        public CompanyService(AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #region Company CRUD Operations

        /// <summary>
        /// Get all companies dengan pagination dan filtering
        /// </summary>
        /// <param name="pageNumber">Nomor halaman (1-based)</param>
        /// <param name="pageSize">Ukuran halaman</param>
        /// <param name="searchTerm">Term pencarian (optional)</param>
        /// <param name="includeInactive">Include inactive companies</param>
        /// <returns>Companies dengan total count</returns>
        public async Task<(List<Company> Companies, int TotalCount)> GetCompaniesAsync(
            int pageNumber = 1, int pageSize = 50, string? searchTerm = null, bool includeInactive = false)
        {
            var cacheKey = $"companies_page_{pageNumber}_{pageSize}_{searchTerm ?? "all"}_{includeInactive}";
            var cached = _context.GetFromCache<(List<Company>, int)>(cacheKey);
            if (cached.Companies != null) return cached;

            var query = _context.Companies.AsNoTracking();

            // Filter active/inactive
            if (!includeInactive)
                query = query.Where(c => c.IsActive);

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                searchTerm = searchTerm.Trim().ToLower();
                query = query.Where(c => 
                    c.CompanyName.ToLower().Contains(searchTerm) ||
                    c.Npwp.Contains(searchTerm) ||
                    c.Idtku.ToLower().Contains(searchTerm) ||
                    c.Address.ToLower().Contains(searchTerm));
            }

            var totalCount = await query.CountAsync();
            var companies = await query
                .OrderBy(c => c.CompanyName)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var result = (companies, totalCount);
            _context.SetCache(cacheKey, result, 5);
            return result;
        }

        /// <summary>
        /// Get company by ID dengan caching
        /// </summary>
        /// <param name="id">Company ID</param>
        /// <param name="includeJobDescriptions">Include job descriptions</param>
        /// <returns>Company atau null jika tidak ditemukan</returns>
        public async Task<Company?> GetCompanyByIdAsync(int id, bool includeJobDescriptions = false)
        {
            var cacheKey = $"company_{id}_{includeJobDescriptions}";
            var cached = _context.GetFromCache<Company>(cacheKey);
            if (cached != null) return cached;

            var query = _context.Companies.AsNoTracking();

            if (includeJobDescriptions)
                query = query.Include(c => c.JobDescriptions.Where(j => j.IsActive));

            var company = await query.FirstOrDefaultAsync(c => c.Id == id);
            
            if (company != null)
                _context.SetCache(cacheKey, company, 10);

            return company;
        }

        /// <summary>
        /// Create new company
        /// </summary>
        /// <param name="company">Company data</param>
        /// <returns>Created company dengan ID</returns>
        public async Task<Company> CreateCompanyAsync(Company company)
        {
            // Validate company data
            var errors = ValidationHelper.ValidateCompany(
                company.CompanyName, company.Npwp, company.Idtku, company.Address);

            if (errors.Any())
                throw new ArgumentException($"Validation failed: {string.Join(", ", errors)}");

            // Check for duplicate NPWP
            var existingNpwp = await _context.Companies
                .AnyAsync(c => c.Npwp == company.Npwp && c.Id != company.Id);
            if (existingNpwp)
                throw new InvalidOperationException($"NPWP {company.Npwp} sudah digunakan oleh perusahaan lain");

            // Set creation time
            company.CreatedAt = DateTime.UtcNow;
            company.UpdatedAt = DateTime.UtcNow;

            _context.Companies.Add(company);
            await _context.SaveChangesAsync();

            // Clear related cache
            ClearCompanyCache();

            return company;
        }

        /// <summary>
        /// Update existing company
        /// </summary>
        /// <param name="company">Updated company data</param>
        /// <returns>Updated company</returns>
        public async Task<Company> UpdateCompanyAsync(Company company)
        {
            // Validate company data
            var errors = ValidationHelper.ValidateCompany(
                company.CompanyName, company.Npwp, company.Idtku, company.Address);

            if (errors.Any())
                throw new ArgumentException($"Validation failed: {string.Join(", ", errors)}");

            // Check if company exists
            var existingCompany = await _context.Companies.FindAsync(company.Id);
            if (existingCompany == null)
                throw new InvalidOperationException($"Company dengan ID {company.Id} tidak ditemukan");

            // Check for duplicate NPWP
            var duplicateNpwp = await _context.Companies
                .AnyAsync(c => c.Npwp == company.Npwp && c.Id != company.Id);
            if (duplicateNpwp)
                throw new InvalidOperationException($"NPWP {company.Npwp} sudah digunakan oleh perusahaan lain");

            // Update fields
            existingCompany.CompanyName = company.CompanyName;
            existingCompany.Npwp = company.Npwp;
            existingCompany.Idtku = company.Idtku;
            existingCompany.Address = company.Address;
            existingCompany.IsActive = company.IsActive;
            existingCompany.UpdateTimestamp();

            await _context.SaveChangesAsync();

            // Clear related cache
            ClearCompanyCache();

            return existingCompany;
        }

        /// <summary>
        /// Soft delete company (set IsActive = false)
        /// </summary>
        /// <param name="id">Company ID</param>
        /// <returns>True jika berhasil</returns>
        public async Task<bool> DeleteCompanyAsync(int id)
        {
            var company = await _context.Companies.FindAsync(id);
            if (company == null)
                return false;

            // Check if company has invoices
            var hasInvoices = await _context.Invoices.AnyAsync(i => i.CompanyId == id);
            if (hasInvoices)
                throw new InvalidOperationException("Tidak dapat menghapus perusahaan yang sudah memiliki invoice");

            // Soft delete
            company.IsActive = false;
            company.UpdateTimestamp();

            // Also deactivate related job descriptions
            var jobDescriptions = await _context.JobDescriptions
                .Where(j => j.CompanyId == id)
                .ToListAsync();

            foreach (var job in jobDescriptions)
            {
                job.IsActive = false;
                job.UpdateTimestamp();
            }

            await _context.SaveChangesAsync();

            // Clear related cache
            ClearCompanyCache();

            return true;
        }

        /// <summary>
        /// Restore deleted company
        /// </summary>
        /// <param name="id">Company ID</param>
        /// <returns>True jika berhasil</returns>
        public async Task<bool> RestoreCompanyAsync(int id)
        {
            var company = await _context.Companies.FindAsync(id);
            if (company == null)
                return false;

            company.IsActive = true;
            company.UpdateTimestamp();

            await _context.SaveChangesAsync();

            // Clear related cache
            ClearCompanyCache();

            return true;
        }

        #endregion

        #region Job Description Operations

        /// <summary>
        /// Get job descriptions by company dengan caching
        /// </summary>
        /// <param name="companyId">Company ID</param>
        /// <param name="includeInactive">Include inactive jobs</param>
        /// <returns>List of job descriptions</returns>
        public async Task<List<JobDescription>> GetJobsByCompanyAsync(int companyId, bool includeInactive = false)
        {
            var cacheKey = $"company_jobs_{companyId}_{includeInactive}";
            var cached = _context.GetFromCache<List<JobDescription>>(cacheKey);
            if (cached != null) return cached;

            var query = _context.JobDescriptions.AsNoTracking()
                .Where(j => j.CompanyId == companyId);

            if (!includeInactive)
                query = query.Where(j => j.IsActive);

            var jobs = await query
                .OrderBy(j => j.SortOrder)
                .ThenBy(j => j.JobName)
                .ToListAsync();

            _context.SetCache(cacheKey, jobs, 30); // Cache 30 menit
            return jobs;
        }

        /// <summary>
        /// Get job description by ID
        /// </summary>
        /// <param name="id">Job description ID</param>
        /// <param name="includeCompany">Include company data</param>
        /// <returns>Job description atau null</returns>
        public async Task<JobDescription?> GetJobDescriptionByIdAsync(int id, bool includeCompany = false)
        {
            var query = _context.JobDescriptions.AsNoTracking();

            if (includeCompany)
                query = query.Include(j => j.Company);

            return await query.FirstOrDefaultAsync(j => j.Id == id);
        }

        /// <summary>
        /// Create new job description
        /// </summary>
        /// <param name="jobDescription">Job description data</param>
        /// <returns>Created job description</returns>
        public async Task<JobDescription> CreateJobDescriptionAsync(JobDescription jobDescription)
        {
            // Validate job description
            var errors = ValidationHelper.ValidateJobDescription(
                jobDescription.JobName, jobDescription.JobDescriptionText, jobDescription.Price);

            if (errors.Any())
                throw new ArgumentException($"Validation failed: {string.Join(", ", errors)}");

            // Verify company exists
            var companyExists = await _context.Companies
                .AnyAsync(c => c.Id == jobDescription.CompanyId && c.IsActive);
            if (!companyExists)
                throw new InvalidOperationException("Company tidak ditemukan atau tidak aktif");

            // Check for duplicate job name dalam company yang sama
            var duplicateJob = await _context.JobDescriptions
                .AnyAsync(j => j.CompanyId == jobDescription.CompanyId && 
                              j.JobName.ToLower() == jobDescription.JobName.ToLower() && 
                              j.Id != jobDescription.Id);
            if (duplicateJob)
                throw new InvalidOperationException($"Job name '{jobDescription.JobName}' sudah ada untuk perusahaan ini");

            // Set sort order jika belum di-set
            if (jobDescription.SortOrder == 0)
            {
                var maxSortOrder = await _context.JobDescriptions
                    .Where(j => j.CompanyId == jobDescription.CompanyId)
                    .MaxAsync(j => (int?)j.SortOrder) ?? 0;
                jobDescription.SortOrder = maxSortOrder + 1;
            }

            jobDescription.CreatedAt = DateTime.UtcNow;
            jobDescription.UpdatedAt = DateTime.UtcNow;

            _context.JobDescriptions.Add(jobDescription);
            await _context.SaveChangesAsync();

            // Clear related cache
            ClearJobCache(jobDescription.CompanyId);

            return jobDescription;
        }

        /// <summary>
        /// Update job description
        /// </summary>
        /// <param name="jobDescription">Updated job description</param>
        /// <returns>Updated job description</returns>
        public async Task<JobDescription> UpdateJobDescriptionAsync(JobDescription jobDescription)
        {
            // Validate job description
            var errors = ValidationHelper.ValidateJobDescription(
                jobDescription.JobName, jobDescription.JobDescriptionText, jobDescription.Price);

            if (errors.Any())
                throw new ArgumentException($"Validation failed: {string.Join(", ", errors)}");

            var existingJob = await _context.JobDescriptions.FindAsync(jobDescription.Id);
            if (existingJob == null)
                throw new InvalidOperationException($"Job description dengan ID {jobDescription.Id} tidak ditemukan");

            // Check for duplicate job name
            var duplicateJob = await _context.JobDescriptions
                .AnyAsync(j => j.CompanyId == jobDescription.CompanyId && 
                              j.JobName.ToLower() == jobDescription.JobName.ToLower() && 
                              j.Id != jobDescription.Id);
            if (duplicateJob)
                throw new InvalidOperationException($"Job name '{jobDescription.JobName}' sudah ada untuk perusahaan ini");

            // Update fields
            existingJob.JobName = jobDescription.JobName;
            existingJob.JobDescriptionText = jobDescription.JobDescriptionText;
            existingJob.Price = jobDescription.Price;
            existingJob.IsActive = jobDescription.IsActive;
            existingJob.SortOrder = jobDescription.SortOrder;
            existingJob.UpdateTimestamp();

            await _context.SaveChangesAsync();

            // Clear related cache
            ClearJobCache(existingJob.CompanyId);

            return existingJob;
        }

        /// <summary>
        /// Delete job description (soft delete)
        /// </summary>
        /// <param name="id">Job description ID</param>
        /// <returns>True jika berhasil</returns>
        public async Task<bool> DeleteJobDescriptionAsync(int id)
        {
            var job = await _context.JobDescriptions.FindAsync(id);
            if (job == null)
                return false;

            // Check if job is used in invoice lines
            var usedInInvoices = await _context.InvoiceLines.AnyAsync(il => il.JobDescriptionId == id);
            if (usedInInvoices)
                throw new InvalidOperationException("Tidak dapat menghapus job description yang sudah digunakan dalam invoice");

            // Soft delete
            job.IsActive = false;
            job.UpdateTimestamp();

            await _context.SaveChangesAsync();

            // Clear related cache
            ClearJobCache(job.CompanyId);

            return true;
        }

        /// <summary>
        /// Update sort order untuk job descriptions
        /// </summary>
        /// <param name="jobSortUpdates">List of ID dan sort order baru</param>
        /// <returns>True jika berhasil</returns>
        public async Task<bool> UpdateJobSortOrderAsync(List<(int JobId, int SortOrder)> jobSortUpdates)
        {
            var jobIds = jobSortUpdates.Select(u => u.JobId).ToList();
            var jobs = await _context.JobDescriptions
                .Where(j => jobIds.Contains(j.Id))
                .ToListAsync();

            var companyIds = new HashSet<int>();

            foreach (var job in jobs)
            {
                var update = jobSortUpdates.First(u => u.JobId == job.Id);
                job.SortOrder = update.SortOrder;
                job.UpdateTimestamp();
                companyIds.Add(job.CompanyId);
            }

            await _context.SaveChangesAsync();

            // Clear cache for affected companies
            foreach (var companyId in companyIds)
            {
                ClearJobCache(companyId);
            }

            return true;
        }

        #endregion

        #region Company Statistics

        /// <summary>
        /// Get company statistics dengan caching
        /// </summary>
        /// <param name="companyId">Company ID</param>
        /// <returns>Dictionary dengan statistik company</returns>
        public async Task<Dictionary<string, object>> GetCompanyStatsAsync(int companyId)
        {
            var cacheKey = $"company_stats_{companyId}";
            var cached = _context.GetFromCache<Dictionary<string, object>>(cacheKey);
            if (cached != null) return cached;

            var stats = new Dictionary<string, object>();

            // Total invoices
            stats["TotalInvoices"] = await _context.Invoices
                .CountAsync(i => i.CompanyId == companyId);

            // Total invoices by status
            var invoicesByStatus = await _context.Invoices
                .Where(i => i.CompanyId == companyId)
                .GroupBy(i => i.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            foreach (var statusGroup in invoicesByStatus)
            {
                stats[$"Invoices{statusGroup.Status}"] = statusGroup.Count;
            }

            // Total amount
            stats["TotalAmount"] = await _context.Invoices
                .Where(i => i.CompanyId == companyId && i.Status != Invoice.InvoiceStatus.Cancelled)
                .SumAsync(i => i.TotalAmount);

            // Unique TKA count
            stats["UniqueTkaCount"] = await _context.InvoiceLines
                .Where(il => il.Invoice.CompanyId == companyId)
                .Select(il => il.TkaId)
                .Distinct()
                .CountAsync();

            // Job descriptions count
            stats["JobDescriptionsCount"] = await _context.JobDescriptions
                .CountAsync(j => j.CompanyId == companyId && j.IsActive);

            // Last invoice date
            var lastInvoiceDate = await _context.Invoices
                .Where(i => i.CompanyId == companyId)
                .MaxAsync(i => (DateTime?)i.InvoiceDate);
            stats["LastInvoiceDate"] = lastInvoiceDate;

            // Average invoice amount
            var avgAmount = await _context.Invoices
                .Where(i => i.CompanyId == companyId && i.Status != Invoice.InvoiceStatus.Cancelled)
                .AverageAsync(i => (decimal?)i.TotalAmount);
            stats["AverageInvoiceAmount"] = avgAmount ?? 0;

            _context.SetCache(cacheKey, stats, 30); // Cache 30 menit
            return stats;
        }

        /// <summary>
        /// Get top companies berdasarkan berbagai kriteria
        /// </summary>
        /// <param name="criteria">Kriteria ranking</param>
        /// <param name="topCount">Jumlah top companies</param>
        /// <returns>List companies dengan ranking data</returns>
        public async Task<List<CompanyRanking>> GetTopCompaniesAsync(string criteria = "revenue", int topCount = 10)
        {
            var cacheKey = $"top_companies_{criteria}_{topCount}";
            var cached = _context.GetFromCache<List<CompanyRanking>>(cacheKey);
            if (cached != null) return cached;

            var query = from c in _context.Companies
                        where c.IsActive
                        join i in _context.Invoices on c.Id equals i.CompanyId into invoices
                        from inv in invoices.DefaultIfEmpty()
                        where inv == null || inv.Status != Invoice.InvoiceStatus.Cancelled
                        group new { c, inv } by c into g
                        select new CompanyRanking
                        {
                            Company = g.Key,
                            TotalInvoices = g.Count(x => x.inv != null),
                            TotalRevenue = g.Sum(x => x.inv != null ? x.inv.TotalAmount : 0),
                            AverageInvoiceAmount = g.Average(x => x.inv != null ? (decimal?)x.inv.TotalAmount : null) ?? 0,
                            LastInvoiceDate = g.Max(x => x.inv != null ? (DateTime?)x.inv.InvoiceDate : null)
                        };

            // Apply sorting based on criteria
            query = criteria.ToLower() switch
            {
                "revenue" => query.OrderByDescending(r => r.TotalRevenue),
                "invoices" => query.OrderByDescending(r => r.TotalInvoices),
                "average" => query.OrderByDescending(r => r.AverageInvoiceAmount),
                "recent" => query.OrderByDescending(r => r.LastInvoiceDate),
                _ => query.OrderByDescending(r => r.TotalRevenue)
            };

            var results = await query.Take(topCount).ToListAsync();

            _context.SetCache(cacheKey, results, 30);
            return results;
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Check if company name atau NPWP sudah exists
        /// </summary>
        /// <param name="companyName">Company name</param>
        /// <param name="npwp">NPWP</param>
        /// <param name="excludeId">ID to exclude from check</param>
        /// <returns>True jika sudah exists</returns>
        public async Task<bool> IsCompanyExistsAsync(string companyName, string npwp, int excludeId = 0)
        {
            return await _context.Companies
                .AnyAsync(c => c.Id != excludeId && 
                              (c.CompanyName.ToLower() == companyName.ToLower() || 
                               c.Npwp == npwp));
        }

        /// <summary>
        /// Get companies yang belum ada job descriptions
        /// </summary>
        /// <returns>List companies tanpa job descriptions</returns>
        public async Task<List<Company>> GetCompaniesWithoutJobsAsync()
        {
            return await _context.Companies
                .Where(c => c.IsActive && !c.JobDescriptions.Any(j => j.IsActive))
                .OrderBy(c => c.CompanyName)
                .ToListAsync();
        }

        /// <summary>
        /// Clear company related cache
        /// </summary>
        private void ClearCompanyCache()
        {
            _context.ClearCache("companies");
            _context.ClearCache("company");
            _context.ClearCache("top_companies");
        }

        /// <summary>
        /// Clear job descriptions cache for specific company
        /// </summary>
        private void ClearJobCache(int companyId)
        {
            _context.ClearCache($"company_jobs_{companyId}");
            _context.ClearCache($"job_search_{companyId}");
        }

        #endregion
    }

    #region Support Classes

    /// <summary>
    /// Class untuk company ranking data
    /// </summary>
    public class CompanyRanking
    {
        public Company Company { get; set; } = null!;
        public int TotalInvoices { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal AverageInvoiceAmount { get; set; }
        public DateTime? LastInvoiceDate { get; set; }
        public int Rank { get; set; }
    }

    #endregion
}