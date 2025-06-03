using System;
using System.Collections.Generic;
using System.Linq;
using System.Printing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Xps;
using Microsoft.Win32;
using InvoiceApp.Models;
using InvoiceApp.Helpers;
using InvoiceApp.Database;
using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Services
{
    /// <summary>
    /// Service untuk printing invoice dengan support preview dan page selection
    /// </summary>
    public class PrintService
    {
        private readonly AppDbContext _context;
        private readonly PdfService _pdfService;

        public PrintService(AppDbContext context, PdfService pdfService)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _pdfService = pdfService ?? throw new ArgumentNullException(nameof(pdfService));
        }

        #region Public Print Methods

        /// <summary>
        /// Print invoice dengan dialog preview
        /// </summary>
        /// <param name="invoiceId">Invoice ID</param>
        /// <param name="showPreview">Show print preview dialog</param>
        /// <param name="selectPages">Allow page selection</param>
        /// <returns>True jika berhasil print</returns>
        public async Task<bool> PrintInvoiceAsync(int invoiceId, bool showPreview = true, bool selectPages = false)
        {
            try
            {
                var invoice = await GetInvoiceForPrintAsync(invoiceId);
                if (invoice == null)
                {
                    MessageHelper.ShowError("Invoice tidak ditemukan atau tidak dapat di-print");
                    return false;
                }

                // Check if invoice can be printed
                if (!invoice.CanPrint)
                {
                    MessageHelper.ShowError("Hanya invoice dengan status 'Finalized' yang dapat di-print");
                    return false;
                }

                // Create print document
                var printDocument = await CreatePrintDocumentAsync(invoice);
                if (printDocument == null)
                    return false;

                // Show print dialog
                var printDialog = new PrintDialog();
                
                if (showPreview)
                {
                    // Show custom print preview dialog
                    var previewResult = ShowPrintPreview(printDocument, invoice);
                    if (!previewResult.HasValue || !previewResult.Value)
                        return false;
                }

                // Print the document
                if (printDialog.ShowDialog() == true)
                {
                    printDialog.PrintDocument(printDocument.DocumentPaginator, $"Invoice {invoice.InvoiceNumber}");
                    
                    // Update print count
                    await UpdatePrintCountAsync(invoiceId);
                    
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error printing invoice: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Print multiple invoices (batch print)
        /// </summary>
        /// <param name="invoiceIds">List of invoice IDs</param>
        /// <param name="separateJobs">Print as separate print jobs</param>
        /// <returns>Number of successfully printed invoices</returns>
        public async Task<int> PrintBatchInvoicesAsync(List<int> invoiceIds, bool separateJobs = true)
        {
            var successCount = 0;

            try
            {
                var invoices = new List<Invoice>();
                foreach (var invoiceId in invoiceIds)
                {
                    var invoice = await GetInvoiceForPrintAsync(invoiceId);
                    if (invoice?.CanPrint == true)
                        invoices.Add(invoice);
                }

                if (!invoices.Any())
                {
                    MessageHelper.ShowWarning("Tidak ada invoice yang valid untuk di-print");
                    return 0;
                }

                var printDialog = new PrintDialog();
                if (printDialog.ShowDialog() != true)
                    return 0;

                if (separateJobs)
                {
                    // Print each invoice as separate job
                    foreach (var invoice in invoices)
                    {
                        try
                        {
                            var printDocument = await CreatePrintDocumentAsync(invoice);
                            if (printDocument != null)
                            {
                                printDialog.PrintDocument(printDocument.DocumentPaginator, $"Invoice {invoice.InvoiceNumber}");
                                await UpdatePrintCountAsync(invoice.Id);
                                successCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageHelper.ShowWarning($"Failed to print invoice {invoice.InvoiceNumber}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    // Combine all invoices in one print job
                    var combinedDocument = await CreateCombinedPrintDocumentAsync(invoices);
                    if (combinedDocument != null)
                    {
                        printDialog.PrintDocument(combinedDocument.DocumentPaginator, "Batch Invoice Print");
                        
                        // Update print count for all invoices
                        foreach (var invoice in invoices)
                        {
                            await UpdatePrintCountAsync(invoice.Id);
                        }
                        
                        successCount = invoices.Count;
                    }
                }

                if (successCount > 0)
                {
                    MessageHelper.ShowSuccess($"Berhasil print {successCount} invoice");
                }

                return successCount;
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error in batch printing: {ex.Message}");
                return successCount;
            }
        }

        /// <summary>
        /// Export to PDF and print (alternative printing method)
        /// </summary>
        /// <param name="invoiceId">Invoice ID</param>
        /// <param name="openAfterExport">Open PDF after export</param>
        /// <returns>PDF file path jika berhasil</returns>
        public async Task<string?> PrintToPdfAsync(int invoiceId, bool openAfterExport = true)
        {
            try
            {
                var invoice = await GetInvoiceForPrintAsync(invoiceId);
                if (invoice == null)
                    return null;

                // Generate temporary PDF file
                var tempPath = System.IO.Path.GetTempPath();
                var fileName = $"Invoice_{invoice.InvoiceNumber.Replace("/", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                var filePath = System.IO.Path.Combine(tempPath, fileName);

                // Generate PDF
                var success = await _pdfService.GenerateInvoicePdfAsync(invoiceId, filePath, true);
                if (!success)
                    return null;

                // Update print count
                await UpdatePrintCountAsync(invoiceId);

                // Open PDF if requested
                if (openAfterExport && System.IO.File.Exists(filePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }

                return filePath;
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error exporting to PDF: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Print Document Creation

        /// <summary>
        /// Create FlowDocument untuk printing single invoice
        /// </summary>
        private async Task<FlowDocument?> CreatePrintDocumentAsync(Invoice invoice)
        {
            try
            {
                var document = new FlowDocument();
                document.PageWidth = 8.5 * 96; // 8.5 inches * 96 DPI
                document.PageHeight = 11 * 96; // 11 inches * 96 DPI
                document.PagePadding = new Thickness(48); // 0.5 inch margins
                document.ColumnWidth = double.PositiveInfinity;
                document.FontFamily = new FontFamily("Arial");
                document.FontSize = 12;

                // Get settings
                var settings = await GetPrintSettingsAsync();

                // Add content
                await AddInvoiceContentToDocument(document, invoice, settings);

                return document;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating print document: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Create combined FlowDocument untuk batch printing
        /// </summary>
        private async Task<FlowDocument?> CreateCombinedPrintDocumentAsync(List<Invoice> invoices)
        {
            try
            {
                var document = new FlowDocument();
                document.PageWidth = 8.5 * 96;
                document.PageHeight = 11 * 96;
                document.PagePadding = new Thickness(48);
                document.ColumnWidth = double.PositiveInfinity;
                document.FontFamily = new FontFamily("Arial");
                document.FontSize = 12;

                var settings = await GetPrintSettingsAsync();

                for (int i = 0; i < invoices.Count; i++)
                {
                    var invoice = invoices[i];
                    await AddInvoiceContentToDocument(document, invoice, settings);

                    // Add page break except for last invoice
                    if (i < invoices.Count - 1)
                    {
                        document.Blocks.Add(new BlockUIContainer(new PageBreak()));
                    }
                }

                return document;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating combined print document: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Add invoice content to FlowDocument
        /// </summary>
        private async Task AddInvoiceContentToDocument(FlowDocument document, Invoice invoice, PrintSettings settings)
        {
            // Header
            AddDocumentHeader(document, invoice, settings);

            // Invoice details
            AddInvoiceDetails(document, invoice, settings);

            // Company info
            AddCompanyInfo(document, invoice);

            // Invoice table
            AddInvoiceTable(document, invoice);

            // Financial summary
            AddFinancialSummary(document, invoice);

            // Terbilang
            AddTerbilang(document, invoice);

            // Footer with bank info (jika ini halaman terakhir dari batch)
            AddFooter(document, invoice, settings);
        }

        #endregion

        #region Document Content Methods

        private void AddDocumentHeader(FlowDocument document, Invoice invoice, PrintSettings settings)
        {
            var headerPara = new Paragraph
            {
                TextAlignment = TextAlignment.Center,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            headerPara.Inlines.Add(new Run(settings.CompanyName));
            document.Blocks.Add(headerPara);

            var invoiceTitlePara = new Paragraph
            {
                TextAlignment = TextAlignment.Center,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5)
            };
            invoiceTitlePara.Inlines.Add(new Run("INVOICE"));
            document.Blocks.Add(invoiceTitlePara);

            if (!string.IsNullOrWhiteSpace(settings.CompanyTagline))
            {
                var taglinePara = new Paragraph
                {
                    TextAlignment = TextAlignment.Center,
                    FontSize = 10,
                    Margin = new Thickness(0, 0, 0, 20)
                };
                taglinePara.Inlines.Add(new Run(settings.CompanyTagline));
                document.Blocks.Add(taglinePara);
            }
        }

        private void AddInvoiceDetails(FlowDocument document, Invoice invoice, PrintSettings settings)
        {
            var table = new Table();
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

            var rowGroup = new TableRowGroup();
            var row = new TableRow();

            // Left cell - Invoice details
            var leftCell = new TableCell();
            leftCell.Blocks.Add(new Paragraph(new Run($"No: {invoice.InvoiceNumber}")) { FontSize = 10 });
            leftCell.Blocks.Add(new Paragraph(new Run($"Tanggal: {settings.InvoicePlace}, {invoice.InvoiceDate:dd MMMM yyyy}")) { FontSize = 10 });
            leftCell.Blocks.Add(new Paragraph(new Run("Halaman: 1/1")) { FontSize = 10 });

            // Right cell - Office info
            var rightCell = new TableCell();
            rightCell.Blocks.Add(new Paragraph(new Run("Kantor:")) { FontSize = 10, FontWeight = FontWeights.Bold });
            rightCell.Blocks.Add(new Paragraph(new Run(settings.CompanyAddress)) { FontSize = 10 });
            if (!string.IsNullOrWhiteSpace(settings.CompanyPhone))
            {
                rightCell.Blocks.Add(new Paragraph(new Run($"Telp: {settings.CompanyPhone}")) { FontSize = 10 });
            }

            row.Cells.Add(leftCell);
            row.Cells.Add(rightCell);
            rowGroup.Rows.Add(row);
            table.RowGroups.Add(rowGroup);

            document.Blocks.Add(table);
            document.Blocks.Add(new Paragraph { Margin = new Thickness(0, 0, 0, 10) }); // Spacing
        }

        private void AddCompanyInfo(FlowDocument document, Invoice invoice)
        {
            var toPara = new Paragraph
            {
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5)
            };
            toPara.Inlines.Add(new Run("To:"));
            document.Blocks.Add(toPara);

            var companyPara = new Paragraph
            {
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 2)
            };
            companyPara.Inlines.Add(new Run(invoice.Company.CompanyName));
            document.Blocks.Add(companyPara);

            var addressPara = new Paragraph
            {
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 15)
            };
            addressPara.Inlines.Add(new Run(invoice.Company.Address));
            document.Blocks.Add(addressPara);
        }

        private void AddInvoiceTable(FlowDocument document, Invoice invoice)
        {
            var table = new Table();
            table.CellSpacing = 0;
            table.BorderBrush = Brushes.Black;
            table.BorderThickness = new Thickness(1);

            // Columns
            table.Columns.Add(new TableColumn { Width = new GridLength(70, GridUnitType.Pixel) });
            table.Columns.Add(new TableColumn { Width = new GridLength(140, GridUnitType.Pixel) });
            table.Columns.Add(new TableColumn { Width = new GridLength(300, GridUnitType.Pixel) });
            table.Columns.Add(new TableColumn { Width = new GridLength(110, GridUnitType.Pixel) });

            // Header row
            var headerRowGroup = new TableRowGroup();
            var headerRow = new TableRow();
            
            var headers = new[] { "No.", "Expatriat", "Keterangan", "Harga" };
            foreach (var header in headers)
            {
                var cell = new TableCell(new Paragraph(new Run(header)))
                {
                    Background = Brushes.LightGray,
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(4),
                    TextAlignment = TextAlignment.Center
                };
                cell.Blocks.First().FontWeight = FontWeights.Bold;
                cell.Blocks.First().FontSize = 8;
                headerRow.Cells.Add(cell);
            }
            
            headerRowGroup.Rows.Add(headerRow);
            table.RowGroups.Add(headerRowGroup);

            // Data rows
            var dataRowGroup = new TableRowGroup();
            var linesByBaris = invoice.InvoiceLines
                .OrderBy(il => il.Baris)
                .ThenBy(il => il.LineOrder)
                .GroupBy(il => il.Baris)
                .ToList();

            for (int i = 0; i < linesByBaris.Count; i++)
            {
                var barisGroup = linesByBaris[i];
                var lines = barisGroup.ToList();
                var row = new TableRow();

                // No
                var noCell = new TableCell(new Paragraph(new Run((i + 1).ToString())))
                {
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(4),
                    TextAlignment = TextAlignment.Center
                };
                noCell.Blocks.First().FontSize = 8;

                // Expatriat
                var tkaNames = string.Join("\n", lines.Select(l => l.TkaWorker.Nama).Distinct());
                var expatCell = new TableCell(new Paragraph(new Run(tkaNames)))
                {
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(4)
                };
                expatCell.Blocks.First().FontSize = 8;

                // Keterangan
                var descriptions = new List<string>();
                decimal totalAmount = 0;
                foreach (var line in lines)
                {
                    descriptions.Add(line.JobName);
                    if (!string.IsNullOrWhiteSpace(line.JobDescriptionText) && line.JobDescriptionText != line.JobName)
                    {
                        descriptions.Add($"  {line.JobDescriptionText}");
                    }
                    totalAmount += line.LineTotal;
                }
                var combinedDescription = string.Join("\n", descriptions);
                var keteranganCell = new TableCell(new Paragraph(new Run(combinedDescription)))
                {
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(4)
                };
                keteranganCell.Blocks.First().FontSize = 8;

                // Harga
                var hargaCell = new TableCell(new Paragraph(new Run(CurrencyHelper.FormatForDisplay(totalAmount))))
                {
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(4),
                    TextAlignment = TextAlignment.Right
                };
                hargaCell.Blocks.First().FontSize = 8;

                row.Cells.Add(noCell);
                row.Cells.Add(expatCell);
                row.Cells.Add(keteranganCell);
                row.Cells.Add(hargaCell);

                dataRowGroup.Rows.Add(row);
            }

            table.RowGroups.Add(dataRowGroup);
            document.Blocks.Add(table);
            document.Blocks.Add(new Paragraph { Margin = new Thickness(0, 0, 0, 10) }); // Spacing
        }

        private void AddFinancialSummary(FlowDocument document, Invoice invoice)
        {
            var summaryTable = new Table();
            summaryTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            summaryTable.Columns.Add(new TableColumn { Width = new GridLength(150, GridUnitType.Pixel) });

            var rowGroup = new TableRowGroup();

            // Sub Total
            var subTotalRow = new TableRow();
            subTotalRow.Cells.Add(new TableCell());
            subTotalRow.Cells.Add(new TableCell(new Paragraph(new Run($"Sub Total: {CurrencyHelper.FormatForDisplay(invoice.Subtotal)}")))
            {
                TextAlignment = TextAlignment.Right
            });
            rowGroup.Rows.Add(subTotalRow);

            // PPN
            var ppnRow = new TableRow();
            ppnRow.Cells.Add(new TableCell());
            ppnRow.Cells.Add(new TableCell(new Paragraph(new Run($"PPN ({invoice.VatPercentage}%): {CurrencyHelper.FormatForDisplay(invoice.VatAmount)}")))
            {
                TextAlignment = TextAlignment.Right
            });
            rowGroup.Rows.Add(ppnRow);

            // Total
            var totalRow = new TableRow();
            totalRow.Cells.Add(new TableCell());
            var totalCell = new TableCell(new Paragraph(new Run($"Total: {CurrencyHelper.FormatForDisplay(invoice.TotalAmount)}")))
            {
                TextAlignment = TextAlignment.Right
            };
            totalCell.Blocks.First().FontWeight = FontWeights.Bold;
            totalRow.Cells.Add(totalCell);
            rowGroup.Rows.Add(totalRow);

            summaryTable.RowGroups.Add(rowGroup);
            document.Blocks.Add(summaryTable);
            document.Blocks.Add(new Paragraph { Margin = new Thickness(0, 0, 0, 10) }); // Spacing
        }

        private void AddTerbilang(FlowDocument document, Invoice invoice)
        {
            var terbilang = CurrencyHelper.ConvertToWordsForInvoice(invoice.TotalAmount);
            var terbilangPara = new Paragraph
            {
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 20)
            };
            terbilangPara.Inlines.Add(new Run($"Terbilang: {terbilang}"));
            document.Blocks.Add(terbilangPara);
        }

        private void AddFooter(FlowDocument document, Invoice invoice, PrintSettings settings)
        {
            var footerTable = new Table();
            footerTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            footerTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            footerTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

            var rowGroup = new TableRowGroup();
            var row = new TableRow();

            // Terms/Notes
            var termsCell = new TableCell();
            if (!string.IsNullOrWhiteSpace(invoice.Notes))
            {
                termsCell.Blocks.Add(new Paragraph(new Run(invoice.Notes)) { FontSize = 8 });
            }

            // Company name
            var companyCell = new TableCell(new Paragraph(new Run(settings.CompanyName))
            {
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center
            });

            // Signatory
            var signatoryCell = new TableCell();
            signatoryCell.Blocks.Add(new Paragraph(new Run("_________________")) { FontSize = 8, TextAlignment = TextAlignment.Right });
            signatoryCell.Blocks.Add(new Paragraph(new Run("Authorized Signature")) { FontSize = 8, TextAlignment = TextAlignment.Right });

            row.Cells.Add(termsCell);
            row.Cells.Add(companyCell);
            row.Cells.Add(signatoryCell);
            rowGroup.Rows.Add(row);
            footerTable.RowGroups.Add(rowGroup);

            document.Blocks.Add(footerTable);

            // Bank info (jika ada dan setting enabled)
            if (invoice.BankAccount != null && settings.ShowBankOnLastPageOnly)
            {
                document.Blocks.Add(new Paragraph { Margin = new Thickness(0, 0, 0, 10) }); // Spacing

                var bankInfoPara = new Paragraph
                {
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                bankInfoPara.Inlines.Add(new Run("BANK INFORMATION:"));
                document.Blocks.Add(bankInfoPara);

                var bankDetailsPara = new Paragraph
                {
                    FontSize = 10
                };
                bankDetailsPara.Inlines.Add(new Run(invoice.BankAccount.FormatForInvoice()));
                document.Blocks.Add(bankDetailsPara);
            }
        }

        #endregion

        #region Print Preview

        private bool? ShowPrintPreview(FlowDocument document, Invoice invoice)
        {
            try
            {
                var previewWindow = new Window
                {
                    Title = $"Print Preview - Invoice {invoice.InvoiceNumber}",
                    Width = 800,
                    Height = 600,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Application.Current.MainWindow
                };

                var viewer = new FlowDocumentPageViewer
                {
                    Document = document
                };

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(10)
                };

                var printButton = new Button
                {
                    Content = "Print",
                    Margin = new Thickness(5),
                    Padding = new Thickness(15, 5),
                    IsDefault = true
                };

                var cancelButton = new Button
                {
                    Content = "Cancel",
                    Margin = new Thickness(5),
                    Padding = new Thickness(15, 5),
                    IsCancel = true
                };

                buttonPanel.Children.Add(printButton);
                buttonPanel.Children.Add(cancelButton);

                var mainPanel = new DockPanel();
                DockPanel.SetDock(buttonPanel, Dock.Bottom);
                mainPanel.Children.Add(buttonPanel);
                mainPanel.Children.Add(viewer);

                previewWindow.Content = mainPanel;

                bool? result = null;
                printButton.Click += (s, e) => { result = true; previewWindow.Close(); };
                cancelButton.Click += (s, e) => { result = false; previewWindow.Close(); };

                previewWindow.ShowDialog();
                return result;
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error showing print preview: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Helper Methods

        private async Task<Invoice?> GetInvoiceForPrintAsync(int invoiceId)
        {
            return await _context.Invoices
                .Include(i => i.Company)
                .Include(i => i.BankAccount)
                .Include(i => i.InvoiceLines.OrderBy(il => il.Baris).ThenBy(il => il.LineOrder))
                .ThenInclude(il => il.TkaWorker)
                .Include(i => i.InvoiceLines)
                .ThenInclude(il => il.JobDescription)
                .FirstOrDefaultAsync(i => i.Id == invoiceId);
        }

        private async Task<PrintSettings> GetPrintSettingsAsync()
        {
            var settings = new PrintSettings();

            try
            {
                var settingsDict = await _context.Settings
                    .Where(s => s.SettingKey.StartsWith("company_") || 
                               s.SettingKey.StartsWith("invoice_") || 
                               s.SettingKey.StartsWith("print_"))
                    .ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue);

                settings.CompanyName = settingsDict.GetValueOrDefault("company_name", "PT. FORTUNA SADA NIOGA");
                settings.CompanyTagline = settingsDict.GetValueOrDefault("company_tagline", "Spirit of Services");
                settings.CompanyAddress = settingsDict.GetValueOrDefault("company_address", "Jakarta");
                settings.CompanyPhone = settingsDict.GetValueOrDefault("company_phone", "");
                settings.InvoicePlace = settingsDict.GetValueOrDefault("invoice_place", "Jakarta");
                settings.ShowBankOnLastPageOnly = bool.Parse(settingsDict.GetValueOrDefault("show_bank_last_page_only", "true"));
            }
            catch
            {
                // Use default values
            }

            return settings;
        }

        private async Task UpdatePrintCountAsync(int invoiceId)
        {
            try
            {
                var invoice = await _context.Invoices.FindAsync(invoiceId);
                if (invoice != null)
                {
                    invoice.IncrementPrintCount();
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating print count: {ex.Message}");
            }
        }

        #endregion
    }

    #region Support Classes

    public class PrintSettings
    {
        public string CompanyName { get; set; } = "PT. FORTUNA SADA NIOGA";
        public string CompanyTagline { get; set; } = "Spirit of Services";
        public string CompanyAddress { get; set; } = "Jakarta";
        public string CompanyPhone { get; set; } = "";
        public string InvoicePlace { get; set; } = "Jakarta";
        public bool ShowBankOnLastPageOnly { get; set; } = true;
    }

    public class PageBreak : FrameworkElement
    {
        public PageBreak()
        {
            this.Height = 0;
            this.Visibility = Visibility.Hidden;
        }
    }

    #endregion
}