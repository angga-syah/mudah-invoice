using System;
using System.Collections.Generic;
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
    /// Company management window for CRUD operations on companies and job descriptions
    /// </summary>
    public partial class CompanyWindow : Window
    {
        private readonly CompanyService _companyService;
        private readonly DispatcherTimer _searchTimer;
        private Company? _selectedCompany;
        private JobDescription? _selectedJob;
        private bool _isEditingCompany = false;
        private bool _isLoadingData = false;

        private List<Company> _allCompanies = new();
        private List<JobDescription> _currentJobs = new();

        public CompanyWindow()
        {
            InitializeComponent();
            
            // Get services from DI container
            _companyService = App.GetService<CompanyService>();

            // Setup search timer for delayed search (300ms delay)
            _searchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _searchTimer.Tick += SearchTimer_Tick;

            Loaded += CompanyWindow_Loaded;
        }

        #region Window Events

        private async void CompanyWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadCompaniesAsync();
                ClearCompanyForm();
                UpdateUI();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error loading companies: {ex.Message}");
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        #endregion

        #region Data Loading

        /// <summary>
        /// Load companies from database
        /// </summary>
        private async Task LoadCompaniesAsync()
        {
            try
            {
                SetLoadingState(true, "Loading companies...");

                var includeInactive = chkShowInactive.IsChecked == true;
                var searchTerm = string.IsNullOrWhiteSpace(txtSearchCompany.Text) ? null : txtSearchCompany.Text;

                var (companies, totalCount) = await _companyService.GetCompaniesAsync(
                    1, 1000, searchTerm, includeInactive);

                _allCompanies = companies;
                lstCompanies.ItemsSource = _allCompanies;

                // Update status
                txtCompanyCount.Text = $"{totalCount} companies";
                txtStatus.Text = $"Loaded {companies.Count} companies";

                // Select first company if available
                if (_allCompanies.Any() && lstCompanies.SelectedItem == null)
                {
                    lstCompanies.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load companies: {ex.Message}", ex);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        /// <summary>
        /// Load job descriptions for selected company
        /// </summary>
        private async Task LoadJobDescriptionsAsync(int companyId)
        {
            try
            {
                if (companyId <= 0) return;

                var jobs = await _companyService.GetJobsByCompanyAsync(companyId, includeInactive: false);
                _currentJobs = jobs;
                dgJobDescriptions.ItemsSource = _currentJobs;

                txtJobCount.Text = $"{jobs.Count} jobs";
                btnAddJob.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error loading job descriptions: {ex.Message}");
            }
        }

        #endregion

        #region Search and Filter

        private void TxtSearchCompany_TextChanged(object sender, TextChangedEventArgs e)
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
                await LoadCompaniesAsync();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Search failed: {ex.Message}");
            }
        }

        private async void ChkShowInactive_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadCompaniesAsync();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Filter change failed: {ex.Message}");
            }
        }

        #endregion

        #region Company CRUD Operations

        private async void LstCompanies_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (lstCompanies.SelectedItem is Company selectedCompany)
                {
                    _selectedCompany = selectedCompany;
                    LoadCompanyToForm(selectedCompany);
                    await LoadJobDescriptionsAsync(selectedCompany.Id);
                    btnDeleteCompany.IsEnabled = true;
                }
                else
                {
                    _selectedCompany = null;
                    ClearCompanyForm();
                    btnDeleteCompany.IsEnabled = false;
                    btnAddJob.IsEnabled = false;
                }
                
                UpdateUI();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error selecting company: {ex.Message}");
            }
        }

        private void BtnAddCompany_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _selectedCompany = null;
                _isEditingCompany = true;
                ClearCompanyForm();
                UpdateUI();
                txtCompanyName.Focus();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error adding company: {ex.Message}");
            }
        }

        private async void BtnSaveCompany_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateCompanyForm()) return;

                SetLoadingState(true, "Saving company...");

                var company = new Company
                {
                    CompanyName = txtCompanyName.Text.Trim(),
                    Npwp = txtNpwp.Text.Trim(),
                    Idtku = txtIdtku.Text.Trim(),
                    Address = txtAddress.Text.Trim(),
                    IsActive = chkIsActive.IsChecked == true
                };

                if (_selectedCompany != null)
                {
                    // Update existing company
                    company.Id = _selectedCompany.Id;
                    await _companyService.UpdateCompanyAsync(company);
                    txtStatus.Text = "Company updated successfully";
                }
                else
                {
                    // Create new company
                    await _companyService.CreateCompanyAsync(company);
                    txtStatus.Text = "Company created successfully";
                }

                _isEditingCompany = false;
                await LoadCompaniesAsync();
                
                // Select the saved company
                var savedCompany = _allCompanies.FirstOrDefault(c => c.Npwp == company.Npwp);
                if (savedCompany != null)
                {
                    lstCompanies.SelectedItem = savedCompany;
                }

                UpdateUI();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error saving company: {ex.Message}");
                txtStatus.Text = "Failed to save company";
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private void BtnCancelCompany_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _isEditingCompany = false;
                
                if (_selectedCompany != null)
                {
                    LoadCompanyToForm(_selectedCompany);
                }
                else
                {
                    ClearCompanyForm();
                    lstCompanies.SelectedItem = null;
                }
                
                UpdateUI();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error canceling: {ex.Message}");
            }
        }

        private async void BtnDeleteCompany_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedCompany == null) return;

                var result = MessageHelper.ShowDeleteConfirmation(_selectedCompany.CompanyName);
                if (!result) return;

                SetLoadingState(true, "Deleting company...");

                await _companyService.DeleteCompanyAsync(_selectedCompany.Id);
                
                txtStatus.Text = "Company deleted successfully";
                await LoadCompaniesAsync();
                ClearCompanyForm();
                UpdateUI();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error deleting company: {ex.Message}");
                txtStatus.Text = "Failed to delete company";
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        #endregion

        #region Job Description Operations

        private void DgJobDescriptions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedJob = dgJobDescriptions.SelectedItem as JobDescription;
        }

        private void BtnAddJob_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedCompany == null) return;

                ShowJobDialog(null, _selectedCompany.Id);
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error adding job: {ex.Message}");
            }
        }

        private void BtnEditJob_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is JobDescription job)
                {
                    ShowJobDialog(job, job.CompanyId);
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error editing job: {ex.Message}");
            }
        }

        private async void BtnDeleteJob_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is JobDescription job)
                {
                    var result = MessageHelper.ShowDeleteConfirmation(job.JobName);
                    if (!result) return;

                    SetLoadingState(true, "Deleting job...");

                    await _companyService.DeleteJobDescriptionAsync(job.Id);
                    
                    txtStatus.Text = "Job deleted successfully";
                    
                    if (_selectedCompany != null)
                    {
                        await LoadJobDescriptionsAsync(_selectedCompany.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error deleting job: {ex.Message}");
                txtStatus.Text = "Failed to delete job";
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        /// <summary>
        /// Show job description dialog
        /// </summary>
        private void ShowJobDialog(JobDescription? job, int companyId)
        {
            var dialog = new JobDescriptionDialog(job, companyId);
            dialog.Owner = this;
            
            if (dialog.ShowDialog() == true)
            {
                // Refresh job descriptions
                _ = Task.Run(async () =>
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        await LoadJobDescriptionsAsync(companyId);
                        txtStatus.Text = job == null ? "Job created successfully" : "Job updated successfully";
                    });
                });
            }
        }

        #endregion

        #region Form Management

        /// <summary>
        /// Load company data to form
        /// </summary>
        private void LoadCompanyToForm(Company company)
        {
            txtCompanyName.Text = company.CompanyName;
            txtNpwp.Text = company.Npwp;
            txtIdtku.Text = company.Idtku;
            txtAddress.Text = company.Address;
            chkIsActive.IsChecked = company.IsActive;
        }

        /// <summary>
        /// Clear company form
        /// </summary>
        private void ClearCompanyForm()
        {
            txtCompanyName.Clear();
            txtNpwp.Clear();
            txtIdtku.Clear();
            txtAddress.Clear();
            chkIsActive.IsChecked = true;
            
            dgJobDescriptions.ItemsSource = null;
            txtJobCount.Text = "0 jobs";
        }

        /// <summary>
        /// Validate company form
        /// </summary>
        private bool ValidateCompanyForm()
        {
            var errors = ValidationHelper.ValidateCompany(
                txtCompanyName.Text, txtNpwp.Text, txtIdtku.Text, txtAddress.Text);

            if (errors.Any())
            {
                MessageHelper.ShowValidationErrors(errors, "Company Validation");
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
            var hasSelection = _selectedCompany != null;
            var isEditing = _isEditingCompany;

            // Form state
            txtCompanyName.IsEnabled = isEditing;
            txtNpwp.IsEnabled = isEditing;
            txtIdtku.IsEnabled = isEditing;
            txtAddress.IsEnabled = isEditing;
            chkIsActive.IsEnabled = isEditing;

            // Button states
            btnSaveCompany.IsEnabled = isEditing;
            btnCancelCompany.IsEnabled = isEditing;
            btnDeleteCompany.IsEnabled = hasSelection && !isEditing;
            btnAddJob.IsEnabled = hasSelection && !isEditing;

            // List selection
            lstCompanies.IsEnabled = !isEditing;
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
            pnlCompanyDetails.IsEnabled = !isLoading;
            lstCompanies.IsEnabled = !isLoading;
            dgJobDescriptions.IsEnabled = !isLoading;
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
                            await LoadCompaniesAsync();
                            txtStatus.Text = "Data refreshed";
                        });
                    });
                    e.Handled = true;
                }
                else if (e.Key == Key.N && 
                        (e.KeyboardDevice.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    // Ctrl+N - New Company
                    if (!_isLoadingData)
                    {
                        BtnAddCompany_Click(this, new RoutedEventArgs());
                    }
                    e.Handled = true;
                }
                else if (e.Key == Key.S && 
                        (e.KeyboardDevice.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    // Ctrl+S - Save Company
                    if (_isEditingCompany && !_isLoadingData)
                    {
                        BtnSaveCompany_Click(this, new RoutedEventArgs());
                    }
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    // Esc - Cancel or Close
                    if (_isEditingCompany)
                    {
                        BtnCancelCompany_Click(this, new RoutedEventArgs());
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
    /// Simple dialog for job description editing
    /// </summary>
    public partial class JobDescriptionDialog : Window
    {
        private readonly CompanyService _companyService;
        private readonly JobDescription? _job;
        private readonly int _companyId;

        public JobDescriptionDialog(JobDescription? job, int companyId)
        {
            _companyService = App.GetService<CompanyService>();
            _job = job;
            _companyId = companyId;
            
            InitializeComponent();
            LoadJobData();
        }

        private void InitializeComponent()
        {
            // Simple dialog layout - could be expanded to separate XAML file
            Width = 500;
            Height = 400;
            Title = _job == null ? "Add Job Description" : "Edit Job Description";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            
            // TODO: Implement proper XAML layout for job dialog
            // For now, this is a placeholder that shows the concept
        }

        private void LoadJobData()
        {
            if (_job != null)
            {
                // Load existing job data
                // TODO: Populate form fields
            }
        }
    }
}