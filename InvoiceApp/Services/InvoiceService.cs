using Microsoft.EntityFrameworkCore;
using InvoiceApp.Database;
using InvoiceApp.Models;
using InvoiceApp.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;

namespace InvoiceApp.Services
{
    /// <summary>
    /// Service untuk mengelola operasi Invoice dengan auto-save, real-time calculation, dan import/export
    /// </summary>
    public class InvoiceService
    {
        private readonly AppDbContext _context;

        public InvoiceService(AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #region Invoice CRUD Operations

        /// <summary>
        /// Get invoices dengan pagination, filtering, dan caching
        /// </summary>
        /// <param name="pageNumber">Nomor halaman (1-based)</param>
        /// <param name="pageSize">Ukuran halaman</param>
        /// <param name="companyId">Filter berdasarkan company (optional)</param>
        /// <param name="status">Filter berdasarkan status (optional)</param>
        /// <param name="dateFrom">Filter tanggal dari (optional)</param>
        /// <param name="dateTo">Filter tanggal sampai (optional)</param>
        /// <param name="searchTerm">Term pencarian untuk invoice number atau company name</param>
        /// <returns>Invoices dengan total count</returns>
        public async Task<(List<Invoice> Invoices, int TotalCount)> GetInvoicesAsync(
            int pageNumber = 1, int pageSize = 50, int? companyId = null, string? status = null,
            DateTime? dateFrom = null, DateTime? dateTo = null, string? searchTerm = null)
        {
            var cacheKey = $"invoices_{pageNumber}_{pageSize}_{companyId}_{status}_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}_{searchTerm}";
            var cached = _context.GetFromCache<(List<Invoice>, int)>(cacheKey);
            if (cached.Invoices != null) return cached;

            var query = _context.Invoices.AsNoTracking()
                .Include(i => i.Company)
                .Include(i => i.CreatedByUser);

            // Apply filters
            if (companyId.HasValue)
                query = query.Where(i => i.CompanyId == companyId.Value);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(i => i.Status == status);

            if (dateFrom.HasValue)
                query = query.Where(i => i.InvoiceDate >= dateFrom.Value);

            if (dateTo.HasValue)
                query = query.Where(i => i.InvoiceDate <= dateTo.Value);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                searchTerm = searchTerm.Trim().ToLower();
                query = query.Where(i => 
                    i.InvoiceNumber.ToLower().Contains(searchTerm) ||
                    i.Company.CompanyName.ToLower().Contains(searchTerm));
            }

            var totalCount = await query.CountAsync();
            var invoices = await query
                .OrderByDescending(i => i.InvoiceDate)
                .ThenByDescending(i => i.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var result = (invoices, totalCount);
            _context.SetCache(cacheKey, result, 5); // Cache 5 menit
            return result;
        }

        /// <summary>
        /// Get invoice by ID dengan semua relasi
        /// </summary>
        /// <param name="id">Invoice ID</param>
        /// <param name="includeLines">Include invoice lines</param>
        /// <returns>Invoice atau null jika tidak ditemukan</returns>
        public async Task<Invoice?> GetInvoiceByIdAsync(int id, bool includeLines = true)
        {
            var cacheKey = $"invoice_{id}_{includeLines}";
            var cached = _context.GetFromCache<Invoice>(cacheKey);
            if (cached != null) return cached;

            var query = _context.Invoices.AsNoTracking()
                .Include(i => i.Company)
                .Include(i => i.CreatedByUser)
                .Include(i => i.BankAccount);

            if (includeLines)
            {
                query = query.Include(i => i.InvoiceLines)
                    .ThenInclude(il => il.TkaWorker)
                    .Include(i => i.InvoiceLines)
                    .ThenInclude(il => il.JobDescription);
            }

            var invoice = await query.FirstOrDefaultAsync(i => i.Id == id);
            
            if (invoice != null)
                _context.SetCache(cacheKey, invoice, 10);

            return invoice;
        }

        /// <summary>
        /// Get invoice by invoice number
        /// </summary>
        /// <param name="invoiceNumber">Invoice number</param>
        /// <returns>Invoice atau null</returns>
        public async Task<Invoice?> GetInvoiceByNumberAsync(string invoiceNumber)
        {
            if (string.IsNullOrWhiteSpace(invoiceNumber))
                return null;

            return await _context.Invoices
                .AsNoTracking()
                .Include(i => i.Company)
                .Include(i => i.InvoiceLines)
                .ThenInclude(il => il.TkaWorker)
                .Include(i => i.InvoiceLines)
                .ThenInclude(il => il.JobDescription)
                .FirstOrDefaultAsync(i => i.InvoiceNumber == invoiceNumber.Trim());
        }

        /// <summary>
        /// Create new invoice dengan auto-generated invoice number
        /// </summary>
        /// <param name="invoice">Invoice data</param>
        /// <param name="userId">User ID yang membuat invoice</param>
        /// <returns>Created invoice dengan ID dan invoice number</returns>
        public async Task<Invoice> CreateInvoiceAsync(Invoice invoice, int userId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Validate invoice data
                if (!invoice.IsValid(out var errors))
                    throw new ArgumentException($"Validation failed: {string.Join(", ", errors)}");

                // Verify company exists
                var companyExists = await _context.Companies
                    .AnyAsync(c => c.Id == invoice.CompanyId && c.IsActive);
                if (!companyExists)
                    throw new InvalidOperationException("Company tidak ditemukan atau tidak aktif");

                // Generate invoice number jika belum ada
                if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
                {
                    invoice.InvoiceNumber = await GenerateInvoiceNumberAsync(invoice.InvoiceDate);
                }
                else
                {
                    // Check duplicate invoice number
                    var duplicateNumber = await _context.Invoices
                        .AnyAsync(i => i.InvoiceNumber == invoice.InvoiceNumber);
                    if (duplicateNumber)
                        throw new InvalidOperationException($"Invoice number {invoice.InvoiceNumber} sudah digunakan");
                }

                // Set default values
                invoice.CreatedBy = userId;
                invoice.CreatedAt = DateTime.UtcNow;
                invoice.UpdatedAt = DateTime.UtcNow;
                invoice.Status = Invoice.InvoiceStatus.Draft;

                // Calculate totals
                invoice.CalculateTotals();

                _context.Invoices.Add(invoice);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Clear related cache
                ClearInvoiceCache();

                return invoice;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Update existing invoice
        /// </summary>
        /// <param name="invoice">Updated invoice data</param>
        /// <returns>Updated invoice</returns>
        public async Task<Invoice> UpdateInvoiceAsync(Invoice invoice)
        {
            // Validate invoice data
            if (!invoice.IsValid(out var errors))
                throw new ArgumentException($"Validation failed: {string.Join(", ", errors)}");

            var existingInvoice = await _context.Invoices
                .Include(i => i.InvoiceLines)
                .FirstOrDefaultAsync(i => i.Id == invoice.Id);
            
            if (existingInvoice == null)
                throw new InvalidOperationException($"Invoice dengan ID {invoice.Id} tidak ditemukan");

            // Check if invoice can be edited
            if (!existingInvoice.CanEdit)
                throw new InvalidOperationException("Invoice yang sudah finalized tidak dapat diedit");

            // Check duplicate invoice number
            var duplicateNumber = await _context.Invoices
                .AnyAsync(i => i.InvoiceNumber == invoice.InvoiceNumber && i.Id != invoice.Id);
            if (duplicateNumber)
                throw new InvalidOperationException($"Invoice number {invoice.InvoiceNumber} sudah digunakan");

            // Update fields
            existingInvoice.InvoiceNumber = invoice.InvoiceNumber;
            existingInvoice.CompanyId = invoice.CompanyId;
            existingInvoice.InvoiceDate = invoice.InvoiceDate;
            existingInvoice.VatPercentage = invoice.VatPercentage;
            existingInvoice.Notes = invoice.Notes;
            existingInvoice.BankAccountId = invoice.BankAccountId;

            // Recalculate totals
            existingInvoice.CalculateTotals();

            await _context.SaveChangesAsync();

            // Clear related cache
            ClearInvoiceCache();

            return existingInvoice;
        }

        /// <summary>
        /// Delete invoice (soft delete untuk draft, hard delete untuk yang lain perlu konfirmasi)
        /// </summary>
        /// <param name="id">Invoice ID</param>
        /// <param name="forceDelete">Force delete even if finalized</param>
        /// <returns>True jika berhasil</returns>
        public async Task<bool> DeleteInvoiceAsync(int id, bool forceDelete = false)
        {
            var invoice = await _context.Invoices
                .Include(i => i.InvoiceLines)
                .FirstOrDefaultAsync(i => i.Id == id);
            
            if (invoice == null)
                return false;

            // Check if can delete
            if (invoice.Status == Invoice.InvoiceStatus.Paid && !forceDelete)
                throw new InvalidOperationException("Invoice yang sudah dibayar tidak dapat dihapus");

            if (invoice.Status == Invoice.InvoiceStatus.Finalized && !forceDelete)
                throw new InvalidOperationException("Invoice yang sudah finalized tidak dapat dihapus. Gunakan force delete jika diperlukan.");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Delete invoice lines first (cascade should handle this, but explicit is better)
                _context.InvoiceLines.RemoveRange(invoice.InvoiceLines);
                
                // Delete invoice
                _context.Invoices.Remove(invoice);
                
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Clear related cache
                ClearInvoiceCache();

                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        #endregion

        #region Invoice Lines Operations

        /// <summary>
        /// Add invoice line ke invoice
        /// </summary>
        /// <param name="invoiceId">Invoice ID</param>
        /// <param name="invoiceLine">Invoice line data</param>
        /// <returns>Added invoice line</returns>
        public async Task<InvoiceLine> AddInvoiceLineAsync(int invoiceId, InvoiceLine invoiceLine)
        {
            var invoice = await _context.Invoices
                .Include(i => i.InvoiceLines)
                .FirstOrDefaultAsync(i => i.Id == invoiceId);

            if (invoice == null)
                throw new InvalidOperationException($"Invoice dengan ID {invoiceId} tidak ditemukan");

            if (!invoice.CanEdit)
                throw new InvalidOperationException("Invoice yang sudah finalized tidak dapat diedit");

            // Validate invoice line
            if (!invoiceLine.IsValid(out var errors))
                throw new ArgumentException($"Validation failed: {string.Join(", ", errors)}");

            // Set invoice ID dan auto-increment line order
            invoiceLine.InvoiceId = invoiceId;
            
            // Set baris if not specified
            if (invoiceLine.Baris == 0)
            {
                var maxBaris = invoice.InvoiceLines.Any() ? 
                    invoice.InvoiceLines.Max(il => il.Baris) : 0;
                invoiceLine.Baris = maxBaris + 1;
            }

            // Set line order if not specified
            if (invoiceLine.LineOrder == 0)
            {
                var maxLineOrder = invoice.InvoiceLines
                    .Where(il => il.Baris == invoiceLine.Baris)
                    .Select(il => (int?)il.LineOrder)
                    .Max() ?? 0;
                invoiceLine.LineOrder = maxLineOrder + 1;
            }

            // Calculate line total
            invoiceLine.CalculateLineTotal();

            invoice.InvoiceLines.Add(invoiceLine);
            
            // Recalculate invoice totals
            invoice.CalculateTotals();

            await _context.SaveChangesAsync();

            // Clear related cache
            ClearInvoiceCache();

            return invoiceLine;
        }

        /// <summary>
        /// Update invoice line
        /// </summary>
        /// <param name="invoiceLine">Updated invoice line data</param>
        /// <returns>Updated invoice line</returns>
        public async Task<InvoiceLine> UpdateInvoiceLineAsync(InvoiceLine invoiceLine)
        {
            var existingLine = await _context.InvoiceLines
                .Include(il => il.Invoice)
                .FirstOrDefaultAsync(il => il.Id == invoiceLine.Id);

            if (existingLine == null)
                throw new InvalidOperationException($"Invoice line dengan ID {invoiceLine.Id} tidak ditemukan");

            if (!existingLine.Invoice.CanEdit)
                throw new InvalidOperationException("Invoice yang sudah finalized tidak dapat diedit");

            // Validate invoice line
            if (!invoiceLine.IsValid(out var errors))
                throw new ArgumentException($"Validation failed: {string.Join(", ", errors)}");

            // Update fields
            existingLine.TkaId = invoiceLine.TkaId;
            existingLine.JobDescriptionId = invoiceLine.JobDescriptionId;
            existingLine.CustomJobName = invoiceLine.CustomJobName;
            existingLine.CustomJobDescription = invoiceLine.CustomJobDescription;
            existingLine.CustomPrice = invoiceLine.CustomPrice;
            existingLine.Quantity = invoiceLine.Quantity;
            existingLine.Baris = invoiceLine.Baris;
            existingLine.LineOrder = invoiceLine.LineOrder;

            // Recalculate line total
            existingLine.CalculateLineTotal();

            // Recalculate invoice totals
            existingLine.Invoice.CalculateTotals();

            await _context.SaveChangesAsync();

            // Clear related cache
            ClearInvoiceCache();

            return existingLine;
        }

        /// <summary>
        /// Delete invoice line
        /// </summary>
        /// <param name="id">Invoice line ID</param>
        /// <returns>True jika berhasil</returns>
        public async Task<bool> DeleteInvoiceLineAsync(int id)
        {
            var invoiceLine = await _context.InvoiceLines
                .Include(il => il.Invoice)
                .FirstOrDefaultAsync(il => il.Id == id);

            if (invoiceLine == null)
                return false;

            if (!invoiceLine.Invoice.CanEdit)
                throw new InvalidOperationException("Invoice yang sudah finalized tidak dapat diedit");

            _context.InvoiceLines.Remove(invoiceLine);

            // Recalculate invoice totals
            invoiceLine.Invoice.CalculateTotals();

            await _context.SaveChangesAsync();

            // Clear related cache
            ClearInvoiceCache();

            return true;
        }

        /// <summary>
        /// Reorder invoice lines within same baris
        /// </summary>
        /// <param name="invoiceId">Invoice ID</param>
        /// <param name="lineUpdates">List of line ID dan order baru</param>
        /// <returns>True jika berhasil</returns>
        public async Task<bool> ReorderInvoiceLinesAsync(int invoiceId, List<(int LineId, int Baris, int LineOrder)> lineUpdates)
        {
            var invoice = await _context.Invoices
                .Include(i => i.InvoiceLines)
                .FirstOrDefaultAsync(i => i.Id == invoiceId);

            if (invoice == null)
                throw new InvalidOperationException($"Invoice dengan ID {invoiceId} tidak ditemukan");

            if (!invoice.CanEdit)
                throw new InvalidOperationException("Invoice yang sudah finalized tidak dapat diedit");

            var lineIds = lineUpdates.Select(u => u.LineId).ToList();
            var lines = invoice.InvoiceLines.Where(il => lineIds.Contains(il.Id)).ToList();

            foreach (var line in lines)
            {
                var update = lineUpdates.First(u => u.LineId == line.Id);
                line.Baris = update.Baris;
                line.LineOrder = update.LineOrder;
            }

            await _context.SaveChangesAsync();

            // Clear related cache
            ClearInvoiceCache();

            return true;
        }

        #endregion

        #region Invoice Status Management

        /// <summary>
        /// Finalize invoice (change status dari draft ke finalized)
        /// </summary>
        /// <param name="id">Invoice ID</param>
        /// <returns>Updated invoice</returns>
        public async Task<Invoice> FinalizeInvoiceAsync(int id)
        {
            var invoice = await _context.Invoices
                .Include(i => i.InvoiceLines)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (invoice == null)
                throw new InvalidOperationException($"Invoice dengan ID {id} tidak ditemukan");

            if (invoice.Status != Invoice.InvoiceStatus.Draft)
                throw new InvalidOperationException("Hanya invoice dengan status Draft yang dapat di-finalize");

            if (!invoice.InvoiceLines.Any())
                throw new InvalidOperationException("Invoice harus memiliki minimal 1 line item");

            // Recalculate totals sebelum finalize
            invoice.CalculateTotals();

            // Change status
            invoice.Finalize();

            await _context.SaveChangesAsync();

            // Clear related cache
            ClearInvoiceCache();

            return invoice;
        }

        /// <summary>
        /// Mark invoice as paid
        /// </summary>
        /// <param name="id">Invoice ID</param>
        /// <returns>Updated invoice</returns>
        public async Task<Invoice> MarkAsPaidAsync(int id)
        {
            var invoice = await _context.Invoices.FindAsync(id);
            if (invoice == null)
                throw new InvalidOperationException($"Invoice dengan ID {id} tidak ditemukan");

            if (invoice.Status != Invoice.InvoiceStatus.Finalized)
                throw new InvalidOperationException("Hanya invoice dengan status Finalized yang dapat di-mark sebagai Paid");

            invoice.MarkAsPaid();
            await _context.SaveChangesAsync();

            // Clear related cache
            ClearInvoiceCache();

            return invoice;
        }

        /// <summary>
        /// Cancel invoice
        /// </summary>
        /// <param name="id">Invoice ID</param>
        /// <returns>Updated invoice</returns>
        public async Task<Invoice> CancelInvoiceAsync(int id)
        {
            var invoice = await _context.Invoices.FindAsync(id);
            if (invoice == null)
                throw new InvalidOperationException($"Invoice dengan ID {id} tidak ditemukan");

            if (invoice.Status == Invoice.InvoiceStatus.Paid)
                throw new InvalidOperationException("Invoice yang sudah dibayar tidak dapat di-cancel");

            invoice.Cancel();
            await _context.SaveChangesAsync();

            // Clear related cache
            ClearInvoiceCache();

            return invoice;
        }

        #endregion

        #region Invoice Number Generation

        /// <summary>
        /// Generate invoice number dengan format FSN/YY/MM/NNN
        /// </summary>
        /// <param name="invoiceDate">Invoice date untuk generate number</param>
        /// <returns>Generated invoice number</returns>
        public async Task<string> GenerateInvoiceNumberAsync(DateTime invoiceDate)
        {
            // Format: FSN/YY/MM/NNN
            var year = invoiceDate.Year;
            var month = invoiceDate.Month;
            var yearSuffix = invoiceDate.ToString("yy");
            var monthSuffix = invoiceDate.ToString("MM");

            // Get or create sequence record
            var sequence = await _context.Database.SqlQueryRaw<InvoiceNumberSequence>(
                "SELECT * FROM invoice_number_sequences WHERE year = {0} AND month = {1}",
                year, month).FirstOrDefaultAsync();

            if (sequence == null)
            {
                // Create new sequence
                sequence = new InvoiceNumberSequence
                {
                    Year = year,
                    Month = month,
                    CurrentNumber = 1,
                    Prefix = "FSN"
                };

                await _context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO invoice_number_sequences (year, month, current_number, prefix, created_at, updated_at) VALUES ({0}, {1}, {2}, {3}, {4}, {5})",
                    year, month, 1, "FSN", DateTime.UtcNow, DateTime.UtcNow);
            }
            else
            {
                // Increment sequence
                await _context.Database.ExecuteSqlRawAsync(
                    "UPDATE invoice_number_sequences SET current_number = current_number + 1, updated_at = {0} WHERE year = {1} AND month = {2}",
                    DateTime.UtcNow, year, month);
                sequence.CurrentNumber++;
            }

            var numberPart = sequence.CurrentNumber.ToString("D3");
            return $"{sequence.Prefix}/{yearSuffix}/{monthSuffix}/{numberPart}";
        }

        /// <summary>
        /// Check if invoice number is available
        /// </summary>
        /// <param name="invoiceNumber">Invoice number to check</param>
        /// <param name="excludeId">Invoice ID to exclude from check</param>
        /// <returns>True jika available</returns>
        public async Task<bool> IsInvoiceNumberAvailableAsync(string invoiceNumber, int excludeId = 0)
        {
            return !await _context.Invoices
                .AnyAsync(i => i.InvoiceNumber == invoiceNumber && i.Id != excludeId);
        }

        #endregion

        #region Auto-Save Functionality

        /// <summary>
        /// Auto-save invoice draft dengan throttling
        /// </summary>
        /// <param name="invoice">Invoice data untuk save</param>
        /// <returns>True jika berhasil save</returns>
        public async Task<bool> AutoSaveInvoiceAsync(Invoice invoice)
        {
            try
            {
                // Only auto-save draft invoices
                if (invoice.Status != Invoice.InvoiceStatus.Draft)
                    return false;

                // Get existing invoice
                var existingInvoice = await _context.Invoices
                    .Include(i => i.InvoiceLines)
                    .FirstOrDefaultAsync(i => i.Id == invoice.Id);

                if (existingInvoice == null)
                    return false;

                // Update only changed fields
                var hasChanges = false;

                if (existingInvoice.InvoiceNumber != invoice.InvoiceNumber)
                {
                    existingInvoice.InvoiceNumber = invoice.InvoiceNumber;
                    hasChanges = true;
                }

                if (existingInvoice.CompanyId != invoice.CompanyId)
                {
                    existingInvoice.CompanyId = invoice.CompanyId;
                    hasChanges = true;
                }

                if (existingInvoice.InvoiceDate != invoice.InvoiceDate)
                {
                    existingInvoice.InvoiceDate = invoice.InvoiceDate;
                    hasChanges = true;
                }

                if (existingInvoice.VatPercentage != invoice.VatPercentage)
                {
                    existingInvoice.VatPercentage = invoice.VatPercentage;
                    hasChanges = true;
                }

                if (existingInvoice.Notes != invoice.Notes)
                {
                    existingInvoice.Notes = invoice.Notes;
                    hasChanges = true;
                }

                if (existingInvoice.BankAccountId != invoice.BankAccountId)
                {
                    existingInvoice.BankAccountId = invoice.BankAccountId;
                    hasChanges = true;
                }

                if (hasChanges)
                {
                    existingInvoice.CalculateTotals();
                    await _context.SaveChangesAsync();
                    
                    // Clear cache but don't wait
                    _ = Task.Run(() => ClearInvoiceCache());
                }

                return hasChanges;
            }
            catch
            {
                // Ignore auto-save errors
                return false;
            }
        }

        #endregion

        #region Printing Support

        /// <summary>
        /// Increment print count untuk invoice
        /// </summary>
        /// <param name="id">Invoice ID</param>
        /// <returns>Updated invoice</returns>
        public async Task<Invoice> IncrementPrintCountAsync(int id)
        {
            var invoice = await _context.Invoices.FindAsync(id);
            if (invoice == null)
                throw new InvalidOperationException($"Invoice dengan ID {id} tidak ditemukan");

            invoice.IncrementPrintCount();
            await _context.SaveChangesAsync();

            // Clear related cache
            ClearInvoiceCache();

            return invoice;
        }

        #endregion

        #region Invoice Statistics

        /// <summary>
        /// Get invoice statistics
        /// </summary>
        /// <param name="companyId">Filter by company (optional)</param>
        /// <param name="dateFrom">Date range from</param>
        /// <param name="dateTo">Date range to</param>
        /// <returns>Dictionary dengan berbagai statistik</returns>
        public async Task<Dictionary<string, object>> GetInvoiceStatsAsync(int? companyId = null, DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            var cacheKey = $"invoice_stats_{companyId}_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}";
            var cached = _context.GetFromCache<Dictionary<string, object>>(cacheKey);
            if (cached != null) return cached;

            var query = _context.Invoices.AsQueryable();

            // Apply filters
            if (companyId.HasValue)
                query = query.Where(i => i.CompanyId == companyId.Value);

            if (dateFrom.HasValue)
                query = query.Where(i => i.InvoiceDate >= dateFrom.Value);

            if (dateTo.HasValue)
                query = query.Where(i => i.InvoiceDate <= dateTo.Value);

            var stats = new Dictionary<string, object>();

            // Count by status
            var statusCounts = await query
                .GroupBy(i => i.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            foreach (var statusCount in statusCounts)
            {
                stats[$"Count{statusCount.Status}"] = statusCount.Count;
            }

            // Total count
            stats["TotalCount"] = statusCounts.Sum(sc => sc.Count);

            // Revenue stats (excluding cancelled)
            var revenueQuery = query.Where(i => i.Status != Invoice.InvoiceStatus.Cancelled);

            stats["TotalRevenue"] = await revenueQuery.SumAsync(i => i.TotalAmount);
            stats["AverageInvoiceAmount"] = await revenueQuery.AverageAsync(i => (decimal?)i.TotalAmount) ?? 0;

            // Monthly breakdown (last 12 months)
            var monthlyStats = await revenueQuery
                .Where(i => i.InvoiceDate >= DateTime.Now.AddMonths(-12))
                .GroupBy(i => new { Year = i.InvoiceDate.Year, Month = i.InvoiceDate.Month })
                .Select(g => new 
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Count = g.Count(),
                    Revenue = g.Sum(i => i.TotalAmount)
                })
                .OrderBy(x => x.Year)
                .ThenBy(x => x.Month)
                .ToListAsync();

            stats["MonthlyBreakdown"] = monthlyStats;

            _context.SetCache(cacheKey, stats, 30);
            return stats;
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Bulk update invoice status
        /// </summary>
        /// <param name="invoiceIds">List of invoice IDs</param>
        /// <param name="newStatus">New status</param>
        /// <returns>Number of updated invoices</returns>
        public async Task<int> BulkUpdateStatusAsync(List<int> invoiceIds, string newStatus)
        {
            if (!Invoice.InvoiceStatus.GetAll().Contains(newStatus))
                throw new ArgumentException($"Invalid status: {newStatus}");

            var invoices = await _context.Invoices
                .Where(i => invoiceIds.Contains(i.Id))
                .ToListAsync();

            var updatedCount = 0;
            foreach (var invoice in invoices)
            {
                // Validate status transition
                var canUpdate = newStatus switch
                {
                    Invoice.InvoiceStatus.Finalized => invoice.Status == Invoice.InvoiceStatus.Draft,
                    Invoice.InvoiceStatus.Paid => invoice.Status == Invoice.InvoiceStatus.Finalized,
                    Invoice.InvoiceStatus.Cancelled => invoice.Status != Invoice.InvoiceStatus.Paid,
                    _ => false
                };

                if (canUpdate)
                {
                    invoice.Status = newStatus;
                    invoice.UpdateTimestamp();
                    updatedCount++;
                }
            }

            if (updatedCount > 0)
            {
                await _context.SaveChangesAsync();
                ClearInvoiceCache();
            }

            return updatedCount;
        }

        /// <summary>
        /// Clone invoice (create copy dengan status draft)
        /// </summary>
        /// <param name="sourceId">Source invoice ID</param>
        /// <param name="userId">User ID yang membuat clone</param>
        /// <returns>Cloned invoice</returns>
        public async Task<Invoice> CloneInvoiceAsync(int sourceId, int userId)
        {
            var sourceInvoice = await _context.Invoices
                .Include(i => i.InvoiceLines)
                .ThenInclude(il => il.JobDescription)
                .FirstOrDefaultAsync(i => i.Id == sourceId);

            if (sourceInvoice == null)
                throw new InvalidOperationException($"Source invoice dengan ID {sourceId} tidak ditemukan");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Create new invoice
                var clonedInvoice = new Invoice
                {
                    CompanyId = sourceInvoice.CompanyId,
                    InvoiceDate = DateTime.Today,
                    VatPercentage = sourceInvoice.VatPercentage,
                    Notes = sourceInvoice.Notes,
                    BankAccountId = sourceInvoice.BankAccountId,
                    CreatedBy = userId,
                    Status = Invoice.InvoiceStatus.Draft
                };

                // Generate new invoice number
                clonedInvoice.InvoiceNumber = await GenerateInvoiceNumberAsync(clonedInvoice.InvoiceDate);

                _context.Invoices.Add(clonedInvoice);
                await _context.SaveChangesAsync(); // Save to get ID

                // Clone invoice lines
                foreach (var sourceLine in sourceInvoice.InvoiceLines)
                {
                    var clonedLine = new InvoiceLine
                    {
                        InvoiceId = clonedInvoice.Id,
                        Baris = sourceLine.Baris,
                        LineOrder = sourceLine.LineOrder,
                        TkaId = sourceLine.TkaId,
                        JobDescriptionId = sourceLine.JobDescriptionId,
                        CustomJobName = sourceLine.CustomJobName,
                        CustomJobDescription = sourceLine.CustomJobDescription,
                        CustomPrice = sourceLine.CustomPrice,
                        Quantity = sourceLine.Quantity
                    };

                    clonedLine.CalculateLineTotal();
                    clonedInvoice.InvoiceLines.Add(clonedLine);
                }

                // Calculate totals
                clonedInvoice.CalculateTotals();

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Clear related cache
                ClearInvoiceCache();

                return clonedInvoice;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Clear invoice related cache
        /// </summary>
        private void ClearInvoiceCache()
        {
            _context.ClearCache("invoices");
            _context.ClearCache("invoice");
            _context.ClearCache("invoice_stats");
        }

        #endregion
    }

    #region Support Classes

    /// <summary>
    /// Simple class untuk invoice number sequence (untuk raw SQL queries)
    /// </summary>
    public class InvoiceNumberSequence
    {
        public int Id { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public int CurrentNumber { get; set; }
        public string Prefix { get; set; } = "FSN";
        public string Suffix { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    #endregion
}