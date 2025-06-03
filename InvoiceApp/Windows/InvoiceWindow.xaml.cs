using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using InvoiceApp.Models;
using InvoiceApp.Services;
using InvoiceApp.Helpers;

namespace InvoiceApp.Windows
{
    /// <summary>
    /// Invoice creation and editing window with real-time calculations and auto-save
    /// </summary>
    public partial class InvoiceWindow : Window
    {
        #region Services and Fields

        private readonly InvoiceService _invoiceService;
        private readonly CompanyService _companyService;
        private readonly TkaService _tkaService;
        private readonly SearchService _searchService;
        private readonly PrintService _printService;

        private readonly DispatcherTimer _autoSaveTimer;
        private readonly DispatcherTimer _searchTimer;

        private WindowMode _currentMode = WindowMode.Create;
        private Invoice? _currentInvoice;
        private int _currentInvoiceId = 0;
        private bool _isAutoSaveEnabled = true;
        private bool _hasUnsavedChanges = false;
        private bool _isLoadingData = false;

        private ObservableCollection<InvoiceLine> _invoiceLines = new();
        private List<Company> _companies = new();
        private List<TkaWorker> _tkaWorkers = new();
        private List<JobDescription> _jobDescriptions = new();
        private List<BankAccount> _bankAccounts = new();

        #endregion

        #region Enums and Constructor

        public enum WindowMode
        {
            Create,
            Edit,
            View
        }

        public InvoiceWindow()
        {
            InitializeComponent();
            
            // Get services from DI container
            _invoiceService = App.GetService<InvoiceService>();
            _companyService = App.GetService<CompanyService>();
            _tkaService = App.GetService<TkaService>();
            _searchService = App.GetService<SearchService>();
            _printService = App.GetService<PrintService>();

            // Setup auto-save timer (every 30 seconds)
            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;

            // Setup search timer for TKA search (300ms delay)
            _searchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _searchTimer.Tick += SearchTimer_Tick;

            // Setup data binding
            dgInvoiceLines.ItemsSource = _invoiceLines;

            Loaded += InvoiceWindow_Loaded;
            Closing += InvoiceWindow_Closing;
        }

        #endregion

        #region Window Events and Initialization

        private async void InvoiceWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await InitializeWindowAsync();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error loading invoice window: {ex.Message}");
            }
        }

        private async Task InitializeWindowAsync()
        {
            SetLoadingState(true, "Initializing invoice window...");

            try
            {
                // Load master data
                await LoadMasterDataAsync();

                // Initialize based on mode
                if (_currentMode == WindowMode.Create)
                {
                    await InitializeNewInvoiceAsync();
                }
                else if (_currentMode == WindowMode.Edit && _currentInvoiceId > 0)
                {
                    await LoadExistingInvoiceAsync(_currentInvoiceId);
                }

                // Start auto-save timer if creating/editing
                if (_currentMode != WindowMode.View && _isAutoSaveEnabled)
                {
                    _autoSaveTimer.Start();
                }

                UpdateUI();
                txtStatus.Text = "Ready";
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private async Task LoadMasterDataAsync()
        {
            // Load companies
            var (companies, _) = await _companyService.GetCompaniesAsync(1, 1000, includeInactive: false);
            _companies = companies;
            cmbCompany.ItemsSource = _companies;

            // Load bank accounts
            // TODO: Implement bank account service
            // For now, create placeholder
            _bankAccounts = new List<BankAccount>();
            cmbBankAccount.ItemsSource = _bankAccounts;

            // Set default date
            dpInvoiceDate.SelectedDate = DateTime.Today;
        }

        private async Task InitializeNewInvoiceAsync()
        {
            txtWindowTitle.Text = "Create New Invoice";
            txtInvoiceStatus.Text = "Draft";

            // Generate invoice number
            var invoiceNumber = await _invoiceService.GenerateInvoiceNumberAsync(DateTime.Today);
            txtInvoiceNumber.Text = invoiceNumber;

            // Reset form
            ClearInvoiceForm();
            UpdateTotals();
        }

        private async Task LoadExistingInvoiceAsync(int invoiceId)
        {
            var invoice = await _invoiceService.GetInvoiceByIdAsync(invoiceId, includeLines: true);
            if (invoice == null)
            {
                MessageHelper.ShowError("Invoice not found");
                this.Close();
                return;
            }

            _currentInvoice = invoice;
            txtWindowTitle.Text = $"Edit Invoice - {invoice.InvoiceNumber}";
            txtInvoiceStatus.Text = invoice.StatusDisplay;

            // Load invoice data to form
            LoadInvoiceToForm(invoice);

            // Update UI based on invoice status
            UpdateUIForInvoiceStatus();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Set window mode and invoice ID
        /// </summary>
        public void SetMode(WindowMode mode, int invoiceId = 0)
        {
            _currentMode = mode;
            _currentInvoiceId = invoiceId;
        }

        #endregion

        #region Data Loading

        private async Task LoadTkaWorkersForCompanyAsync(int companyId)
        {
            try
            {
                // Load TKA workers that have worked for this company
                var tkaWorkers = await _searchService.GetTkaByCompanyAsync(companyId);
                _tkaWorkers = tkaWorkers;
                cmbTkaWorker.ItemsSource = _tkaWorkers;
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error loading TKA workers: {ex.Message}");
            }
        }

        private async Task LoadJobDescriptionsForCompanyAsync(int companyId)
        {
            try
            {
                var jobDescriptions = await _companyService.GetJobsByCompanyAsync(companyId);
                _jobDescriptions = jobDescriptions;
                cmbJobDescription.ItemsSource = _jobDescriptions;
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error loading job descriptions: {ex.Message}");
            }
        }

        #endregion

        #region Form Management

        private void LoadInvoiceToForm(Invoice invoice)
        {
            txtInvoiceNumber.Text = invoice.InvoiceNumber;
            cmbCompany.SelectedValue = invoice.CompanyId;
            dpInvoiceDate.SelectedDate = invoice.InvoiceDate;
            txtVatPercentage.Text = invoice.VatPercentage.ToString("0.00");
            txtNotes.Text = invoice.Notes ?? "";
            cmbBankAccount.SelectedValue = invoice.BankAccountId;

            // Load invoice lines
            _invoiceLines.Clear();
            foreach (var line in invoice.InvoiceLines.OrderBy(l => l.Baris).ThenBy(l => l.LineOrder))
            {
                _invoiceLines.Add(line);
            }

            UpdateTotals();
        }

        private void ClearInvoiceForm()
        {
            cmbCompany.SelectedIndex = -1;
            txtVatPercentage.Text = "11.00";
            txtNotes.Clear();
            cmbBankAccount.SelectedIndex = -1;
            
            _invoiceLines.Clear();
            ClearLineItemForm();
            UpdateTotals();
        }

        private void ClearLineItemForm()
        {
            txtBaris.Text = GetNextBarisNumber().ToString();
            cmbTkaWorker.SelectedIndex = -1;
            cmbJobDescription.SelectedIndex = -1;
            txtQuantity.Text = "1";
            txtUnitPrice.Clear();
        }

        private int GetNextBarisNumber()
        {
            if (!_invoiceLines.Any()) return 1;
            return _invoiceLines.Max(l => l.Baris) + 1;
        }

        #endregion

        #region Event Handlers

        private async void CmbCompany_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (cmbCompany.SelectedValue is int companyId && companyId > 0)
                {
                    await LoadTkaWorkersForCompanyAsync(companyId);
                    await LoadJobDescriptionsForCompanyAsync(companyId);
                    
                    // Clear TKA and job selection
                    cmbTkaWorker.SelectedIndex = -1;
                    cmbJobDescription.SelectedIndex = -1;
                    txtUnitPrice.Clear();
                    
                    MarkAsChanged();
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error loading company data: {ex.Message}");
            }
        }

        private void CmbTkaWorker_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Trigger search timer for TKA workers
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private async void SearchTimer_Tick(object? sender, EventArgs e)
        {
            _searchTimer.Stop();
            
            try
            {
                if (cmbCompany.SelectedValue is int companyId && companyId > 0)
                {
                    var searchTerm = cmbTkaWorker.Text;
                    if (!string.IsNullOrWhiteSpace(searchTerm) && searchTerm.Length >= 2)
                    {
                        var searchResults = await _searchService.SearchTkaAsync(searchTerm, companyId);
                        cmbTkaWorker.ItemsSource = searchResults;
                        cmbTkaWorker.IsDropDownOpen = true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TKA search error: {ex.Message}");
            }
        }

        private void CmbJobDescription_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbJobDescription.SelectedItem is JobDescription selectedJob)
            {
                txtUnitPrice.Text = CurrencyHelper.FormatForInput(selectedJob.Price);
            }
            else
            {
                txtUnitPrice.Clear();
            }
        }

        private void TxtQuantity_TextChanged(object sender, TextChangedEventArgs e)
        {
            MarkAsChanged();
        }

        private void TxtVatPercentage_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateTotals();
            MarkAsChanged();
        }

        private void BtnAddLine_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateLineItem()) return;

                var line = CreateInvoiceLineFromForm();
                _invoiceLines.Add(line);
                
                ClearLineItemForm();
                UpdateTotals();
                MarkAsChanged();
                
                txtStatus.Text = "Line item added";
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error adding line item: {ex.Message}");
            }
        }

        private void DgInvoiceLines_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var hasSelection = dgInvoiceLines.SelectedItem != null;
            btnMoveUp.IsEnabled = hasSelection;
            btnMoveDown.IsEnabled = hasSelection;
            btnDeleteLine.IsEnabled = hasSelection;
        }

        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedLine(-1);
        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedLine(1);
        }

        private void BtnDeleteLine_Click(object sender, RoutedEventArgs e)
        {
            if (dgInvoiceLines.SelectedItem is InvoiceLine selectedLine)
            {
                _invoiceLines.Remove(selectedLine);
                UpdateTotals();
                MarkAsChanged();
                txtStatus.Text = "Line item deleted";
            }
        }

        private async void BtnSaveInvoice_Click(object sender, RoutedEventArgs e)
        {
            await SaveInvoiceAsync();
        }

        private async void BtnFinalizeInvoice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentInvoice == null) return;

                var result = MessageHelper.ShowConfirmation(
                    "Are you sure you want to finalize this invoice? Once finalized, it cannot be edited.", 
                    "Finalize Invoice");

                if (result)
                {
                    SetLoadingState(true, "Finalizing invoice...");

                    await _invoiceService.FinalizeInvoiceAsync(_currentInvoice.Id);
                    txtInvoiceStatus.Text = "Finalized";
                    
                    UpdateUIForInvoiceStatus();
                    txtStatus.Text = "Invoice finalized successfully";
                    btnPrintPreview.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error finalizing invoice: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private void BtnCancelInvoice_Click(object sender, RoutedEventArgs e)
        {
            if (_hasUnsavedChanges)
            {
                var result = MessageHelper.ShowSaveConfirmation();
                if (result == MessageBoxResult.Yes)
                {
                    _ = Task.Run(async () =>
                    {
                        await Dispatcher.InvokeAsync(async () =>
                        {
                            await SaveInvoiceAsync();
                            this.Close();
                        });
                    });
                    return;
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }
            
            this.Close();
        }

        private void BtnAutoSave_Click(object sender, RoutedEventArgs e)
        {
            _isAutoSaveEnabled = !_isAutoSaveEnabled;
            
            if (_isAutoSaveEnabled)
            {
                btnAutoSave.Content = "Auto-Save: ON";
                btnAutoSave.Style = (Style)FindResource("SuccessButtonStyle");
                _autoSaveTimer.Start();
            }
            else
            {
                btnAutoSave.Content = "Auto-Save: OFF";
                btnAutoSave.Style = (Style)FindResource("ErrorButtonStyle");
                _autoSaveTimer.Stop();
            }
        }

        private async void BtnPrintPreview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentInvoice == null) return;

                SetLoadingState(true, "Preparing print preview...");

                var success = await _printService.PrintInvoiceAsync(_currentInvoice.Id, showPreview: true);
                if (success)
                {
                    txtStatus.Text = "Print preview opened";
                }
                else
                {
                    txtStatus.Text = "Print preview cancelled";
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error opening print preview: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        #endregion

        #region Invoice Operations

        private async Task SaveInvoiceAsync()
        {
            try
            {
                if (!ValidateInvoice()) return;

                SetLoadingState(true, "Saving invoice...");

                var invoice = CreateInvoiceFromForm();

                if (_currentMode == WindowMode.Create)
                {
                    // Create new invoice
                    var currentUser = LoginWindow.GetCurrentUser();
                    var savedInvoice = await _invoiceService.CreateInvoiceAsync(invoice, currentUser?.Id ?? 1);
                    
                    _currentInvoice = savedInvoice;
                    _currentMode = WindowMode.Edit;
                    txtWindowTitle.Text = $"Edit Invoice - {savedInvoice.InvoiceNumber}";
                    txtInvoiceId.Text = $"Invoice ID: {savedInvoice.Id}";
                    
                    // Add invoice lines
                    foreach (var line in _invoiceLines)
                    {
                        line.InvoiceId = savedInvoice.Id;
                        await _invoiceService.AddInvoiceLineAsync(savedInvoice.Id, line);
                    }
                    
                    btnFinalizeInvoice.IsEnabled = true;
                    txtStatus.Text = "Invoice created successfully";
                }
                else
                {
                    // Update existing invoice
                    invoice.Id = _currentInvoice!.Id;
                    await _invoiceService.UpdateInvoiceAsync(invoice);
                    
                    // TODO: Handle invoice lines updates
                    txtStatus.Text = "Invoice updated successfully";
                }

                _hasUnsavedChanges = false;
                UpdateAutoSaveStatus();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error saving invoice: {ex.Message}");
                txtStatus.Text = "Failed to save invoice";
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private Invoice CreateInvoiceFromForm()
        {
            var invoice = new Invoice
            {
                InvoiceNumber = txtInvoiceNumber.Text,
                CompanyId = (int)cmbCompany.SelectedValue,
                InvoiceDate = dpInvoiceDate.SelectedDate ?? DateTime.Today,
                VatPercentage = decimal.Parse(txtVatPercentage.Text),
                Notes = txtNotes.Text,
                BankAccountId = cmbBankAccount.SelectedValue as int?
            };

            // Calculate totals
            invoice.Subtotal = _invoiceLines.Sum(l => l.LineTotal);
            invoice.CalculateTotals();

            return invoice;
        }

        private InvoiceLine CreateInvoiceLineFromForm()
        {
            var line = new InvoiceLine
            {
                Baris = int.Parse(txtBaris.Text),
                LineOrder = GetNextLineOrderForBaris(int.Parse(txtBaris.Text)),
                TkaId = (int)cmbTkaWorker.SelectedValue,
                JobDescriptionId = (int)cmbJobDescription.SelectedValue,
                Quantity = int.Parse(txtQuantity.Text)
            };

            // Set unit price from form or job description
            if (CurrencyHelper.TryParseCurrency(txtUnitPrice.Text, out var unitPrice))
            {
                line.UnitPrice = unitPrice;
            }
            else if (cmbJobDescription.SelectedItem is JobDescription selectedJob)
            {
                line.UnitPrice = selectedJob.Price;
            }

            line.CalculateLineTotal();

            // Set display properties (would normally be loaded from navigation properties)
            if (cmbTkaWorker.SelectedItem is TkaWorker selectedTka)
            {
                line.TkaWorker = selectedTka;
            }
            if (cmbJobDescription.SelectedItem is JobDescription selectedJobDesc)
            {
                line.JobDescription = selectedJobDesc;
            }

            return line;
        }

        private int GetNextLineOrderForBaris(int baris)
        {
            var existingLines = _invoiceLines.Where(l => l.Baris == baris);
            if (!existingLines.Any()) return 1;
            return existingLines.Max(l => l.LineOrder) + 1;
        }

        #endregion

        #region Validation

        private bool ValidateInvoice()
        {
            var errors = new List<string>();

            if (cmbCompany.SelectedValue == null)
                errors.Add("Please select a company");

            if (dpInvoiceDate.SelectedDate == null)
                errors.Add("Please select an invoice date");

            if (!decimal.TryParse(txtVatPercentage.Text, out var vatPercentage) || vatPercentage < 0 || vatPercentage > 100)
                errors.Add("VAT percentage must be between 0 and 100");

            if (!_invoiceLines.Any())
                errors.Add("Invoice must have at least one line item");

            if (errors.Any())
            {
                MessageHelper.ShowValidationErrors(errors, "Invoice Validation");
                return false;
            }

            return true;
        }

        private bool ValidateLineItem()
        {
            var errors = new List<string>();

            if (!int.TryParse(txtBaris.Text, out var baris) || baris <= 0)
                errors.Add("Baris must be a positive number");

            if (cmbTkaWorker.SelectedValue == null)
                errors.Add("Please select a TKA worker");

            if (cmbJobDescription.SelectedValue == null)
                errors.Add("Please select a job description");

            if (!int.TryParse(txtQuantity.Text, out var quantity) || quantity <= 0)
                errors.Add("Quantity must be a positive number");

            if (!CurrencyHelper.TryParseCurrency(txtUnitPrice.Text, out _))
                errors.Add("Please enter a valid unit price");

            if (errors.Any())
            {
                MessageHelper.ShowValidationErrors(errors, "Line Item Validation");
                return false;
            }

            return true;
        }

        #endregion

        #region Calculations and Updates

        private void UpdateTotals()
        {
            var subtotal = _invoiceLines.Sum(l => l.LineTotal);
            
            // Apply invoice rounding rules
            var roundedSubtotal = CurrencyHelper.ApplyInvoiceRounding(subtotal);
            
            // Calculate VAT
            if (decimal.TryParse(txtVatPercentage.Text, out var vatPercentage))
            {
                var vatAmount = CurrencyHelper.CalculateVAT(roundedSubtotal, vatPercentage);
                var totalAmount = roundedSubtotal + vatAmount;

                // Update display
                txtSubtotal.Text = CurrencyHelper.FormatForDisplay(roundedSubtotal);
                txtVatAmount.Text = CurrencyHelper.FormatForDisplay(vatAmount);
                txtTotalAmount.Text = CurrencyHelper.FormatForDisplay(totalAmount);
                lblVatText.Text = $"PPN ({vatPercentage:0.##}%):";
                
                // Update terbilang
                txtTerbilang.Text = CurrencyHelper.ConvertToWordsForInvoice(totalAmount);
            }

            // Update line count
            txtLineCount.Text = $"{_invoiceLines.Count} lines";
        }

        private void MoveSelectedLine(int direction)
        {
            if (dgInvoiceLines.SelectedItem is not InvoiceLine selectedLine) return;
            
            var currentIndex = _invoiceLines.IndexOf(selectedLine);
            var newIndex = currentIndex + direction;
            
            if (newIndex >= 0 && newIndex < _invoiceLines.Count)
            {
                _invoiceLines.Move(currentIndex, newIndex);
                dgInvoiceLines.SelectedIndex = newIndex;
                MarkAsChanged();
            }
        }

        private void UpdateUI()
        {
            var isEditable = _currentMode != WindowMode.View && 
                           (_currentInvoice?.Status == Invoice.InvoiceStatus.Draft || _currentInvoice == null);

            // Enable/disable form controls
            cmbCompany.IsEnabled = isEditable;
            dpInvoiceDate.IsEnabled = isEditable;
            txtVatPercentage.IsEnabled = isEditable;
            txtNotes.IsEnabled = isEditable;
            cmbBankAccount.IsEnabled = isEditable;

            // Line item controls
            txtBaris.IsEnabled = isEditable;
            cmbTkaWorker.IsEnabled = isEditable;
            cmbJobDescription.IsEnabled = isEditable;
            txtQuantity.IsEnabled = isEditable;
            btnAddLine.IsEnabled = isEditable;

            // Action buttons
            btnSaveInvoice.IsEnabled = isEditable;
            btnFinalizeInvoice.IsEnabled = _currentInvoice != null && 
                                         _currentInvoice.Status == Invoice.InvoiceStatus.Draft && 
                                         _invoiceLines.Any();
        }

        private void UpdateUIForInvoiceStatus()
        {
            if (_currentInvoice == null) return;

            switch (_currentInvoice.Status)
            {
                case Invoice.InvoiceStatus.Draft:
                    txtInvoiceStatus.Text = "Draft";
                    break;
                case Invoice.InvoiceStatus.Finalized:
                    txtInvoiceStatus.Text = "Finalized";
                    btnPrintPreview.IsEnabled = true;
                    break;
                case Invoice.InvoiceStatus.Paid:
                    txtInvoiceStatus.Text = "Paid";
                    btnPrintPreview.IsEnabled = true;
                    break;
                case Invoice.InvoiceStatus.Cancelled:
                    txtInvoiceStatus.Text = "Cancelled";
                    break;
            }

            UpdateUI();
        }

        #endregion

        #region Auto-Save

        private async void AutoSaveTimer_Tick(object? sender, EventArgs e)
        {
            if (!_hasUnsavedChanges || _isLoadingData) return;

            try
            {
                if (_currentInvoice != null)
                {
                    var invoice = CreateInvoiceFromForm();
                    invoice.Id = _currentInvoice.Id;
                    
                    var success = await _invoiceService.AutoSaveInvoiceAsync(invoice);
                    if (success)
                    {
                        UpdateAutoSaveStatus();
                        _hasUnsavedChanges = false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Auto-save error: {ex.Message}");
            }
        }

        private void UpdateAutoSaveStatus()
        {
            txtAutoSaveStatus.Text = $"Auto-saved at {DateTime.Now:HH:mm:ss}";
        }

        private void MarkAsChanged()
        {
            _hasUnsavedChanges = true;
        }

        #endregion

        #region UI Helper Methods

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

        #region Window Events

        private void InvoiceWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_hasUnsavedChanges)
            {
                var result = MessageHelper.ShowSaveConfirmation();
                if (result == MessageBoxResult.Yes)
                {
                    // Save and close
                    e.Cancel = true;
                    _ = Task.Run(async () =>
                    {
                        await Dispatcher.InvokeAsync(async () =>
                        {
                            await SaveInvoiceAsync();
                            _hasUnsavedChanges = false;
                            this.Close();
                        });
                    });
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    // Cancel close
                    e.Cancel = true;
                }
            }

            // Stop timers
            _autoSaveTimer?.Stop();
            _searchTimer?.Stop();
        }

        #endregion

        #region Keyboard Shortcuts

        protected override void OnKeyDown(KeyEventArgs e)
        {
            try
            {
                if (e.Key == Key.S && (e.KeyboardDevice.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    // Ctrl+S - Save
                    if (!_isLoadingData)
                    {
                        _ = Task.Run(async () => await Dispatcher.InvokeAsync(async () => await SaveInvoiceAsync()));
                    }
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter && (e.KeyboardDevice.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    // Ctrl+Enter - Add Line
                    if (btnAddLine.IsEnabled)
                    {
                        BtnAddLine_Click(this, new RoutedEventArgs());
                    }
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error handling keyboard shortcut: {ex.Message}");
            }

            base.OnKeyDown(e);
        }

        #endregion
    }
}