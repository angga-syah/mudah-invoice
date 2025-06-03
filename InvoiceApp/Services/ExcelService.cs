using Microsoft.EntityFrameworkCore;
using InvoiceApp.Database;
using InvoiceApp.Models;
using InvoiceApp.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace InvoiceApp.Services
{
    /// <summary>
    /// Service untuk import/export Excel dengan template support dan validation
    /// </summary>
    public class ExcelService
    {
        private readonly AppDbContext _context;
        private readonly InvoiceService _invoiceService;
        private readonly CompanyService _companyService;
        private readonly TkaService _tkaService;

        public ExcelService(AppDbContext context, InvoiceService invoiceService, CompanyService companyService, TkaService tkaService)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _invoiceService = invoiceService ?? throw new ArgumentNullException(nameof(invoiceService));
            _companyService = companyService ?? throw new ArgumentNullException(nameof(companyService));
            _tkaService = tkaService ?? throw new ArgumentNullException(nameof(tkaService));
        }

        #region Template Export Methods

        /// <summary>
        /// Export template untuk daftar TKA
        /// </summary>
        /// <param name="filePath">Path file output</param>
        /// <returns>True jika berhasil</returns>
        public async Task<bool> ExportTkaTemplateAsync(string filePath)
        {
            try
            {
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Daftar TKA");

                // Headers
                var headers = new[] { "Nama", "Passport", "Divisi", "Jenis Kelamin" };
                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(1, i + 1).Value = headers[i];
                    worksheet.Cell(1, i + 1).Style.Font.Bold = true;
                    worksheet.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightBlue;
                }

                // Sample data
                worksheet.Cell(2, 1).Value = "John Doe";
                worksheet.Cell(2, 2).Value = "A1234567";
                worksheet.Cell(2, 3).Value = "Engineering";
                worksheet.Cell(2, 4).Value = "Laki-laki";

                worksheet.Cell(3, 1).Value = "Jane Smith";
                worksheet.Cell(3, 2).Value = "B7654321";
                worksheet.Cell(3, 3).Value = "Administration";
                worksheet.Cell(3, 4).Value = "Perempuan";

                // Auto-fit columns
                worksheet.Columns().AdjustToContents();

                // Add validation untuk Jenis Kelamin
                var genderValidation = worksheet.Range("D:D").SetDataValidation();
                genderValidation.List("Laki-laki,Perempuan");

                // Add instructions sheet
                var instructionsSheet = workbook.Worksheets.Add("Instruksi");
                instructionsSheet.Cell("A1").Value = "INSTRUKSI IMPORT DAFTAR TKA";
                instructionsSheet.Cell("A1").Style.Font.Bold = true;
                instructionsSheet.Cell("A1").Style.Font.FontSize = 14;

                instructionsSheet.Cell("A3").Value = "1. Isi data TKA pada sheet 'Daftar TKA'";
                instructionsSheet.Cell("A4").Value = "2. Pastikan format passport benar (6-12 karakter alfanumerik)";
                instructionsSheet.Cell("A5").Value = "3. Jenis Kelamin harus 'Laki-laki' atau 'Perempuan'";
                instructionsSheet.Cell("A6").Value = "4. Divisi bersifat opsional";
                instructionsSheet.Cell("A7").Value = "5. Simpan file dan import melalui aplikasi";

                instructionsSheet.Columns().AdjustToContents();

                workbook.SaveAs(filePath);
                return true;
            }
            catch (Exception ex)
            {
                // Log error
                System.Diagnostics.Debug.WriteLine($"Error exporting TKA template: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Export template untuk daftar harga (job descriptions)
        /// </summary>
        /// <param name="filePath">Path file output</param>
        /// <param name="companyId">Company ID (optional, untuk filter company specific)</param>
        /// <returns>True jika berhasil</returns>
        public async Task<bool> ExportJobDescriptionTemplateAsync(string filePath, int? companyId = null)
        {
            try
            {
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Daftar Harga");

                // Headers
                var headers = new[] { "Company Name", "Job Name", "Job Description", "Price", "Sort Order" };
                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(1, i + 1).Value = headers[i];
                    worksheet.Cell(1, i + 1).Style.Font.Bold = true;
                    worksheet.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightGreen;
                }

                // Get sample data atau data existing
                var companies = await _companyService.GetCompaniesAsync(includeInactive: false);
                var rowIndex = 2;

                if (companyId.HasValue)
                {
                    // Export existing job descriptions untuk specific company
                    var company = await _companyService.GetCompanyByIdAsync(companyId.Value);
                    if (company != null)
                    {
                        var jobs = await _companyService.GetJobsByCompanyAsync(companyId.Value);
                        foreach (var job in jobs)
                        {
                            worksheet.Cell(rowIndex, 1).Value = company.CompanyName;
                            worksheet.Cell(rowIndex, 2).Value = job.JobName;
                            worksheet.Cell(rowIndex, 3).Value = job.JobDescriptionText;
                            worksheet.Cell(rowIndex, 4).Value = job.Price;
                            worksheet.Cell(rowIndex, 5).Value = job.SortOrder;
                            rowIndex++;
                        }
                    }
                }
                else
                {
                    // Sample data
                    if (companies.Companies.Any())
                    {
                        var sampleCompany = companies.Companies.First();
                        worksheet.Cell(2, 1).Value = sampleCompany.CompanyName;
                        worksheet.Cell(2, 2).Value = "Consultation Services";
                        worksheet.Cell(2, 3).Value = "Professional consultation and advisory services";
                        worksheet.Cell(2, 4).Value = 1000000;
                        worksheet.Cell(2, 5).Value = 1;

                        worksheet.Cell(3, 1).Value = sampleCompany.CompanyName;
                        worksheet.Cell(3, 2).Value = "Technical Support";
                        worksheet.Cell(3, 3).Value = "Technical support and maintenance services";
                        worksheet.Cell(3, 4).Value = 750000;
                        worksheet.Cell(3, 5).Value = 2;
                    }
                }

                // Format price column
                worksheet.Column(4).Style.NumberFormat.Format = "#,##0";

                // Auto-fit columns
                worksheet.Columns().AdjustToContents();

                // Add company validation
                if (companies.Companies.Any())
                {
                    var companyNames = string.Join(",", companies.Companies.Select(c => c.CompanyName));
                    var companyValidation = worksheet.Range("A:A").SetDataValidation();
                    companyValidation.List(companyNames);
                }

                // Add instructions sheet
                var instructionsSheet = workbook.Worksheets.Add("Instruksi");
                instructionsSheet.Cell("A1").Value = "INSTRUKSI IMPORT DAFTAR HARGA";
                instructionsSheet.Cell("A1").Style.Font.Bold = true;
                instructionsSheet.Cell("A1").Style.Font.FontSize = 14;

                instructionsSheet.Cell("A3").Value = "1. Isi data job descriptions pada sheet 'Daftar Harga'";
                instructionsSheet.Cell("A4").Value = "2. Company Name harus sesuai dengan yang ada di database";
                instructionsSheet.Cell("A5").Value = "3. Price harus berupa angka (tidak boleh negatif)";
                instructionsSheet.Cell("A6").Value = "4. Sort Order untuk mengurutkan job dalam company";
                instructionsSheet.Cell("A7").Value = "5. Simpan file dan import melalui aplikasi";

                instructionsSheet.Columns().AdjustToContents();

                workbook.SaveAs(filePath);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error exporting job description template: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Export template untuk import invoice
        /// </summary>
        /// <param name="filePath">Path file output</param>
        /// <returns>True jika berhasil</returns>
        public async Task<bool> ExportInvoiceTemplateAsync(string filePath)
        {
            try
            {
                using var workbook = new XLWorkbook();
                
                // Sheet 1: Invoice Headers
                var headerSheet = workbook.Worksheets.Add("Invoice Headers");
                var headerHeaders = new[] { "Invoice Number", "Company Name", "Company NPWP", "Invoice Date" };
                
                for (int i = 0; i < headerHeaders.Length; i++)
                {
                    headerSheet.Cell(1, i + 1).Value = headerHeaders[i];
                    headerSheet.Cell(1, i + 1).Style.Font.Bold = true;
                    headerSheet.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightYellow;
                }

                // Sample header data
                headerSheet.Cell(2, 1).Value = "FSN/24/01/001";
                headerSheet.Cell(2, 2).Value = "PT. Sample Company";
                headerSheet.Cell(2, 3).Value = "01.234.567.8-901.000";
                headerSheet.Cell(2, 4).Value = DateTime.Today;

                // Format date column
                headerSheet.Column(4).Style.DateFormat.Format = "dd/mm/yyyy";
                headerSheet.Columns().AdjustToContents();

                // Sheet 2: Invoice Lines
                var lineSheet = workbook.Worksheets.Add("Invoice Lines");
                var lineHeaders = new[] { "Invoice Number", "Baris", "TKA Name", "Job Name", "Job Description", "Price", "Quantity" };
                
                for (int i = 0; i < lineHeaders.Length; i++)
                {
                    lineSheet.Cell(1, i + 1).Value = lineHeaders[i];
                    lineSheet.Cell(1, i + 1).Style.Font.Bold = true;
                    lineSheet.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightCyan;
                }

                // Sample line data
                lineSheet.Cell(2, 1).Value = "FSN/24/01/001";
                lineSheet.Cell(2, 2).Value = 1;
                lineSheet.Cell(2, 3).Value = "John Doe";
                lineSheet.Cell(2, 4).Value = "Consultation";
                lineSheet.Cell(2, 5).Value = "Professional consultation services";
                lineSheet.Cell(2, 6).Value = 1000000;
                lineSheet.Cell(2, 7).Value = 1;

                lineSheet.Cell(3, 1).Value = "FSN/24/01/001";
                lineSheet.Cell(3, 2).Value = 1;
                lineSheet.Cell(3, 3).Value = "Jane Smith";
                lineSheet.Cell(3, 4).Value = "Technical Support";
                lineSheet.Cell(3, 5).Value = "Technical support services";
                lineSheet.Cell(3, 6).Value = 750000;
                lineSheet.Cell(3, 7).Value = 1;

                // Format columns
                lineSheet.Column(6).Style.NumberFormat.Format = "#,##0";
                lineSheet.Columns().AdjustToContents();

                // Instructions sheet
                var instructionsSheet = workbook.Worksheets.Add("Instruksi");
                instructionsSheet.Cell("A1").Value = "INSTRUKSI IMPORT INVOICE";
                instructionsSheet.Cell("A1").Style.Font.Bold = true;
                instructionsSheet.Cell("A1").Style.Font.FontSize = 14;

                instructionsSheet.Cell("A3").Value = "FORMAT IMPORT INVOICE:";
                instructionsSheet.Cell("A4").Value = "- Sheet 'Invoice Headers': Data header invoice";
                instructionsSheet.Cell("A5").Value = "- Sheet 'Invoice Lines': Data line items invoice";
                instructionsSheet.Cell("A7").Value = "ATURAN IMPORT:";
                instructionsSheet.Cell("A8").Value = "1. Invoice Number di kedua sheet harus sama";
                instructionsSheet.Cell("A9").Value = "2. Company Name dan NPWP harus sesuai dengan data di sistem";
                instructionsSheet.Cell("A10").Value = "3. TKA Name harus ada di database";
                instructionsSheet.Cell("A11").Value = "4. Job Name harus sesuai dengan company yang dipilih";
                instructionsSheet.Cell("A12").Value = "5. Baris = nomor urut untuk mengelompokkan line items";
                instructionsSheet.Cell("A13").Value = "6. Price dan Quantity harus berupa angka";

                instructionsSheet.Columns().AdjustToContents();

                workbook.SaveAs(filePath);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error exporting invoice template: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Data Export Methods

        /// <summary>
        /// Export invoices ke Excel dengan 2 sheets (headers + lines)
        /// </summary>
        /// <param name="filePath">Path file output</param>
        /// <param name="invoiceIds">List invoice IDs untuk export (optional, jika null export semua)</param>
        /// <param name="dateFrom">Filter tanggal dari</param>
        /// <param name="dateTo">Filter tanggal sampai</param>
        /// <param name="companyId">Filter company</param>
        /// <returns>Export result</returns>
        public async Task<ExportResult> ExportInvoicesToExcelAsync(string filePath, List<int>? invoiceIds = null, 
            DateTime? dateFrom = null, DateTime? dateTo = null, int? companyId = null)
        {
            var result = new ExportResult();
            
            try
            {
                // Get invoices
                var query = _context.Invoices.AsNoTracking()
                    .Include(i => i.Company)
                    .Include(i => i.InvoiceLines)
                    .ThenInclude(il => il.TkaWorker)
                    .Include(i => i.InvoiceLines)
                    .ThenInclude(il => il.JobDescription)
                    .AsQueryable();

                // Apply filters
                if (invoiceIds != null && invoiceIds.Any())
                    query = query.Where(i => invoiceIds.Contains(i.Id));

                if (dateFrom.HasValue)
                    query = query.Where(i => i.InvoiceDate >= dateFrom.Value);

                if (dateTo.HasValue)
                    query = query.Where(i => i.InvoiceDate <= dateTo.Value);

                if (companyId.HasValue)
                    query = query.Where(i => i.CompanyId == companyId.Value);

                var invoices = await query.OrderBy(i => i.InvoiceDate).ToListAsync();

                if (!invoices.Any())
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = "Tidak ada data invoice untuk di-export";
                    return result;
                }

                using var workbook = new XLWorkbook();

                // Sheet 1: Invoice Headers
                var headerSheet = workbook.Worksheets.Add("Invoice Headers");
                var headerHeaders = new[] { "Invoice Number", "Company Name", "Company NPWP", "Invoice Date", 
                    "Subtotal", "VAT Percentage", "VAT Amount", "Total Amount", "Status", "Created By" };

                for (int i = 0; i < headerHeaders.Length; i++)
                {
                    headerSheet.Cell(1, i + 1).Value = headerHeaders[i];
                    headerSheet.Cell(1, i + 1).Style.Font.Bold = true;
                    headerSheet.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightBlue;
                }

                int headerRow = 2;
                foreach (var invoice in invoices)
                {
                    headerSheet.Cell(headerRow, 1).Value = invoice.InvoiceNumber;
                    headerSheet.Cell(headerRow, 2).Value = invoice.Company.CompanyName;
                    headerSheet.Cell(headerRow, 3).Value = invoice.Company.Npwp;
                    headerSheet.Cell(headerRow, 4).Value = invoice.InvoiceDate;
                    headerSheet.Cell(headerRow, 5).Value = (double)invoice.Subtotal;
                    headerSheet.Cell(headerRow, 6).Value = (double)invoice.VatPercentage;
                    headerSheet.Cell(headerRow, 7).Value = (double)invoice.VatAmount;
                    headerSheet.Cell(headerRow, 8).Value = (double)invoice.TotalAmount;
                    headerSheet.Cell(headerRow, 9).Value = invoice.StatusDisplay;
                    headerSheet.Cell(headerRow, 10).Value = invoice.CreatedByUser?.FullName ?? "";
                    headerRow++;
                }

                // Format currency columns
                headerSheet.Columns("E:H").Style.NumberFormat.Format = "#,##0";
                headerSheet.Column("D").Style.DateFormat.Format = "dd/mm/yyyy";
                headerSheet.Columns().AdjustToContents();

                // Sheet 2: Invoice Lines
                var lineSheet = workbook.Worksheets.Add("Invoice Lines");
                var lineHeaders = new[] { "Invoice Number", "Baris", "TKA Name", "Job Name", "Job Description", 
                    "Unit Price", "Quantity", "Line Total" };

                for (int i = 0; i < lineHeaders.Length; i++)
                {
                    lineSheet.Cell(1, i + 1).Value = lineHeaders[i];
                    lineSheet.Cell(1, i + 1).Style.Font.Bold = true;
                    lineSheet.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightGreen;
                }

                int lineRow = 2;
                foreach (var invoice in invoices)
                {
                    foreach (var line in invoice.InvoiceLines.OrderBy(il => il.Baris).ThenBy(il => il.LineOrder))
                    {
                        lineSheet.Cell(lineRow, 1).Value = invoice.InvoiceNumber;
                        lineSheet.Cell(lineRow, 2).Value = line.Baris;
                        lineSheet.Cell(lineRow, 3).Value = line.TkaWorker.Nama;
                        lineSheet.Cell(lineRow, 4).Value = line.JobName;
                        lineSheet.Cell(lineRow, 5).Value = line.JobDescriptionText;
                        lineSheet.Cell(lineRow, 6).Value = (double)line.UnitPrice;
                        lineSheet.Cell(lineRow, 7).Value = line.Quantity;
                        lineSheet.Cell(lineRow, 8).Value = (double)line.LineTotal;
                        lineRow++;
                    }
                }

                // Format currency columns
                lineSheet.Columns("F:H").Style.NumberFormat.Format = "#,##0";
                lineSheet.Columns().AdjustToContents();

                workbook.SaveAs(filePath);

                result.IsSuccess = true;
                result.ProcessedCount = invoices.Count;
                result.SuccessCount = invoices.Count;
                result.Message = $"Berhasil export {invoices.Count} invoice";

                return result;
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Export TKA workers ke Excel
        /// </summary>
        /// <param name="filePath">Path file output</param>
        /// <param name="includeInactive">Include inactive TKA</param>
        /// <param name="includeFamilyMembers">Include family members</param>
        /// <returns>Export result</returns>
        public async Task<ExportResult> ExportTkaWorkersToExcelAsync(string filePath, bool includeInactive = false, bool includeFamilyMembers = false)
        {
            var result = new ExportResult();

            try
            {
                var tkaWorkers = await _context.TkaWorkers.AsNoTracking()
                    .Include(t => t.FamilyMembers.Where(f => includeInactive || f.IsActive))
                    .Where(t => includeInactive || t.IsActive)
                    .OrderBy(t => t.Nama)
                    .ToListAsync();

                using var workbook = new XLWorkbook();

                // Sheet 1: TKA Workers
                var tkaSheet = workbook.Worksheets.Add("TKA Workers");
                var tkaHeaders = new[] { "Nama", "Passport", "Divisi", "Jenis Kelamin", "Status", "Created Date" };

                for (int i = 0; i < tkaHeaders.Length; i++)
                {
                    tkaSheet.Cell(1, i + 1).Value = tkaHeaders[i];
                    tkaSheet.Cell(1, i + 1).Style.Font.Bold = true;
                    tkaSheet.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightBlue;
                }

                int tkaRow = 2;
                foreach (var tka in tkaWorkers)
                {
                    tkaSheet.Cell(tkaRow, 1).Value = tka.Nama;
                    tkaSheet.Cell(tkaRow, 2).Value = tka.Passport;
                    tkaSheet.Cell(tkaRow, 3).Value = tka.Divisi ?? "";
                    tkaSheet.Cell(tkaRow, 4).Value = tka.JenisKelamin;
                    tkaSheet.Cell(tkaRow, 5).Value = tka.IsActive ? "Active" : "Inactive";
                    tkaSheet.Cell(tkaRow, 6).Value = tka.CreatedAt;
                    tkaRow++;
                }

                tkaSheet.Column("F").Style.DateFormat.Format = "dd/mm/yyyy";
                tkaSheet.Columns().AdjustToContents();

                // Sheet 2: Family Members (jika diminta)
                if (includeFamilyMembers)
                {
                    var familySheet = workbook.Worksheets.Add("Family Members");
                    var familyHeaders = new[] { "TKA Name", "TKA Passport", "Family Name", "Family Passport", 
                        "Jenis Kelamin", "Relationship", "Status" };

                    for (int i = 0; i < familyHeaders.Length; i++)
                    {
                        familySheet.Cell(1, i + 1).Value = familyHeaders[i];
                        familySheet.Cell(1, i + 1).Style.Font.Bold = true;
                        familySheet.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightGreen;
                    }

                    int familyRow = 2;
                    foreach (var tka in tkaWorkers)
                    {
                        foreach (var family in tka.FamilyMembers)
                        {
                            familySheet.Cell(familyRow, 1).Value = tka.Nama;
                            familySheet.Cell(familyRow, 2).Value = tka.Passport;
                            familySheet.Cell(familyRow, 3).Value = family.Nama;
                            familySheet.Cell(familyRow, 4).Value = family.Passport;
                            familySheet.Cell(familyRow, 5).Value = family.JenisKelamin;
                            familySheet.Cell(familyRow, 6).Value = family.GetRelationshipDisplay();
                            familySheet.Cell(familyRow, 7).Value = family.IsActive ? "Active" : "Inactive";
                            familyRow++;
                        }
                    }

                    familySheet.Columns().AdjustToContents();
                }

                workbook.SaveAs(filePath);

                result.IsSuccess = true;
                result.ProcessedCount = tkaWorkers.Count;
                result.SuccessCount = tkaWorkers.Count;
                result.Message = $"Berhasil export {tkaWorkers.Count} TKA workers";

                return result;
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        #endregion

        #region Import Methods

        /// <summary>
        /// Import TKA workers dari Excel file
        /// </summary>
        /// <param name="filePath">Path file Excel</param>
        /// <param name="skipDuplicates">Skip duplicate passport numbers</param>
        /// <returns>Import result</returns>
        public async Task<ImportResult> ImportTkaWorkersFromExcelAsync(string filePath, bool skipDuplicates = true)
        {
            var result = new ImportResult();

            try
            {
                using var workbook = new XLWorkbook(filePath);
                var worksheet = workbook.Worksheet("Daftar TKA") ?? workbook.Worksheets.First();

                var tkaWorkers = new List<TkaWorker>();
                var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

                for (int row = 2; row <= lastRow; row++) // Skip header row
                {
                    try
                    {
                        var nama = worksheet.Cell(row, 1).GetString().Trim();
                        var passport = worksheet.Cell(row, 2).GetString().Trim();
                        var divisi = worksheet.Cell(row, 3).GetString().Trim();
                        var jenisKelamin = worksheet.Cell(row, 4).GetString().Trim();

                        // Skip empty rows
                        if (string.IsNullOrWhiteSpace(nama) && string.IsNullOrWhiteSpace(passport))
                            continue;

                        // Validate required fields
                        if (string.IsNullOrWhiteSpace(nama))
                        {
                            result.Errors.Add($"Row {row}: Nama tidak boleh kosong");
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(passport))
                        {
                            result.Errors.Add($"Row {row}: Passport tidak boleh kosong");
                            continue;
                        }

                        // Validate gender
                        if (string.IsNullOrWhiteSpace(jenisKelamin) || 
                            !TkaWorker.GenderTypes.GetAll().Contains(jenisKelamin))
                        {
                            jenisKelamin = TkaWorker.GenderTypes.Male; // Default
                        }

                        var tkaWorker = new TkaWorker
                        {
                            Nama = ValidationHelper.ToProperCase(nama),
                            Passport = passport.ToUpper(),
                            Divisi = string.IsNullOrWhiteSpace(divisi) ? null : ValidationHelper.ToProperCase(divisi),
                            JenisKelamin = jenisKelamin,
                            IsActive = true
                        };

                        tkaWorkers.Add(tkaWorker);
                        result.ProcessedCount++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Row {row}: {ex.Message}");
                    }
                }

                // Bulk import using TkaService
                if (tkaWorkers.Any())
                {
                    var importResult = await _tkaService.BulkImportTkaWorkersAsync(tkaWorkers, skipDuplicates);
                    result.SuccessCount = importResult.SuccessCount;
                    result.ErrorCount = importResult.ErrorCount;
                    result.SkippedCount = importResult.SkippedCount;
                    result.Errors.AddRange(importResult.Errors);
                }

                result.IsSuccess = result.SuccessCount > 0;
                result.Message = $"Import selesai: {result.SuccessCount} berhasil, {result.ErrorCount} error, {result.SkippedCount} dilewati";

                return result;
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Import invoices dari Excel file dengan 2 sheets
        /// </summary>
        /// <param name="filePath">Path file Excel</param>
        /// <param name="userId">User ID yang melakukan import</param>
        /// <param name="batchId">Batch ID untuk tracking</param>
        /// <returns>Import result</returns>
        public async Task<ImportResult> ImportInvoicesFromExcelAsync(string filePath, int userId, string? batchId = null)
        {
            var result = new ImportResult();
            batchId ??= Guid.NewGuid().ToString("N")[..8];

            try
            {
                using var workbook = new XLWorkbook(filePath);
                
                // Validate required sheets
                var headerSheet = workbook.Worksheets.FirstOrDefault(w => w.Name.Contains("Header"));
                var lineSheet = workbook.Worksheets.FirstOrDefault(w => w.Name.Contains("Line"));

                if (headerSheet == null || lineSheet == null)
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = "File harus memiliki sheet 'Invoice Headers' dan 'Invoice Lines'";
                    return result;
                }

                // Read headers
                var invoiceHeaders = await ReadInvoiceHeadersFromSheet(headerSheet);
                var invoiceLines = await ReadInvoiceLinesFromSheet(lineSheet);

                // Group lines by invoice number
                var linesByInvoice = invoiceLines.GroupBy(l => l.InvoiceNumber).ToDictionary(g => g.Key, g => g.ToList());

                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    foreach (var header in invoiceHeaders)
                    {
                        try
                        {
                            // Find company
                            var company = await _context.Companies
                                .FirstOrDefaultAsync(c => c.CompanyName == header.CompanyName && c.IsActive);

                            if (company == null)
                            {
                                result.Errors.Add($"Invoice {header.InvoiceNumber}: Company '{header.CompanyName}' tidak ditemukan");
                                result.ErrorCount++;
                                continue;
                            }

                            // Create invoice
                            var invoice = new Invoice
                            {
                                InvoiceNumber = header.InvoiceNumber,
                                CompanyId = company.Id,
                                InvoiceDate = header.InvoiceDate,
                                CreatedBy = userId,
                                ImportedFrom = Path.GetFileName(filePath),
                                ImportBatchId = batchId,
                                Status = Invoice.InvoiceStatus.Draft
                            };

                            _context.Invoices.Add(invoice);
                            await _context.SaveChangesAsync(); // Save to get ID

                            // Add lines
                            if (linesByInvoice.TryGetValue(header.InvoiceNumber, out var lines))
                            {
                                foreach (var lineData in lines)
                                {
                                    // Find TKA
                                    var tka = await _context.TkaWorkers
                                        .FirstOrDefaultAsync(t => t.Nama == lineData.TkaName && t.IsActive);

                                    if (tka == null)
                                    {
                                        result.Errors.Add($"Invoice {header.InvoiceNumber}: TKA '{lineData.TkaName}' tidak ditemukan");
                                        continue;
                                    }

                                    // Find or create job description
                                    var job = await _context.JobDescriptions
                                        .FirstOrDefaultAsync(j => j.CompanyId == company.Id && 
                                                                 j.JobName == lineData.JobName && j.IsActive);

                                    if (job == null)
                                    {
                                        // Create temporary job description
                                        job = new JobDescription
                                        {
                                            CompanyId = company.Id,
                                            JobName = lineData.JobName,
                                            JobDescriptionText = lineData.JobDescription,
                                            Price = lineData.Price,
                                            IsActive = true
                                        };
                                        _context.JobDescriptions.Add(job);
                                        await _context.SaveChangesAsync();
                                    }

                                    // Create invoice line
                                    var invoiceLine = new InvoiceLine
                                    {
                                        InvoiceId = invoice.Id,
                                        Baris = lineData.Baris,
                                        LineOrder = 1, // Will be auto-adjusted
                                        TkaId = tka.Id,
                                        JobDescriptionId = job.Id,
                                        Quantity = lineData.Quantity,
                                        UnitPrice = lineData.Price,
                                        LineTotal = lineData.Price * lineData.Quantity
                                    };

                                    invoice.InvoiceLines.Add(invoiceLine);
                                }
                            }

                            // Calculate totals
                            invoice.CalculateTotals();
                            result.SuccessCount++;
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add($"Invoice {header.InvoiceNumber}: {ex.Message}");
                            result.ErrorCount++;
                        }

                        result.ProcessedCount++;
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    result.IsSuccess = result.SuccessCount > 0;
                    result.Message = $"Import selesai: {result.SuccessCount} invoice berhasil, {result.ErrorCount} error";

                    return result;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        #endregion

        #region Helper Methods

        private async Task<List<InvoiceHeaderData>> ReadInvoiceHeadersFromSheet(IXLWorksheet sheet)
        {
            var headers = new List<InvoiceHeaderData>();
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;

            for (int row = 2; row <= lastRow; row++)
            {
                var invoiceNumber = sheet.Cell(row, 1).GetString();
                if (string.IsNullOrWhiteSpace(invoiceNumber)) continue;

                headers.Add(new InvoiceHeaderData
                {
                    InvoiceNumber = invoiceNumber.Trim(),
                    CompanyName = sheet.Cell(row, 2).GetString().Trim(),
                    CompanyNpwp = sheet.Cell(row, 3).GetString().Trim(),
                    InvoiceDate = sheet.Cell(row, 4).GetDateTime()
                });
            }

            return headers;
        }

        private async Task<List<InvoiceLineData>> ReadInvoiceLinesFromSheet(IXLWorksheet sheet)
        {
            var lines = new List<InvoiceLineData>();
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;

            for (int row = 2; row <= lastRow; row++)
            {
                var invoiceNumber = sheet.Cell(row, 1).GetString();
                if (string.IsNullOrWhiteSpace(invoiceNumber)) continue;

                lines.Add(new InvoiceLineData
                {
                    InvoiceNumber = invoiceNumber.Trim(),
                    Baris = sheet.Cell(row, 2).GetValue<int>(),
                    TkaName = sheet.Cell(row, 3).GetString().Trim(),
                    JobName = sheet.Cell(row, 4).GetString().Trim(),
                    JobDescription = sheet.Cell(row, 5).GetString().Trim(),
                    Price = sheet.Cell(row, 6).GetValue<decimal>(),
                    Quantity = Math.Max(1, sheet.Cell(row, 7).GetValue<int>())
                });
            }

            return lines;
        }

        #endregion
    }

    #region Support Classes

    public class ExportResult
    {
        public bool IsSuccess { get; set; }
        public int ProcessedCount { get; set; }
        public int SuccessCount { get; set; }
        public string Message { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }

    public class ImportResult
    {
        public bool IsSuccess { get; set; }
        public int ProcessedCount { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public int SkippedCount { get; set; }
        public List<string> Errors { get; set; } = new();
        public string Message { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }

    internal class InvoiceHeaderData
    {
        public string InvoiceNumber { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string CompanyNpwp { get; set; } = "";
        public DateTime InvoiceDate { get; set; }
    }

    internal class InvoiceLineData
    {
        public string InvoiceNumber { get; set; } = "";
        public int Baris { get; set; }
        public string TkaName { get; set; } = "";
        public string JobName { get; set; } = "";
        public string JobDescription { get; set; } = "";
        public decimal Price { get; set; }
        public int Quantity { get; set; } = 1;
    }

    #endregion
}