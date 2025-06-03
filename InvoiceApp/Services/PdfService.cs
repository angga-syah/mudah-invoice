using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Kernel.Font;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using InvoiceApp.Models;
using InvoiceApp.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace InvoiceApp.Services
{
    /// <summary>
    /// Service untuk generate PDF invoice dengan format sesuai template VB.NET legacy
    /// </summary>
    public class PdfService
    {
        private readonly Database.AppDbContext _context;

        // Font constants
        private PdfFont _regularFont;
        private PdfFont _boldFont;
        private PdfFont _headerFont;

        public PdfService(Database.AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            InitializeFonts();
        }

        private void InitializeFonts()
        {
            try
            {
                _regularFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
                _boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
                _headerFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            }
            catch
            {
                // Fallback jika font tidak tersedia
                _regularFont = PdfFontFactory.CreateFont();
                _boldFont = PdfFontFactory.CreateFont();
                _headerFont = PdfFontFactory.CreateFont();
            }
        }

        #region Public Methods

        /// <summary>
        /// Generate PDF untuk single invoice
        /// </summary>
        /// <param name="invoiceId">Invoice ID</param>
        /// <param name="outputPath">Path output file PDF</param>
        /// <param name="showBankInfo">Tampilkan info bank (default true untuk last page only)</param>
        /// <returns>True jika berhasil</returns>
        public async Task<bool> GenerateInvoicePdfAsync(int invoiceId, string outputPath, bool showBankInfo = true)
        {
            try
            {
                var invoice = await GetInvoiceWithDetailsAsync(invoiceId);
                if (invoice == null)
                    return false;

                using var writer = new PdfWriter(outputPath);
                using var pdf = new PdfDocument(writer);
                using var document = new Document(pdf);

                await GenerateInvoicePdfContent(document, invoice, showBankInfo);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating PDF: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Generate PDF untuk multiple invoices (batch export)
        /// </summary>
        /// <param name="invoiceIds">List invoice IDs</param>
        /// <param name="outputPath">Path output file PDF</param>
        /// <param name="separateFiles">True = file terpisah per invoice, False = satu file gabungan</param>
        /// <returns>List path file yang berhasil di-generate</returns>
        public async Task<List<string>> GenerateBatchInvoicePdfAsync(List<int> invoiceIds, string outputPath, bool separateFiles = true)
        {
            var generatedFiles = new List<string>();

            try
            {
                var invoices = new List<Invoice>();
                foreach (var invoiceId in invoiceIds)
                {
                    var invoice = await GetInvoiceWithDetailsAsync(invoiceId);
                    if (invoice != null)
                        invoices.Add(invoice);
                }

                if (!invoices.Any())
                    return generatedFiles;

                if (separateFiles)
                {
                    // Generate file terpisah per invoice
                    var directory = Path.GetDirectoryName(outputPath) ?? "";
                    var baseFileName = Path.GetFileNameWithoutExtension(outputPath);
                    var extension = Path.GetExtension(outputPath);

                    foreach (var invoice in invoices)
                    {
                        var fileName = $"{baseFileName}_{invoice.InvoiceNumber.Replace("/", "_")}{extension}";
                        var filePath = Path.Combine(directory, fileName);

                        if (await GenerateInvoicePdfAsync(invoice.Id, filePath))
                        {
                            generatedFiles.Add(filePath);
                        }
                    }
                }
                else
                {
                    // Generate satu file gabungan
                    using var writer = new PdfWriter(outputPath);
                    using var pdf = new PdfDocument(writer);
                    using var document = new Document(pdf);

                    for (int i = 0; i < invoices.Count; i++)
                    {
                        var invoice = invoices[i];
                        var isLastInvoice = i == invoices.Count - 1;

                        await GenerateInvoicePdfContent(document, invoice, isLastInvoice);

                        // Add page break kecuali untuk invoice terakhir
                        if (!isLastInvoice)
                        {
                            document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
                        }
                    }

                    generatedFiles.Add(outputPath);
                }

                return generatedFiles;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating batch PDF: {ex.Message}");
                return generatedFiles;
            }
        }

        /// <summary>
        /// Generate preview PDF untuk testing (tanpa save ke file)
        /// </summary>
        /// <param name="invoiceId">Invoice ID</param>
        /// <returns>PDF byte array</returns>
        public async Task<byte[]?> GenerateInvoicePreviewAsync(int invoiceId)
        {
            try
            {
                var invoice = await GetInvoiceWithDetailsAsync(invoiceId);
                if (invoice == null)
                    return null;

                using var stream = new MemoryStream();
                using var writer = new PdfWriter(stream);
                using var pdf = new PdfDocument(writer);
                using var document = new Document(pdf);

                await GenerateInvoicePdfContent(document, invoice, true);

                return stream.ToArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating preview: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region PDF Content Generation

        /// <summary>
        /// Generate konten PDF untuk invoice
        /// </summary>
        private async Task GenerateInvoicePdfContent(Document document, Invoice invoice, bool showBankInfo)
        {
            // Get settings
            var settings = await GetPdfSettingsAsync();

            // Header Section
            await AddInvoiceHeader(document, invoice, settings);

            // Invoice Details Section
            await AddInvoiceDetails(document, invoice, settings);

            // Company Info Section
            await AddCompanyInfo(document, invoice, settings);

            // Invoice Table
            await AddInvoiceTable(document, invoice);

            // Financial Summary
            await AddFinancialSummary(document, invoice);

            // Terbilang
            await AddTerbilang(document, invoice);

            // Footer dengan bank info (jika showBankInfo = true)
            if (showBankInfo)
            {
                await AddFooterWithBank(document, invoice, settings);
            }
            else
            {
                await AddFooter(document, settings);
            }
        }

        /// <summary>
        /// Add header section (Company name + INVOICE + tagline)
        /// </summary>
        private async Task AddInvoiceHeader(Document document, Invoice invoice, PdfSettings settings)
        {
            // Company Name
            var companyNamePara = new Paragraph(settings.CompanyName)
                .SetFont(_headerFont)
                .SetFontSize(20)
                .SetTextAlignment(TextAlignment.CENTER)
                .SetMarginBottom(5);
            document.Add(companyNamePara);

            // INVOICE title
            var invoiceTitlePara = new Paragraph("INVOICE")
                .SetFont(_headerFont)
                .SetFontSize(16)
                .SetTextAlignment(TextAlignment.CENTER)
                .SetMarginBottom(5);
            document.Add(invoiceTitlePara);

            // Tagline
            if (!string.IsNullOrWhiteSpace(settings.CompanyTagline))
            {
                var taglinePara = new Paragraph(settings.CompanyTagline)
                    .SetFont(_regularFont)
                    .SetFontSize(10)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetMarginBottom(15);
                document.Add(taglinePara);
            }
        }

        /// <summary>
        /// Add invoice details section (No, Tanggal, Halaman)
        /// </summary>
        private async Task AddInvoiceDetails(Document document, Invoice invoice, PdfSettings settings)
        {
            var detailsTable = new Table(2);
            detailsTable.SetWidth(UnitValue.CreatePercentValue(100));

            // Left column: No, Tanggal, Halaman
            var leftCell = new Cell();
            leftCell.Add(new Paragraph($"No: {invoice.InvoiceNumber}").SetFont(_regularFont).SetFontSize(10));
            leftCell.Add(new Paragraph($"Tanggal: {settings.InvoicePlace}, {invoice.InvoiceDate:dd MMMM yyyy}").SetFont(_regularFont).SetFontSize(10));
            leftCell.Add(new Paragraph("Halaman: 1/1").SetFont(_regularFont).SetFontSize(10)); // TODO: Implement proper pagination
            leftCell.SetBorder(iText.Layout.Borders.Border.NO_BORDER);

            // Right column: Office info
            var rightCell = new Cell();
            rightCell.Add(new Paragraph("Kantor:").SetFont(_boldFont).SetFontSize(10));
            rightCell.Add(new Paragraph(settings.CompanyAddress).SetFont(_regularFont).SetFontSize(10));
            
            if (!string.IsNullOrWhiteSpace(settings.CompanyPhone))
            {
                rightCell.Add(new Paragraph($"Telp: {settings.CompanyPhone}").SetFont(_regularFont).SetFontSize(10));
            }
            
            if (!string.IsNullOrWhiteSpace(settings.CompanyPhone2))
            {
                rightCell.Add(new Paragraph($"      {settings.CompanyPhone2}").SetFont(_regularFont).SetFontSize(10));
            }
            
            rightCell.SetBorder(iText.Layout.Borders.Border.NO_BORDER);
            rightCell.SetTextAlignment(TextAlignment.RIGHT);

            detailsTable.AddCell(leftCell);
            detailsTable.AddCell(rightCell);

            document.Add(detailsTable);
            document.Add(new Paragraph().SetMarginBottom(10)); // Spacing
        }

        /// <summary>
        /// Add company info section (To: Company Name + Address)
        /// </summary>
        private async Task AddCompanyInfo(Document document, Invoice invoice, PdfSettings settings)
        {
            var toPara = new Paragraph("To:")
                .SetFont(_boldFont)
                .SetFontSize(10)
                .SetMarginBottom(5);
            document.Add(toPara);

            var companyPara = new Paragraph(invoice.Company.CompanyName)
                .SetFont(_boldFont)
                .SetFontSize(10)
                .SetMarginBottom(2);
            document.Add(companyPara);

            var addressPara = new Paragraph(invoice.Company.Address)
                .SetFont(_regularFont)
                .SetFontSize(10)
                .SetMarginBottom(15);
            document.Add(addressPara);
        }

        /// <summary>
        /// Add invoice table dengan line items
        /// </summary>
        private async Task AddInvoiceTable(Document document, Invoice invoice)
        {
            // Table dengan 4 kolom: No, Expatriat, Keterangan, Harga
            // Column widths berdasarkan template VB.NET: No(70), Expatriat(140), Keterangan(300), Harga(110)
            float[] columnWidths = { 70f, 140f, 300f, 110f };
            var table = new Table(columnWidths);
            table.SetWidth(UnitValue.CreatePercentValue(100));

            // Headers
            var headers = new[] { "No.", "Expatriat", "Keterangan", "Harga" };
            foreach (var header in headers)
            {
                var headerCell = new Cell()
                    .Add(new Paragraph(header).SetFont(_boldFont).SetFontSize(8))
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                    .SetHeight(42f); // 42px untuk headers sesuai template
                table.AddHeaderCell(headerCell);
            }

            // Group invoice lines by Baris
            var linesByBaris = invoice.InvoiceLines
                .OrderBy(il => il.Baris)
                .ThenBy(il => il.LineOrder)
                .GroupBy(il => il.Baris)
                .ToList();

            for (int barisIndex = 0; barisIndex < linesByBaris.Count; barisIndex++)
            {
                var barisGroup = linesByBaris[barisIndex];
                var lines = barisGroup.ToList();

                // Untuk setiap baris, combine semua line descriptions dengan satu total amount
                var tkaNames = string.Join("\n", lines.Select(l => l.TkaWorker.Nama).Distinct());
                var descriptions = new List<string>();
                decimal totalAmount = 0;

                foreach (var line in lines)
                {
                    descriptions.Add($"{line.JobName}");
                    if (!string.IsNullOrWhiteSpace(line.JobDescriptionText) && line.JobDescriptionText != line.JobName)
                    {
                        descriptions.Add($"  {line.JobDescriptionText}");
                    }
                    totalAmount += line.LineTotal;
                }

                var combinedDescription = string.Join("\n", descriptions);

                // No column
                var noCell = new Cell()
                    .Add(new Paragraph((barisIndex + 1).ToString()).SetFont(_regularFont).SetFontSize(8))
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetHeight(32f); // 32px standard row height
                table.AddCell(noCell);

                // Expatriat column
                var expatCell = new Cell()
                    .Add(new Paragraph(tkaNames).SetFont(_regularFont).SetFontSize(8))
                    .SetTextAlignment(TextAlignment.LEFT)
                    .SetHeight(32f);
                table.AddCell(expatCell);

                // Keterangan column (multi-line support)
                var keteranganCell = new Cell()
                    .Add(new Paragraph(combinedDescription).SetFont(_regularFont).SetFontSize(8))
                    .SetTextAlignment(TextAlignment.LEFT)
                    .SetHeight(32f);
                table.AddCell(keteranganCell);

                // Harga column
                var hargaCell = new Cell()
                    .Add(new Paragraph(CurrencyHelper.FormatForDisplay(totalAmount)).SetFont(_regularFont).SetFontSize(8))
                    .SetTextAlignment(TextAlignment.RIGHT)
                    .SetHeight(32f);
                table.AddCell(hargaCell);
            }

            document.Add(table);
            document.Add(new Paragraph().SetMarginBottom(10)); // Spacing
        }

        /// <summary>
        /// Add financial summary (Sub Total, PPN, Total)
        /// </summary>
        private async Task AddFinancialSummary(Document document, Invoice invoice)
        {
            // Right-aligned financial summary
            var summaryTable = new Table(2);
            summaryTable.SetWidth(UnitValue.CreatePercentValue(50));
            summaryTable.SetHorizontalAlignment(HorizontalAlignment.RIGHT);

            // Sub Total
            summaryTable.AddCell(new Cell().Add(new Paragraph("Sub Total:").SetFont(_regularFont).SetFontSize(10)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT));
            summaryTable.AddCell(new Cell().Add(new Paragraph(CurrencyHelper.FormatForDisplay(invoice.Subtotal)).SetFont(_regularFont).SetFontSize(10)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT));

            // PPN
            summaryTable.AddCell(new Cell().Add(new Paragraph($"PPN ({invoice.VatPercentage}%):").SetFont(_regularFont).SetFontSize(10)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT));
            summaryTable.AddCell(new Cell().Add(new Paragraph(CurrencyHelper.FormatForDisplay(invoice.VatAmount)).SetFont(_regularFont).SetFontSize(10)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT));

            // Total
            summaryTable.AddCell(new Cell().Add(new Paragraph("Total:").SetFont(_boldFont).SetFontSize(10)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT));
            summaryTable.AddCell(new Cell().Add(new Paragraph(CurrencyHelper.FormatForDisplay(invoice.TotalAmount)).SetFont(_boldFont).SetFontSize(10)).SetBorder(iText.Layout.Borders.Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT));

            document.Add(summaryTable);
            document.Add(new Paragraph().SetMarginBottom(10)); // Spacing
        }

        /// <summary>
        /// Add terbilang section
        /// </summary>
        private async Task AddTerbilang(Document document, Invoice invoice)
        {
            var terbilang = CurrencyHelper.ConvertToWordsForInvoice(invoice.TotalAmount);
            var terbilangPara = new Paragraph($"Terbilang: {terbilang}")
                .SetFont(_regularFont)
                .SetFontSize(10)
                .SetMarginBottom(20);
            document.Add(terbilangPara);
        }

        /// <summary>
        /// Add footer with bank information (untuk last page only)
        /// </summary>
        private async Task AddFooterWithBank(Document document, Invoice invoice, PdfSettings settings)
        {
            // Footer table dengan 3 kolom
            var footerTable = new Table(3);
            footerTable.SetWidth(UnitValue.CreatePercentValue(100));

            // Terms/Notes
            var termsCell = new Cell();
            if (!string.IsNullOrWhiteSpace(invoice.Notes))
            {
                termsCell.Add(new Paragraph(invoice.Notes).SetFont(_regularFont).SetFontSize(8));
            }
            termsCell.SetBorder(iText.Layout.Borders.Border.NO_BORDER);

            // Company name
            var companyCell = new Cell();
            companyCell.Add(new Paragraph(settings.CompanyName).SetFont(_boldFont).SetFontSize(8));
            companyCell.SetBorder(iText.Layout.Borders.Border.NO_BORDER);
            companyCell.SetTextAlignment(TextAlignment.CENTER);

            // Signatory name (placeholder)
            var signatoryCell = new Cell();
            signatoryCell.Add(new Paragraph("_________________").SetFont(_regularFont).SetFontSize(8));
            signatoryCell.Add(new Paragraph("Authorized Signature").SetFont(_regularFont).SetFontSize(8));
            signatoryCell.SetBorder(iText.Layout.Borders.Border.NO_BORDER);
            signatoryCell.SetTextAlignment(TextAlignment.RIGHT);

            footerTable.AddCell(termsCell);
            footerTable.AddCell(companyCell);
            footerTable.AddCell(signatoryCell);

            document.Add(footerTable);

            // Bank info (hanya di last page)
            if (invoice.BankAccount != null)
            {
                document.Add(new Paragraph().SetMarginBottom(10)); // Spacing

                var bankInfoPara = new Paragraph("BANK INFORMATION:")
                    .SetFont(_boldFont)
                    .SetFontSize(10)
                    .SetMarginBottom(5);
                document.Add(bankInfoPara);

                var bankDetailsPara = new Paragraph(invoice.BankAccount.FormatForInvoice())
                    .SetFont(_regularFont)
                    .SetFontSize(10);
                document.Add(bankDetailsPara);
            }
        }

        /// <summary>
        /// Add footer tanpa bank information
        /// </summary>
        private async Task AddFooter(Document document, PdfSettings settings)
        {
            var footerTable = new Table(3);
            footerTable.SetWidth(UnitValue.CreatePercentValue(100));

            // Empty left cell
            var leftCell = new Cell();
            leftCell.SetBorder(iText.Layout.Borders.Border.NO_BORDER);

            // Company name
            var companyCell = new Cell();
            companyCell.Add(new Paragraph(settings.CompanyName).SetFont(_boldFont).SetFontSize(8));
            companyCell.SetBorder(iText.Layout.Borders.Border.NO_BORDER);
            companyCell.SetTextAlignment(TextAlignment.CENTER);

            // Signatory
            var signatoryCell = new Cell();
            signatoryCell.Add(new Paragraph("_________________").SetFont(_regularFont).SetFontSize(8));
            signatoryCell.Add(new Paragraph("Authorized Signature").SetFont(_regularFont).SetFontSize(8));
            signatoryCell.SetBorder(iText.Layout.Borders.Border.NO_BORDER);
            signatoryCell.SetTextAlignment(TextAlignment.RIGHT);

            footerTable.AddCell(leftCell);
            footerTable.AddCell(companyCell);
            footerTable.AddCell(signatoryCell);

            document.Add(footerTable);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Get invoice dengan semua relasi yang diperlukan
        /// </summary>
        private async Task<Invoice?> GetInvoiceWithDetailsAsync(int invoiceId)
        {
            return await _context.Invoices
                .Include(i => i.Company)
                .Include(i => i.CreatedByUser)
                .Include(i => i.BankAccount)
                .Include(i => i.InvoiceLines.OrderBy(il => il.Baris).ThenBy(il => il.LineOrder))
                .ThenInclude(il => il.TkaWorker)
                .Include(i => i.InvoiceLines)
                .ThenInclude(il => il.JobDescription)
                .FirstOrDefaultAsync(i => i.Id == invoiceId);
        }

        /// <summary>
        /// Get PDF settings dari database
        /// </summary>
        private async Task<PdfSettings> GetPdfSettingsAsync()
        {
            var settings = new PdfSettings();

            try
            {
                var settingsDict = await _context.Settings
                    .Where(s => s.SettingKey.StartsWith("company_") || s.SettingKey.StartsWith("invoice_"))
                    .ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue);

                settings.CompanyName = settingsDict.GetValueOrDefault("company_name", "PT. FORTUNA SADA NIOGA");
                settings.CompanyTagline = settingsDict.GetValueOrDefault("company_tagline", "Spirit of Services");
                settings.CompanyAddress = settingsDict.GetValueOrDefault("company_address", "Jakarta");
                settings.CompanyPhone = settingsDict.GetValueOrDefault("company_phone", "");
                settings.CompanyPhone2 = settingsDict.GetValueOrDefault("company_phone2", "");
                settings.InvoicePlace = settingsDict.GetValueOrDefault("invoice_place", "Jakarta");
            }
            catch
            {
                // Use default values jika gagal load dari database
            }

            return settings;
        }

        #endregion
    }

    #region Support Classes

    /// <summary>
    /// Class untuk PDF settings
    /// </summary>
    public class PdfSettings
    {
        public string CompanyName { get; set; } = "PT. FORTUNA SADA NIOGA";
        public string CompanyTagline { get; set; } = "Spirit of Services";
        public string CompanyAddress { get; set; } = "Jakarta";
        public string CompanyPhone { get; set; } = "";
        public string CompanyPhone2 { get; set; } = "";
        public string InvoicePlace { get; set; } = "Jakarta";
    }

    #endregion
}