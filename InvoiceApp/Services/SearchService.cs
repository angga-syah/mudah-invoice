using Microsoft.EntityFrameworkCore;
using InvoiceApp.Database;
using InvoiceApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InvoiceApp.Services
{
    /// <summary>
    /// Service untuk advanced search functionality dengan performance optimization
    /// Mendukung fuzzy search, typo tolerance, dan real-time filtering
    /// </summary>
    public class SearchService
    {
        private readonly AppDbContext _context;

        public SearchService(AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #region TKA Search Methods

        /// <summary>
        /// Enhanced TKA search dengan caching dan fuzzy matching
        /// </summary>
        /// <param name="searchTerm">Term pencarian</param>
        /// <param name="companyId">Filter berdasarkan company (optional)</param>
        /// <param name="includeInactive">Include inactive records</param>
        /// <param name="maxResults">Maximum results to return</param>
        /// <returns>List of matching TKA workers</returns>
        public async Task<List<TkaWorker>> SearchTkaAsync(string searchTerm, int? companyId = null, bool includeInactive = false, int maxResults = 50)
        {
            if (string.IsNullOrWhiteSpace(searchTerm) || searchTerm.Length < 2)
                return new List<TkaWorker>();

            // Cache key untuk performance
            var cacheKey = $"tka_search_{searchTerm}_{companyId}_{includeInactive}_{maxResults}";
            var cached = _context.GetFromCache<List<TkaWorker>>(cacheKey);
            if (cached != null) return cached;

            var query = _context.TkaWorkers.AsNoTracking();

            // Filter active status
            if (!includeInactive)
                query = query.Where(t => t.IsActive);

            // Filter by company jika diperlukan (melalui invoice history)
            if (companyId.HasValue)
            {
                var tkaIdsFromCompany = await _context.InvoiceLines
                    .Where(il => il.Invoice.CompanyId == companyId.Value)
                    .Select(il => il.TkaId)
                    .Distinct()
                    .ToListAsync();

                query = query.Where(t => tkaIdsFromCompany.Contains(t.Id));
            }

            // Advanced search - multiple strategies
            var results = await PerformTkaSearch(query, searchTerm, maxResults);

            // Cache hasil untuk 2 menit
            _context.SetCache(cacheKey, results, 2);
            return results;
        }

        /// <summary>
        /// Search TKA by company dengan caching
        /// </summary>
        /// <param name="companyId">Company ID</param>
        /// <param name="includeInactive">Include inactive records</param>
        /// <returns>List TKA yang pernah bekerja di company</returns>
        public async Task<List<TkaWorker>> GetTkaByCompanyAsync(int companyId, bool includeInactive = false)
        {
            var cacheKey = $"company_tka_{companyId}_{includeInactive}";
            var cached = _context.GetFromCache<List<TkaWorker>>(cacheKey);
            if (cached != null) return cached;

            var query = _context.InvoiceLines.AsNoTracking()
                .Where(il => il.Invoice.CompanyId == companyId)
                .Select(il => il.TkaWorker)
                .Where(t => includeInactive || t.IsActive)
                .Distinct();

            var tkaWorkers = await query
                .OrderBy(t => t.Nama)
                .ToListAsync();

            _context.SetCache(cacheKey, tkaWorkers, 15); // Cache 15 menit
            return tkaWorkers;
        }

        /// <summary>
        /// Advanced TKA search implementation dengan fuzzy matching
        /// </summary>
        private async Task<List<TkaWorker>> PerformTkaSearch(IQueryable<TkaWorker> baseQuery, string searchTerm, int maxResults)
        {
            searchTerm = searchTerm.Trim().ToLower();
            var words = searchTerm.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Strategy 1: Exact matches (highest priority)
            var exactMatches = await baseQuery
                .Where(t => t.Nama.ToLower().Contains(searchTerm) ||
                           t.Passport.ToLower().Contains(searchTerm) ||
                           (t.Divisi != null && t.Divisi.ToLower().Contains(searchTerm)))
                .OrderBy(t => t.Nama)
                .Take(maxResults)
                .ToListAsync();

            if (exactMatches.Count >= maxResults)
                return exactMatches;

            // Strategy 2: Word-by-word matching untuk flexible word order
            var wordMatches = new List<TkaWorker>();
            if (words.Length > 1)
            {
                var wordQuery = baseQuery;
                foreach (var word in words)
                {
                    wordQuery = wordQuery.Where(t => 
                        t.Nama.ToLower().Contains(word) ||
                        t.Passport.ToLower().Contains(word) ||
                        (t.Divisi != null && t.Divisi.ToLower().Contains(word)));
                }

                wordMatches = await wordQuery
                    .Where(t => !exactMatches.Select(em => em.Id).Contains(t.Id))
                    .OrderBy(t => t.Nama)
                    .Take(maxResults - exactMatches.Count)
                    .ToListAsync();
            }

            // Strategy 3: Fuzzy/partial matching untuk typo tolerance
            var fuzzyMatches = new List<TkaWorker>();
            var remainingCount = maxResults - exactMatches.Count - wordMatches.Count;
            if (remainingCount > 0)
            {
                var excludeIds = exactMatches.Concat(wordMatches).Select(t => t.Id).ToList();
                fuzzyMatches = await PerformFuzzyTkaSearch(baseQuery, searchTerm, excludeIds, remainingCount);
            }

            // Combine results dengan prioritas
            var finalResults = new List<TkaWorker>();
            finalResults.AddRange(exactMatches);
            finalResults.AddRange(wordMatches);
            finalResults.AddRange(fuzzyMatches);

            return finalResults.Take(maxResults).ToList();
        }

        /// <summary>
        /// Fuzzy search untuk TKA dengan Levenshtein distance approximation
        /// </summary>
        private async Task<List<TkaWorker>> PerformFuzzyTkaSearch(IQueryable<TkaWorker> baseQuery, string searchTerm, List<int> excludeIds, int maxResults)
        {
            // Simplified fuzzy search - check for partial matches and common typos
            var fuzzyQueries = GenerateFuzzyVariations(searchTerm);
            
            var fuzzyQuery = baseQuery.Where(t => !excludeIds.Contains(t.Id));
            
            // Build OR conditions for fuzzy matches
            var combinedQuery = fuzzyQuery.Where(t => false); // Start with false
            
            foreach (var fuzzyTerm in fuzzyQueries)
            {
                combinedQuery = combinedQuery.Union(
                    fuzzyQuery.Where(t => 
                        t.Nama.ToLower().Contains(fuzzyTerm) ||
                        t.Passport.ToLower().Contains(fuzzyTerm) ||
                        (t.Divisi != null && t.Divisi.ToLower().Contains(fuzzyTerm)))
                );
            }

            return await combinedQuery
                .OrderBy(t => t.Nama)
                .Take(maxResults)
                .ToListAsync();
        }

        #endregion

        #region Company Search Methods

        /// <summary>
        /// Enhanced Company search dengan caching
        /// </summary>
        /// <param name="searchTerm">Term pencarian (optional)</param>
        /// <param name="includeInactive">Include inactive companies</param>
        /// <param name="maxResults">Maximum results</param>
        /// <returns>List of matching companies</returns>
        public async Task<List<Company>> SearchCompaniesAsync(string? searchTerm = null, bool includeInactive = false, int maxResults = 100)
        {
            var cacheKey = $"company_search_{searchTerm ?? "all"}_{includeInactive}_{maxResults}";
            var cached = _context.GetFromCache<List<Company>>(cacheKey);
            if (cached != null) return cached;

            var query = _context.Companies.AsNoTracking();

            if (!includeInactive)
                query = query.Where(c => c.IsActive);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                searchTerm = searchTerm.Trim().ToLower();
                query = query.Where(c => 
                    c.CompanyName.ToLower().Contains(searchTerm) ||
                    c.Npwp.Contains(searchTerm) ||
                    c.Idtku.ToLower().Contains(searchTerm));
            }

            var results = await query
                .OrderBy(c => c.CompanyName)
                .Take(maxResults)
                .ToListAsync();

            // Cache lebih lama untuk company karena jarang berubah
            var cacheMinutes = string.IsNullOrEmpty(searchTerm) ? 10 : 5;
            _context.SetCache(cacheKey, results, cacheMinutes);

            return results;
        }

        /// <summary>
        /// Get companies yang paling sering digunakan (berdasarkan jumlah invoice)
        /// </summary>
        /// <param name="topCount">Jumlah top companies</param>
        /// <returns>List companies terurut berdasarkan usage</returns>
        public async Task<List<Company>> GetTopCompaniesAsync(int topCount = 10)
        {
            var cacheKey = $"top_companies_{topCount}";
            var cached = _context.GetFromCache<List<Company>>(cacheKey);
            if (cached != null) return cached;

            var topCompanies = await _context.Invoices
                .Where(i => i.Status != Invoice.InvoiceStatus.Cancelled)
                .GroupBy(i => i.CompanyId)
                .Select(g => new { CompanyId = g.Key, InvoiceCount = g.Count() })
                .OrderByDescending(x => x.InvoiceCount)
                .Take(topCount)
                .Join(_context.Companies,
                      stats => stats.CompanyId,
                      company => company.Id,
                      (stats, company) => company)
                .ToListAsync();

            _context.SetCache(cacheKey, topCompanies, 30); // Cache 30 menit
            return topCompanies;
        }

        #endregion

        #region Job Description Search Methods

        /// <summary>
        /// Search job descriptions by company
        /// </summary>
        /// <param name="companyId">Company ID</param>
        /// <param name="searchTerm">Term pencarian (optional)</param>
        /// <param name="includeInactive">Include inactive jobs</param>
        /// <returns>List of job descriptions</returns>
        public async Task<List<JobDescription>> SearchJobDescriptionsAsync(int companyId, string? searchTerm = null, bool includeInactive = false)
        {
            var cacheKey = $"job_search_{companyId}_{searchTerm ?? "all"}_{includeInactive}";
            var cached = _context.GetFromCache<List<JobDescription>>(cacheKey);
            if (cached != null) return cached;

            var query = _context.JobDescriptions.AsNoTracking()
                .Where(j => j.CompanyId == companyId);

            if (!includeInactive)
                query = query.Where(j => j.IsActive);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                searchTerm = searchTerm.Trim().ToLower();
                query = query.Where(j => 
                    j.JobName.ToLower().Contains(searchTerm) ||
                    j.JobDescriptionText.ToLower().Contains(searchTerm));
            }

            var results = await query
                .OrderBy(j => j.SortOrder)
                .ThenBy(j => j.JobName)
                .ToListAsync();

            _context.SetCache(cacheKey, results, 30); // Cache 30 menit
            return results;
        }

        #endregion

        #region Invoice Search Methods

        /// <summary>
        /// Search invoices dengan multiple criteria
        /// </summary>
        /// <param name="criteria">Search criteria</param>
        /// <returns>List of matching invoices</returns>
        public async Task<List<Invoice>> SearchInvoicesAsync(InvoiceSearchCriteria criteria)
        {
            var cacheKey = $"invoice_search_{criteria.GetCacheKey()}";
            var cached = _context.GetFromCache<List<Invoice>>(cacheKey);
            if (cached != null) return cached;

            var query = _context.Invoices.AsNoTracking()
                .Include(i => i.Company);

            // Apply filters
            if (!string.IsNullOrWhiteSpace(criteria.InvoiceNumber))
            {
                query = query.Where(i => i.InvoiceNumber.Contains(criteria.InvoiceNumber));
            }

            if (criteria.CompanyId.HasValue)
            {
                query = query.Where(i => i.CompanyId == criteria.CompanyId.Value);
            }

            if (!string.IsNullOrWhiteSpace(criteria.CompanyName))
            {
                query = query.Where(i => i.Company.CompanyName.ToLower().Contains(criteria.CompanyName.ToLower()));
            }

            if (criteria.DateFrom.HasValue)
            {
                query = query.Where(i => i.InvoiceDate >= criteria.DateFrom.Value);
            }

            if (criteria.DateTo.HasValue)
            {
                query = query.Where(i => i.InvoiceDate <= criteria.DateTo.Value);
            }

            if (criteria.Status != null && criteria.Status.Any())
            {
                query = query.Where(i => criteria.Status.Contains(i.Status));
            }

            if (criteria.MinAmount.HasValue)
            {
                query = query.Where(i => i.TotalAmount >= criteria.MinAmount.Value);
            }

            if (criteria.MaxAmount.HasValue)
            {
                query = query.Where(i => i.TotalAmount <= criteria.MaxAmount.Value);
            }

            // Apply sorting
            query = criteria.SortBy?.ToLower() switch
            {
                "number" => criteria.SortDescending ? query.OrderByDescending(i => i.InvoiceNumber) : query.OrderBy(i => i.InvoiceNumber),
                "company" => criteria.SortDescending ? query.OrderByDescending(i => i.Company.CompanyName) : query.OrderBy(i => i.Company.CompanyName),
                "amount" => criteria.SortDescending ? query.OrderByDescending(i => i.TotalAmount) : query.OrderBy(i => i.TotalAmount),
                "status" => criteria.SortDescending ? query.OrderByDescending(i => i.Status) : query.OrderBy(i => i.Status),
                _ => criteria.SortDescending ? query.OrderByDescending(i => i.InvoiceDate) : query.OrderBy(i => i.InvoiceDate)
            };

            var results = await query
                .Skip(criteria.Skip)
                .Take(criteria.Take)
                .ToListAsync();

            _context.SetCache(cacheKey, results, 5); // Cache 5 menit
            return results;
        }

        #endregion

        #region Family Search Methods

        /// <summary>
        /// Search TKA family members
        /// </summary>
        /// <param name="searchTerm">Term pencarian</param>
        /// <param name="tkaId">Filter by TKA ID (optional)</param>
        /// <param name="maxResults">Maximum results</param>
        /// <returns>List of family members</returns>
        public async Task<List<TkaFamily>> SearchTkaFamilyAsync(string searchTerm, int? tkaId = null, int maxResults = 50)
        {
            if (string.IsNullOrWhiteSpace(searchTerm) || searchTerm.Length < 2)
                return new List<TkaFamily>();

            var cacheKey = $"family_search_{searchTerm}_{tkaId}_{maxResults}";
            var cached = _context.GetFromCache<List<TkaFamily>>(cacheKey);
            if (cached != null) return cached;

            var query = _context.TkaFamilyMembers.AsNoTracking()
                .Include(f => f.TkaWorker)
                .Where(f => f.IsActive);

            if (tkaId.HasValue)
            {
                query = query.Where(f => f.TkaId == tkaId.Value);
            }

            searchTerm = searchTerm.ToLower();
            query = query.Where(f => 
                f.Nama.ToLower().Contains(searchTerm) ||
                f.Passport.ToLower().Contains(searchTerm) ||
                f.TkaWorker.Nama.ToLower().Contains(searchTerm));

            var results = await query
                .OrderBy(f => f.TkaWorker.Nama)
                .ThenBy(f => f.Nama)
                .Take(maxResults)
                .ToListAsync();

            _context.SetCache(cacheKey, results, 10);
            return results;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Generate fuzzy variations untuk typo tolerance
        /// </summary>
        private List<string> GenerateFuzzyVariations(string input)
        {
            var variations = new List<string>();
            
            if (input.Length < 3) return variations;

            // Common Indonesian typos and variations
            var typoMap = new Dictionary<char, char[]>
            {
                { 'a', new[] { 'e', 'o' } },
                { 'e', new[] { 'a', 'i' } },
                { 'i', new[] { 'e', 'y' } },
                { 'o', new[] { 'a', 'u' } },
                { 'u', new[] { 'o', 'i' } },
                { 'y', new[] { 'i' } },
                { 'c', new[] { 'k', 's' } },
                { 'k', new[] { 'c', 'q' } },
                { 'f', new[] { 'v', 'p' } },
                { 'v', new[] { 'f', 'w' } }
            };

            // Generate single character substitutions
            for (int i = 0; i < input.Length; i++)
            {
                var ch = input[i];
                if (typoMap.ContainsKey(ch))
                {
                    foreach (var replacement in typoMap[ch])
                    {
                        var variation = input.Substring(0, i) + replacement + input.Substring(i + 1);
                        variations.Add(variation);
                    }
                }
            }

            // Add partial matches (remove first/last character)
            if (input.Length > 3)
            {
                variations.Add(input[1..]); // Remove first char
                variations.Add(input[..^1]); // Remove last char
            }

            return variations.Distinct().ToList();
        }

        /// <summary>
        /// Clear all search caches
        /// </summary>
        public void ClearSearchCache()
        {
            _context.ClearCache("search");
            _context.ClearCache("company");
            _context.ClearCache("tka");
            _context.ClearCache("job");
            _context.ClearCache("invoice");
            _context.ClearCache("family");
        }

        /// <summary>
        /// Clear specific cache by pattern
        /// </summary>
        public void ClearCacheByPattern(string pattern)
        {
            _context.ClearCache(pattern);
        }

        #endregion
    }

    #region Search Criteria Classes

    /// <summary>
    /// Criteria untuk invoice search
    /// </summary>
    public class InvoiceSearchCriteria
    {
        public string? InvoiceNumber { get; set; }
        public int? CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public List<string>? Status { get; set; }
        public decimal? MinAmount { get; set; }
        public decimal? MaxAmount { get; set; }
        public string? SortBy { get; set; } = "date";
        public bool SortDescending { get; set; } = true;
        public int Skip { get; set; } = 0;
        public int Take { get; set; } = 50;

        public string GetCacheKey()
        {
            var status = Status != null ? string.Join(",", Status) : "";
            return $"{InvoiceNumber}_{CompanyId}_{CompanyName}_{DateFrom:yyyyMMdd}_{DateTo:yyyyMMdd}_{status}_{MinAmount}_{MaxAmount}_{SortBy}_{SortDescending}_{Skip}_{Take}";
        }
    }

    #endregion
}