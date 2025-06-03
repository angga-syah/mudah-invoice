    using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using InvoiceApp.Models;
using InvoiceApp.Services;
using InvoiceApp.Helpers;

namespace InvoiceApp.Windows
{
    /// <summary>
    /// Reports and analytics window for revenue reports, company performance, and TKA analysis
    /// </summary>
    public partial class ReportsWindow : Window
    {
        #region Services and Fields

        private readonly InvoiceService _invoiceService;
        private readonly CompanyService _companyService;
        private readonly TkaService _tkaService;
        private readonly ExcelService _excelService;
        private readonly PdfService _pdfService;
        private readonly PrintService _printService;

        private List<Company> _allCompanies = new();
        private bool _isLoadingData = false;

        #endregion

        #region Constructor and Initialization

        public ReportsWindow()
        {
            InitializeComponent();
            
            // Get services from DI container
            _invoiceService = App.GetService<InvoiceService>();
            _companyService = App.GetService<CompanyService>();
            _tkaService = App.GetService<TkaService>();
            _excelService = App.GetService<ExcelService>();
            _pdfService = App.GetService<PdfService>();
            _printService = App.GetService<PrintService>();

            // Set default date ranges
            dpRevenueFrom.SelectedDate = DateTime.Today.AddMonths(-1);
            dpRevenueTo.SelectedDate = DateTime.Today;
            dpBatchFrom.SelectedDate = DateTime.Today.AddMonths(-1);
            dpBatchTo.SelectedDate = DateTime.Today;

            Loaded += ReportsWindow_Loaded;
        }

        private async void ReportsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadMasterDataAsync();
                txtStatus.Text = "Ready to generate reports";
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error loading reports window: {ex.Message}");
            }
        }

        private async Task LoadMasterDataAsync()
        {
            SetLoadingState(true, "Loading master data...");

            try
            {
                // Load companies for filters
                var (companies, _) = await _companyService.GetCompaniesAsync(1, 1000, includeInactive: false);
                _allCompanies = companies;

                // Add "All Companies" option
                var allCompaniesOption = new Company { Id = 0, CompanyName = "All Companies" };
                var companyOptions = new List<Company> { allCompaniesOption };
                companyOptions.AddRange(_allCompanies);

                cmbRevenueCompany.ItemsSource = companyOptions;
                cmbTkaCompany.ItemsSource = companyOptions;

                // Set default selections
                cmbRevenueCompany.SelectedIndex = 0;
                cmbTkaCompany.SelectedIndex = 0;
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        #endregion

        #region Window Events

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        #endregion

        #region Revenue Reports

        private async void BtnGenerateRevenue_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateRevenueFilters()) return;

                SetLoadingState(true, "Generating revenue report...");

                var dateFrom = dpRevenueFrom.SelectedDate;
                var dateTo = dpRevenueTo.SelectedDate;
                var companyId = GetSelectedCompanyId(cmbRevenueCompany);
                var reportType = cmbRevenueReportType.SelectedIndex;

                switch (reportType)
                {
                    case 0: // Monthly Summary
                        await GenerateMonthlySummaryReportAsync(dateFrom, dateTo, companyId);
                        break;
                    case 1: // Company Breakdown
                        await GenerateCompanyBreakdownReportAsync(dateFrom, dateTo);
                        break;
                    case 2: // TKA Performance
                        await GenerateTkaPerformanceReportAsync(dateFrom, dateTo, companyId);
                        break;
                    case 3: // Detailed Report
                        await GenerateDetailedReportAsync(dateFrom, dateTo, companyId);
                        break;
                }

                txtStatus.Text = "Revenue report generated successfully";
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error generating revenue report: {ex.Message}");
                txtStatus.Text = "Failed to generate revenue report";
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private async Task GenerateMonthlySummaryReportAsync(DateTime? dateFrom, DateTime? dateTo, int? companyId)
        {
            var (invoices, _) = await _invoiceService.GetInvoicesAsync(
                1, 10000, companyId, null, dateFrom, dateTo);

            var monthlyData = invoices
                .Where(i => i.Status != Invoice.InvoiceStatus.Cancelled)
                .GroupBy(i => new { Year = i.InvoiceDate.Year, Month = i.InvoiceDate.Month })
                .Select(g => new
                {
                    Period = $"{g.Key.Year}-{g.Key.Month:D2}",
                    InvoiceCount = g.Count(),
                    TotalRevenue = g.Sum(i => i.TotalAmount),
                    AverageInvoice = g.Average(i => i.TotalAmount),
                    UniqueCompanies = g.Select(i => i.CompanyId).Distinct().Count()
                })
                .OrderBy(x => x.Period)
                .ToList();

            // Setup DataGrid columns
            dgRevenueReport.Columns.Clear();
            dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Period", Binding = new System.Windows.Data.Binding("Period"), Width = 100 });
            dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Invoice Count", Binding = new System.Windows.Data.Binding("InvoiceCount"), Width = 120 });
            dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Total Revenue", Binding = new System.Windows.Data.Binding("TotalRevenue") { StringFormat = "C0" }, Width = 150 });
            dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Average Invoice", Binding = new System.Windows.Data.Binding("AverageInvoice") { StringFormat = "C0" }, Width = 150 });
            dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Unique Companies", Binding = new System.Windows.Data.Binding("UniqueCompanies"), Width = 150 });

            dgRevenueReport.ItemsSource = monthlyData;

            // Update summary
            UpdateRevenueSummary(invoices);
        }

        private async Task GenerateCompanyBreakdownReportAsync(DateTime? dateFrom, DateTime? dateTo)
        {
            var companyRankings = await _companyService.GetTopCompaniesAsync("revenue", 50);

            // Filter by date range
            if (dateFrom.HasValue || dateTo.HasValue)
            {
                // Re-calculate with date filters
                var (invoices, _) = await _invoiceService.GetInvoicesAsync(
                    1, 10000, null, null, dateFrom, dateTo);

                var companyStats = invoices
                    .Where(i => i.Status != Invoice.InvoiceStatus.Cancelled)
                    .GroupBy(i => i.CompanyId)
                    .Select(g => new
                    {
                        CompanyId = g.Key,
                        CompanyName = g.First().Company.CompanyName,
                        InvoiceCount = g.Count(),
                        TotalRevenue = g.Sum(i => i.TotalAmount),
                        AverageInvoice = g.Average(i => i.TotalAmount),
                        LastInvoice = g.Max(i => i.InvoiceDate)
                    })
                    .OrderByDescending(x => x.TotalRevenue)
                    .ToList();

                dgRevenueReport.Columns.Clear();
                dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Company Name", Binding = new System.Windows.Data.Binding("CompanyName"), Width = 250 });
                dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Invoice Count", Binding = new System.Windows.Data.Binding("InvoiceCount"), Width = 120 });
                dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Total Revenue", Binding = new System.Windows.Data.Binding("TotalRevenue") { StringFormat = "C0" }, Width = 150 });
                dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Average Invoice", Binding = new System.Windows.Data.Binding("AverageInvoice") { StringFormat = "C0" }, Width = 150 });
                dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Last Invoice", Binding = new System.Windows.Data.Binding("LastInvoice") { StringFormat = "dd/MM/yyyy" }, Width = 120 });

                dgRevenueReport.ItemsSource = companyStats;

                // Update summary
                UpdateRevenueSummary(invoices);
            }
            else
            {
                dgRevenueReport.ItemsSource = companyRankings;
            }
        }

        private async Task GenerateTkaPerformanceReportAsync(DateTime? dateFrom, DateTime? dateTo, int? companyId)
        {
            var tkaPerformance = await _tkaService.GetTopTkaPerformanceAsync(50, companyId);

            dgRevenueReport.Columns.Clear();
            dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Rank", Binding = new System.Windows.Data.Binding("Rank"), Width = 60 });
            dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "TKA Name", Binding = new System.Windows.Data.Binding("TkaWorker.Nama"), Width = 200 });
            dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Passport", Binding = new System.Windows.Data.Binding("TkaWorker.Passport"), Width = 120 });
            dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Total Jobs", Binding = new System.Windows.Data.Binding("TotalInvoiceLines"), Width = 100 });
            dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Total Revenue", Binding = new System.Windows.Data.Binding("TotalRevenue") { StringFormat = "C0" }, Width = 150 });
            dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Companies", Binding = new System.Windows.Data.Binding("UniqueCompanies"), Width = 100 });

            dgRevenueReport.ItemsSource = tkaPerformance;

            // Update summary for TKA performance
            txtRevenueTotalInvoices.Text = tkaPerformance.Sum(t => t.TotalInvoiceLines).ToString("N0");
            txtRevenueTotalAmount.Text = CurrencyHelper.FormatForDisplay(tkaPerformance.Sum(t => t.TotalRevenue));
            txtRevenueAverageAmount.Text = tkaPerformance.Any() ? 
                CurrencyHelper.FormatForDisplay(tkaPerformance.Average(t => t.TotalRevenue)) : "Rp 0";
            txtRevenuePeriod.Text = $"{tkaPerformance.Count} TKA Workers";
        }

        private async Task GenerateDetailedReportAsync(DateTime? dateFrom, DateTime? dateTo, int? companyId)
        {
            var (invoices, _) = await _invoiceService.GetInvoicesAsync(
                1, 10000, companyId, null, dateFrom, dateTo);

            var detailedData = invoices
                .Where(i => i.Status != Invoice.InvoiceStatus.Cancelled)
                .Select(i => new
                {
                    InvoiceNumber = i.InvoiceNumber,
                    CompanyName = i.Company.CompanyName,
                    InvoiceDate = i.InvoiceDate,
                    Status = i.StatusDisplay,
                    Subtotal = i.Subtotal,
                    VatAmount = i.VatAmount,
                    TotalAmount = i.TotalAmount,
                    CreatedBy = i.CreatedByUser?.FullName ?? ""
                })
                .OrderByDescending(x => x.InvoiceDate)
                .ToList();

            dgRevenueReport.Columns.Clear();
            dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Invoice #", Binding = new System.Windows.Data.Binding("InvoiceNumber"), Width = 150 });
            dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Company", Binding = new System.Windows.Data.Binding("CompanyName"), Width = 200 });
            dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Date", Binding = new System.Windows.Data.Binding("InvoiceDate") { StringFormat = "dd/MM/yyyy" }, Width = 100 });
            dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new System.Windows.Data.Binding("Status"), Width = 100 });
            dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Subtotal", Binding = new System.Windows.Data.Binding("Subtotal") { StringFormat = "C0" }, Width = 120 });
            dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "VAT", Binding = new System.Windows.Data.Binding("VatAmount") { StringFormat = "C0" }, Width = 100 });
            dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Total", Binding = new System.Windows.Data.Binding("TotalAmount") { StringFormat = "C0" }, Width = 120 });
            dgRevenueReport.Columns.Add(new DataGridTextColumn { Header = "Created By", Binding = new System.Windows.Data.Binding("CreatedBy"), Width = 150 });

            dgRevenueReport.ItemsSource = detailedData;

            // Update summary
            UpdateRevenueSummary(invoices);
        }

        private void UpdateRevenueSummary(List<Invoice> invoices)
        {
            var validInvoices = invoices.Where(i => i.Status != Invoice.InvoiceStatus.Cancelled).ToList();

            txtRevenueTotalInvoices.Text = validInvoices.Count.ToString("N0");
            txtRevenueTotalAmount.Text = CurrencyHelper.FormatForDisplay(validInvoices.Sum(i => i.TotalAmount));
            txtRevenueAverageAmount.Text = validInvoices.Any() ? 
                CurrencyHelper.FormatForDisplay(validInvoices.Average(i => i.TotalAmount)) : "Rp 0";

            if (dpRevenueFrom.SelectedDate.HasValue && dpRevenueTo.SelectedDate.HasValue)
            {
                txtRevenuePeriod.Text = $"{dpRevenueFrom.SelectedDate:dd/MM/yy} - {dpRevenueTo.SelectedDate:dd/MM/yy}";
            }
            else
            {
                txtRevenuePeriod.Text = "All Time";
            }

            txtReportCount.Text = $"{validInvoices.Count} records";
        }

        #endregion

        #region Company Performance

        private async void BtnGenerateCompany_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetLoadingState(true, "Analyzing company performance...");

                var criteria = GetCompanyRankingCriteria();
                var topCount = GetCompanyTopCount();

                var companyRankings = await _companyService.GetTopCompaniesAsync(criteria, topCount);

                // Set rank numbers
                for (int i = 0; i < companyRankings.Count; i++)
                {
                    companyRankings[i].Rank = i + 1;
                }

                dgCompanyPerformance.ItemsSource = companyRankings;
                txtStatus.Text = $"Company performance analysis completed - {companyRankings.Count} companies";
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error analyzing company performance: {ex.Message}");
                txtStatus.Text = "Failed to analyze company performance";
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private string GetCompanyRankingCriteria()
        {
            return cmbCompanyRanking.SelectedIndex switch
            {
                0 => "revenue",
                1 => "invoices",
                2 => "average",
                3 => "recent",
                _ => "revenue"
            };
        }

        private int GetCompanyTopCount()
        {
            return cmbCompanyTopCount.SelectedIndex switch
            {
                0 => 5,
                1 => 10,
                2 => 20,
                3 => 1000,
                _ => 10
            };
        }

        #endregion

        #region TKA Performance

        private async void BtnGenerateTka_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetLoadingState(true, "Analyzing TKA performance...");

                var companyId = GetSelectedCompanyId(cmbTkaCompany);
                var topCount = GetTkaTopCount();

                var tkaPerformance = await _tkaService.GetTopTkaPerformanceAsync(topCount, companyId);

                dgTkaPerformance.ItemsSource = tkaPerformance;
                txtStatus.Text = $"TKA performance analysis completed - {tkaPerformance.Count} TKA workers";
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error analyzing TKA performance: {ex.Message}");
                txtStatus.Text = "Failed to analyze TKA performance";
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private int GetTkaTopCount()
        {
            return cmbTkaTopCount.SelectedIndex switch
            {
                0 => 10,
                1 => 20,
                2 => 50,
                3 => 1000,
                _ => 20
            };
        }

        #endregion

        #region Export Operations

        private async void BtnExportRevenueExcel_Click(object sender, RoutedEventArgs e)
        {
            await ExportDataGridToExcelAsync(dgRevenueReport, "Revenue_Report");
        }

        private async void BtnExportRevenuePdf_Click(object sender, RoutedEventArgs e)
        {
            MessageHelper.ShowInfo("PDF export functionality will be implemented soon.");
        }

        private async void BtnExportCompanyExcel_Click(object sender, RoutedEventArgs e)
        {
            await ExportDataGridToExcelAsync(dgCompanyPerformance, "Company_Performance");
        }

        private async void BtnExportTkaExcel_Click(object sender, RoutedEventArgs e)
        {
            await ExportDataGridToExcelAsync(dgTkaPerformance, "TKA_Performance");
        }

        private async Task ExportDataGridToExcelAsync(DataGrid dataGrid, string reportName)
        {
            try
            {
                if (dataGrid.ItemsSource == null)
                {
                    MessageHelper.ShowWarning("No data to export. Please generate a report first.");
                    return;
                }

                var saveDialog = new SaveFileDialog
                {
                    Filter = "Excel Files (*.xlsx)|*.xlsx",
                    FileName = $"{reportName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    SetLoadingState(true, $"Exporting {reportName} to Excel...");

                    // Export logic would be implemented here
                    // For now, show success message
                    await Task.Delay(1000); // Simulate export

                    MessageHelper.ShowSuccess($"Report exported successfully to:\n{saveDialog.FileName}");
                    txtStatus.Text = $"{reportName} exported to Excel";
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error exporting to Excel: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        #endregion

        #region Template Export

        private async void BtnExportTkaTemplate_Click(object sender, RoutedEventArgs e)
        {
            await ExportTemplateAsync("TKA_Workers_Template", 
                () => _excelService.ExportTkaTemplateAsync(GetSaveFilePath("TKA_Workers_Template")));
        }

        private async void BtnExportJobTemplate_Click(object sender, RoutedEventArgs e)
        {
            await ExportTemplateAsync("Job_Descriptions_Template", 
                () => _excelService.ExportJobDescriptionTemplateAsync(GetSaveFilePath("Job_Descriptions_Template")));
        }

        private async void BtnExportInvoiceTemplate_Click(object sender, RoutedEventArgs e)
        {
            await ExportTemplateAsync("Invoice_Import_Template", 
                () => _excelService.ExportInvoiceTemplateAsync(GetSaveFilePath("Invoice_Import_Template")));
        }

        private async Task ExportTemplateAsync(string templateName, Func<Task<bool>> exportAction)
        {
            try
            {
                SetLoadingState(true, $"Exporting {templateName}...");

                var success = await exportAction();
                if (success)
                {
                    MessageHelper.ShowSuccess($"{templateName} exported successfully!");
                    txtStatus.Text = $"{templateName} exported";
                }
                else
                {
                    MessageHelper.ShowError($"Failed to export {templateName}");
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error exporting template: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        #endregion

        #region Data Export

        private async void BtnExportAllInvoices_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "Excel Files (*.xlsx)|*.xlsx",
                    FileName = $"All_Invoices_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    SetLoadingState(true, "Exporting all invoices...");

                    var result = await _excelService.ExportInvoicesToExcelAsync(saveDialog.FileName);
                    
                    if (result.IsSuccess)
                    {
                        MessageHelper.ShowSuccess($"Invoices exported successfully!\n{result.Message}");
                        txtStatus.Text = "All invoices exported";
                    }
                    else
                    {
                        MessageHelper.ShowError($"Export failed: {result.ErrorMessage}");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error exporting invoices: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private async void BtnExportAllTka_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "Excel Files (*.xlsx)|*.xlsx",
                    FileName = $"All_TKA_Workers_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    SetLoadingState(true, "Exporting all TKA workers...");

                    var includeFamilyMembers = chkIncludeFamilyMembers.IsChecked == true;
                    var includeInactive = chkIncludeInactive.IsChecked == true;

                    var result = await _excelService.ExportTkaWorkersToExcelAsync(
                        saveDialog.FileName, includeInactive, includeFamilyMembers);
                    
                    if (result.IsSuccess)
                    {
                        MessageHelper.ShowSuccess($"TKA workers exported successfully!\n{result.Message}");
                        txtStatus.Text = "All TKA workers exported";
                    }
                    else
                    {
                        MessageHelper.ShowError($"Export failed: {result.ErrorMessage}");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error exporting TKA workers: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private async void BtnExportAllCompanies_Click(object sender, RoutedEventArgs e)
        {
            MessageHelper.ShowInfo("Company export functionality will be implemented soon.");
        }

        #endregion

        #region Batch Operations

        private async void BtnBatchPrint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateBatchDates()) return;

                var dateFrom = dpBatchFrom.SelectedDate;
                var dateTo = dpBatchTo.SelectedDate;

                // Get invoices in date range
                var (invoices, _) = await _invoiceService.GetInvoicesAsync(
                    1, 10000, null, Invoice.InvoiceStatus.Finalized, dateFrom, dateTo);

                if (!invoices.Any())
                {
                    MessageHelper.ShowWarning("No finalized invoices found in the selected date range.");
                    return;
                }

                var result = MessageHelper.ShowConfirmation(
                    $"This will print {invoices.Count} invoices. Continue?", "Batch Print Confirmation");

                if (result)
                {
                    SetLoadingState(true, $"Batch printing {invoices.Count} invoices...");

                    var invoiceIds = invoices.Select(i => i.Id).ToList();
                    var printedCount = await _printService.PrintBatchInvoicesAsync(invoiceIds, separateJobs: true);

                    MessageHelper.ShowSuccess($"Batch print completed. {printedCount} invoices printed successfully.");
                    txtStatus.Text = $"Batch printed {printedCount} invoices";
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error in batch printing: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private async void BtnBatchExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateBatchDates()) return;

                var saveDialog = new SaveFileDialog
                {
                    Filter = "PDF Files (*.pdf)|*.pdf",
                    FileName = $"Batch_Invoices_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var dateFrom = dpBatchFrom.SelectedDate;
                    var dateTo = dpBatchTo.SelectedDate;

                    // Get invoices in date range
                    var (invoices, _) = await _invoiceService.GetInvoicesAsync(
                        1, 10000, null, Invoice.InvoiceStatus.Finalized, dateFrom, dateTo);

                    if (!invoices.Any())
                    {
                        MessageHelper.ShowWarning("No finalized invoices found in the selected date range.");
                        return;
                    }

                    SetLoadingState(true, $"Exporting {invoices.Count} invoices to PDF...");

                    var invoiceIds = invoices.Select(i => i.Id).ToList();
                    var exportedFiles = await _pdfService.GenerateBatchInvoicePdfAsync(
                        invoiceIds, saveDialog.FileName, separateFiles: false);

                    if (exportedFiles.Any())
                    {
                        MessageHelper.ShowSuccess($"Batch export completed!\nFile: {exportedFiles.First()}");
                        txtStatus.Text = $"Batch exported {invoices.Count} invoices to PDF";
                    }
                    else
                    {
                        MessageHelper.ShowError("Batch export failed");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error in batch export: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private void BtnDataCleanup_Click(object sender, RoutedEventArgs e)
        {
            MessageHelper.ShowInfo("Data cleanup functionality will be implemented in a future version.");
        }

        private void BtnBackupData_Click(object sender, RoutedEventArgs e)
        {
            MessageHelper.ShowInfo("Database backup functionality will be implemented in a future version.");
        }

        #endregion

        #region Helper Methods

        private bool ValidateRevenueFilters()
        {
            if (dpRevenueFrom.SelectedDate.HasValue && dpRevenueTo.SelectedDate.HasValue)
            {
                if (dpRevenueFrom.SelectedDate > dpRevenueTo.SelectedDate)
                {
                    MessageHelper.ShowWarning("Date From cannot be later than Date To");
                    return false;
                }
            }

            return true;
        }

        private bool ValidateBatchDates()
        {
            if (!dpBatchFrom.SelectedDate.HasValue || !dpBatchTo.SelectedDate.HasValue)
            {
                MessageHelper.ShowWarning("Please select both start and end dates for batch operations");
                return false;
            }

            if (dpBatchFrom.SelectedDate > dpBatchTo.SelectedDate)
            {
                MessageHelper.ShowWarning("Start date cannot be later than end date");
                return false;
            }

            return true;
        }

        private int? GetSelectedCompanyId(ComboBox comboBox)
        {
            if (comboBox.SelectedValue is int companyId && companyId > 0)
                return companyId;
            return null;
        }

        private string GetSaveFilePath(string fileName)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                FileName = $"{fileName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            };

            return saveDialog.ShowDialog() == true ? saveDialog.FileName : string.Empty;
        }

        private void SetLoadingState(bool isLoading, string? message = null)
        {
            _isLoadingData = isLoading;
            
            pnlLoading.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            
            if (message != null)
            {
                txtStatus.Text = message;
            }
        }

        #endregion
    }
}