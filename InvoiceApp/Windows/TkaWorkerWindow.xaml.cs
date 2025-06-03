using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using InvoiceApp.Models;
using InvoiceApp.Services;
using InvoiceApp.Helpers;

namespace InvoiceApp.Windows
{
    /// <summary>
    /// TKA Worker management window for CRUD operations on TKA workers and family members
    /// </summary>
    public partial class TkaWorkerWindow : Window
    {
        private readonly TkaService _tkaService;
        private readonly CompanyService _companyService;
        private readonly ExcelService _excelService;
        private readonly DispatcherTimer _searchTimer;
        
        private TkaWorker? _selectedTkaWorker;
        private TkaFamily? _selectedFamily;
        private bool _isEditingTka = false;
        private bool _isLoadingData = false;

        private List<TkaWorker> _allTkaWorkers = new();
        private List<TkaFamily> _currentFamilyMembers = new();
        private List<Company> _allCompanies = new();

        public TkaWorkerWindow()
        {
            InitializeComponent();
            
            // Get services from DI container
            _tkaService = App.GetService<TkaService>();
            _companyService = App.GetService<CompanyService>();
            _excelService = App.GetService<ExcelService>();

            // Setup search timer for delayed search (300ms delay)
            _searchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _searchTimer.Tick += SearchTimer_Tick;

            Loaded += TkaWorkerWindow_Loaded;
        }

        #region Window Events

        private async void TkaWorkerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await InitializeWindowAsync();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error loading TKA worker window: {ex.Message}");
            }
        }

        private async Task InitializeWindowAsync()
        {
            SetLoadingState(true, "Initializing...");

            // Load companies for filter
            await LoadCompaniesAsync();
            
            // Load TKA workers
            await LoadTkaWorkersAsync();
            
            // Clear form
            ClearTkaForm();
            UpdateUI();

            SetLoadingState(false);
            txtStatus.Text = "Ready";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        #endregion

        #region Data Loading

        /// <summary>
        /// Load companies for filtering
        /// </summary>
        private async Task LoadCompaniesAsync()
        {
            try
            {
                var (companies, _) = await _companyService.GetCompaniesAsync(1, 1000, includeInactive: false);
                _allCompanies = companies;

                // Update company filter combobox
                cmbCompanyFilter.Items.Clear();
                cmbCompanyFilter.Items.Add(new ComboBoxItem { Content = "All Companies", Tag = 0 });
                
                foreach (var company in companies)
                {
                    cmbCompanyFilter.Items.Add(new ComboBoxItem { Content = company.CompanyName, Tag = company.Id });
                }

                cmbCompanyFilter.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load companies: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Load TKA workers from database
        /// </summary>
        private async Task LoadTkaWorkersAsync()
        {
            try
            {
                SetLoadingState(true, "Loading TKA workers...");

                var includeInactive = chkShowInactiveTka.IsChecked == true;
                var searchTerm = string.IsNullOrWhiteSpace(txtSearchTka.Text) ? null : txtSearchTka.Text;
                
                // Get company filter
                int? companyFilter = null;
                if (cmbCompanyFilter.SelectedItem is ComboBoxItem selectedItem && 
                    selectedItem.Tag is int companyId && companyId > 0)
                {
                    companyFilter = companyId;
                }

                var (tkaWorkers, totalCount) = await _tkaService.GetTkaWorkersAsync(
                    1, 1000, searchTerm, includeInactive, companyFilter);

                _allTkaWorkers = tkaWorkers;
                lstTkaWorkers.ItemsSource = _allTkaWorkers;

                // Update status
                txtTkaCount.Text = $"{totalCount} TKA workers";
                txtStatus.Text = $"Loaded {tkaWorkers.Count} TKA workers";

                // Select first TKA if available
                if (_allTkaWorkers.Any() && lstTkaWorkers.SelectedItem == null)
                {
                    lstTkaWorkers.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load TKA workers: {ex.Message}", ex);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        /// <summary>
        /// Load family members for selected TKA worker
        /// </summary>
        private async Task LoadFamilyMembersAsync(int tkaId)
        {
            try
            {
                if (tkaId <= 0) return;

                var familyMembers = await _tkaService.GetFamilyMembersByTkaIdAsync(tkaId, includeInactive: false);
                _currentFamilyMembers = familyMembers;
                dgFamilyMembers.ItemsSource = _currentFamilyMembers;

                // Update family summary
                UpdateFamilySummary();
                btnAddFamily.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error loading family members: {ex.Message}");
            }
        }

        /// <summary>
        /// Load TKA performance statistics
        /// </summary>
        private async Task LoadTkaPerformanceAsync(int tkaId)
        {
            try
            {
                if (tkaId <= 0)
                {
                    ClearPerformanceStats();
                    return;
                }

                var stats = await _tkaService.GetTkaStatsAsync(tkaId);

                // Update performance display
                if (stats.TryGetValue("TotalInvoiceLines", out var totalInvoices))
                    txtTotalInvoices.Text = totalInvoices.ToString();

                if (stats.TryGetValue("TotalRevenue", out var totalRevenue) && totalRevenue is decimal revenue)
                    txtTotalRevenue.Text = CurrencyHelper.FormatForDisplay(revenue);

                if (stats.TryGetValue("UniqueCompanies", out var uniqueCompanies))
                    txtCompanyCount.Text = uniqueCompanies.ToString();

                // Update TKA stats text
                var statsText = $"Total invoices: {totalInvoices}\n";
                statsText += $"Revenue: {(totalRevenue is decimal rev ? CurrencyHelper.FormatForDisplay(rev) : "Rp 0")}\n";
                statsText += $"Companies worked: {uniqueCompanies}";
                
                if (stats.TryGetValue("LastInvoiceDate", out var lastDate) && lastDate is DateTime lastInvoiceDate)
                {
                    statsText += $"\nLast invoice: {lastInvoiceDate:dd/MM/yyyy}";
                }

                txtTkaStats.Text = statsText;
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error loading TKA performance: {ex.Message}");
            }
        }

        #endregion

        #region Search and Filter

        private void TxtSearchTka_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Reset timer on each keystroke
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private async void SearchTimer_Tick(object? sender, EventArgs e)
        {
            _searchTimer.Stop();
            
            try
            {
                await LoadTkaWorkersAsync();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Search failed: {ex.Message}");
            }
        }

        private async void ChkShowInactiveTka_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadTkaWorkersAsync();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Filter change failed: {ex.Message}");
            }
        }

        private async void CmbCompanyFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                await LoadTkaWorkersAsync();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Company filter change failed: {ex.Message}");
            }
        }

        #endregion

        #region TKA Worker CRUD Operations

        private async void LstTkaWorkers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (lstTkaWorkers.SelectedItem is TkaWorker selectedTka)
                {
                    _selectedTkaWorker = selectedTka;
                    LoadTkaToForm(selectedTka);
                    await LoadFamilyMembersAsync(selectedTka.Id);
                    await LoadTkaPerformanceAsync(selectedTka.Id);
                    btnDeleteTka.IsEnabled = true;
                }
                else
                {
                    _selectedTkaWorker = null;
                    ClearTkaForm();
                    btnDeleteTka.IsEnabled = false;
                    btnAddFamily.IsEnabled = false;
                }
                
                UpdateUI();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error selecting TKA worker: {ex.Message}");
            }
        }

        private void BtnAddTka_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _selectedTkaWorker = null;
                _isEditingTka = true;
                ClearTkaForm();
                UpdateUI();
                txtTkaName.Focus();
                
                // Switch to TKA Details tab
                tabTkaDetails.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error adding TKA: {ex.Message}");
            }
        }

        private async void BtnSaveTka_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateTkaForm()) return;

                SetLoadingState(true, "Saving TKA worker...");

                var tkaWorker = new TkaWorker
                {
                    Nama = txtTkaName.Text.Trim(),
                    Passport = txtTkaPassport.Text.Trim(),
                    Divisi = txtTkaDivision.Text.Trim(),
                    JenisKelamin = ((ComboBoxItem)cmbTkaGender.SelectedItem).Content.ToString()!,
                    IsActive = chkTkaIsActive.IsChecked == true
                };

                if (_selectedTkaWorker != null)
                {
                    // Update existing TKA
                    tkaWorker.Id = _selectedTkaWorker.Id;
                    await _tkaService.UpdateTkaWorkerAsync(tkaWorker);
                    txtStatus.Text = "TKA worker updated successfully";
                }
                else
                {
                    // Create new TKA
                    await _tkaService.CreateTkaWorkerAsync(tkaWorker);
                    txtStatus.Text = "TKA worker created successfully";
                }

                _isEditingTka = false;
                await LoadTkaWorkersAsync();
                
                // Select the saved TKA
                var savedTka = _allTkaWorkers.FirstOrDefault(t => t.Passport == tkaWorker.Passport);
                if (savedTka != null)
                {
                    lstTkaWorkers.SelectedItem = savedTka;
                }

                UpdateUI();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error saving TKA worker: {ex.Message}");
                txtStatus.Text = "Failed to save TKA worker";
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private void BtnCancelTka_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _isEditingTka = false;
                
                if (_selectedTkaWorker != null)
                {
                    LoadTkaToForm(_selectedTkaWorker);
                }
                else
                {
                    ClearTkaForm();
                    lstTkaWorkers.SelectedItem = null;
                }
                
                UpdateUI();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error canceling: {ex.Message}");
            }
        }

        private async void BtnDeleteTka_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedTkaWorker == null) return;

                var result = MessageHelper.ShowDeleteConfirmation(_selectedTkaWorker.Nama);
                if (!result) return;

                SetLoadingState(true, "Deleting TKA worker...");

                await _tkaService.DeleteTkaWorkerAsync(_selectedTkaWorker.Id);
                
                txtStatus.Text = "TKA worker deleted successfully";
                await LoadTkaWorkersAsync();
                ClearTkaForm();
                UpdateUI();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error deleting TKA worker: {ex.Message}");
                txtStatus.Text = "Failed to delete TKA worker";
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        #endregion

        #region Family Member Operations

        private void DgFamilyMembers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedFamily = dgFamilyMembers.SelectedItem as TkaFamily;
        }

        private void BtnAddFamily_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedTkaWorker == null) return;

                ShowFamilyDialog(null, _selectedTkaWorker.Id);
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error adding family member: {ex.Message}");
            }
        }

        private void BtnEditFamily_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is TkaFamily family)
                {
                    ShowFamilyDialog(family, family.TkaId);
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error editing family member: {ex.Message}");
            }
        }

        private async void BtnDeleteFamily_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is TkaFamily family)
                {
                    var result = MessageHelper.ShowDeleteConfirmation(family.Nama);
                    if (!result) return;

                    SetLoadingState(true, "Deleting family member...");

                    await _tkaService.DeleteFamilyMemberAsync(family.Id);
                    
                    txtStatus.Text = "Family member deleted successfully";
                    
                    if (_selectedTkaWorker != null)
                    {
                        await LoadFamilyMembersAsync(_selectedTkaWorker.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error deleting family member: {ex.Message}");
                txtStatus.Text = "Failed to delete family member";
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        /// <summary>
        /// Show family member dialog
        /// </summary>
        private void ShowFamilyDialog(TkaFamily? family, int tkaId)
        {
            var dialog = new FamilyMemberDialog(family, tkaId);
            dialog.Owner = this;
            
            if (dialog.ShowDialog() == true)
            {
                // Refresh family members
                _ = Task.Run(async () =>
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        await LoadFamilyMembersAsync(tkaId);
                        txtStatus.Text = family == null ? "Family member added successfully" : "Family member updated successfully";
                    });
                });
            }
        }

        #endregion

        #region Import/Export Operations

        private async void BtnImportTka_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Title = "Import TKA Workers",
                    Filter = "Excel Files (*.xlsx;*.xls)|*.xlsx;*.xls|All Files (*.*)|*.*",
                    DefaultExt = "xlsx"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    SetLoadingState(true, "Importing TKA workers...");

                    var result = await _excelService.ImportTkaWorkersFromExcelAsync(openFileDialog.FileName, skipDuplicates: true);

                    if (result.IsSuccess)
                    {
                        MessageHelper.ShowSuccess($"Import completed: {result.SuccessCount} TKA workers imported, {result.SkippedCount} skipped, {result.ErrorCount} errors");
                        await LoadTkaWorkersAsync();
                    }
                    else
                    {
                        if (result.Errors.Any())
                        {
                            MessageHelper.ShowValidationErrors(result.Errors, "Import Errors");
                        }
                        else
                        {
                            MessageHelper.ShowError($"Import failed: {result.ErrorMessage}");
                        }
                    }

                    txtStatus.Text = $"Import completed: {result.SuccessCount} successful";
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Import error: {ex.Message}");
                txtStatus.Text = "Import failed";
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private async void BtnExportTka_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Title = "Export TKA Workers",
                    Filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*",
                    DefaultExt = "xlsx",
                    FileName = $"TKA_Workers_{DateTime.Now:yyyyMMdd}.xlsx"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    SetLoadingState(true, "Exporting TKA workers...");

                    var includeInactive = chkShowInactiveTka.IsChecked == true;
                    var includeFamilyMembers = MessageHelper.ShowConfirmation("Include family members in export?", "Export Options");

                    var result = await _excelService.ExportTkaWorkersToExcelAsync(
                        saveFileDialog.FileName, includeInactive, includeFamilyMembers);

                    if (result.IsSuccess)
                    {
                        MessageHelper.ShowSuccess($"Export completed: {result.SuccessCount} TKA workers exported to {saveFileDialog.FileName}");
                        txtStatus.Text = "Export completed successfully";
                    }
                    else
                    {
                        MessageHelper.ShowError($"Export failed: {result.ErrorMessage}");
                        txtStatus.Text = "Export failed";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Export error: {ex.Message}");
                txtStatus.Text = "Export failed";
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        #endregion

        #region Form Management

        /// <summary>
        /// Load TKA worker data to form
        /// </summary>
        private void LoadTkaToForm(TkaWorker tka)
        {
            txtTkaName.Text = tka.Nama;
            txtTkaPassport.Text = tka.Passport;
            txtTkaDivision.Text = tka.Divisi ?? "";
            cmbTkaGender.SelectedIndex = tka.JenisKelamin == "Perempuan" ? 1 : 0;
            chkTkaIsActive.IsChecked = tka.IsActive;
        }

        /// <summary>
        /// Clear TKA worker form
        /// </summary>
        private void ClearTkaForm()
        {
            txtTkaName.Clear();
            txtTkaPassport.Clear();
            txtTkaDivision.Clear();
            cmbTkaGender.SelectedIndex = 0;
            chkTkaIsActive.IsChecked = true;
            
            dgFamilyMembers.ItemsSource = null;
            txtFamilyCount.Text = "0 family members";
            txtFamilySummary.Text = "No family members";
            
            ClearPerformanceStats();
        }

        /// <summary>
        /// Clear performance statistics
        /// </summary>
        private void ClearPerformanceStats()
        {
            txtTotalInvoices.Text = "0";
            txtTotalRevenue.Text = "Rp 0";
            txtCompanyCount.Text = "0";
            txtTkaStats.Text = "No statistics available";
        }

        /// <summary>
        /// Update family summary
        /// </summary>
        private void UpdateFamilySummary()
        {
            if (!_currentFamilyMembers.Any())
            {
                txtFamilySummary.Text = "No family members";
                txtFamilyCount.Text = "0 family members";
                return;
            }

            var summary = $"{_currentFamilyMembers.Count} family member(s): ";
            var relationships = _currentFamilyMembers.GroupBy(f => f.Relationship)
                .Select(g => $"{g.Count()} {g.Key}")
                .ToList();
            
            summary += string.Join(", ", relationships);
            txtFamilySummary.Text = summary;
            txtFamilyCount.Text = $"{_currentFamilyMembers.Count} family members";
        }

        /// <summary>
        /// Validate TKA worker form
        /// </summary>
        private bool ValidateTkaForm()
        {
            var errors = ValidationHelper.ValidateTkaWorker(
                txtTkaName.Text, txtTkaPassport.Text, txtTkaDivision.Text);

            if (errors.Any())
            {
                MessageHelper.ShowValidationErrors(errors, "TKA Worker Validation");
                return false;
            }

            return true;
        }

        #endregion

        #region UI Helper Methods

        /// <summary>
        /// Update UI state based on current mode
        /// </summary>
        private void UpdateUI()
        {
            var hasSelection = _selectedTkaWorker != null;
            var isEditing = _isEditingTka;

            // Form state
            txtTkaName.IsEnabled = isEditing;
            txtTkaPassport.IsEnabled = isEditing;
            txtTkaDivision.IsEnabled = isEditing;
            cmbTkaGender.IsEnabled = isEditing;
            chkTkaIsActive.IsEnabled = isEditing;

            // Button states
            btnSaveTka.IsEnabled = isEditing;
            btnCancelTka.IsEnabled = isEditing;
            btnDeleteTka.IsEnabled = hasSelection && !isEditing;
            btnAddFamily.IsEnabled = hasSelection && !isEditing;

            // List selection
            lstTkaWorkers.IsEnabled = !isEditing;
        }

        /// <summary>
        /// Set loading state
        /// </summary>
        private void SetLoadingState(bool isLoading, string? message = null)
        {
            _isLoadingData = isLoading;
            
            pnlLoading.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            
            if (message != null)
            {
                txtStatus.Text = message;
            }

            // Disable all interactive elements during loading
            pnlTkaDetails.IsEnabled = !isLoading;
            lstTkaWorkers.IsEnabled = !isLoading;
            dgFamilyMembers.IsEnabled = !isLoading;
            tabTkaDetails.IsEnabled = !isLoading;
        }

        #endregion

        #region Keyboard Shortcuts

        protected override void OnKeyDown(KeyEventArgs e)
        {
            try
            {
                if (e.Key == Key.F5)
                {
                    // F5 - Refresh
                    _ = Task.Run(async () =>
                    {
                        await Dispatcher.InvokeAsync(async () =>
                        {
                            await LoadTkaWorkersAsync();
                            txtStatus.Text = "Data refreshed";
                        });
                    });
                    e.Handled = true;
                }
                else if (e.Key == Key.N && 
                        (e.KeyboardDevice.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    // Ctrl+N - New TKA
                    if (!_isLoadingData)
                    {
                        BtnAddTka_Click(this, new RoutedEventArgs());
                    }
                    e.Handled = true;
                }
                else if (e.Key == Key.S && 
                        (e.KeyboardDevice.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    // Ctrl+S - Save TKA
                    if (_isEditingTka && !_isLoadingData)
                    {
                        BtnSaveTka_Click(this, new RoutedEventArgs());
                    }
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    // Esc - Cancel or Close
                    if (_isEditingTka)
                    {
                        BtnCancelTka_Click(this, new RoutedEventArgs());
                    }
                    else
                    {
                        this.Close();
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

        #region Window Cleanup

        protected override void OnClosed(EventArgs e)
        {
            // Stop search timer
            _searchTimer?.Stop();
            
            base.OnClosed(e);
        }

        #endregion
    }

    /// <summary>
    /// Simple dialog for family member editing
    /// </summary>
    public partial class FamilyMemberDialog : Window
    {
        private readonly TkaService _tkaService;
        private readonly TkaFamily? _family;
        private readonly int _tkaId;

        public FamilyMemberDialog(TkaFamily? family, int tkaId)
        {
            _tkaService = App.GetService<TkaService>();
            _family = family;
            _tkaId = tkaId;
            
            InitializeComponent();
            LoadFamilyData();
        }

        private void InitializeComponent()
        {
            // Simple dialog layout - could be expanded to separate XAML file
            Width = 450;
            Height = 350;
            Title = _family == null ? "Add Family Member" : "Edit Family Member";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            
            // TODO: Implement proper XAML layout for family dialog
            // For now, this is a placeholder that shows the concept
        }

        private void LoadFamilyData()
        {
            if (_family != null)
            {
                // Load existing family data
                // TODO: Populate form fields
            }
        }
    }
}