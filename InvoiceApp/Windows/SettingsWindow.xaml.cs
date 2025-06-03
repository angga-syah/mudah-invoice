using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using InvoiceApp.Models;
using InvoiceApp.Services;
using InvoiceApp.Helpers;
using InvoiceApp.Database;

namespace InvoiceApp.Windows
{
    /// <summary>
    /// Settings window for application configuration and user management
    /// </summary>
    public partial class SettingsWindow : Window
    {
        #region Services and Fields

        private readonly AppDbContext _context;
        private readonly CompanyService _companyService;
        
        private List<Setting> _allSettings = new();
        private List<User> _allUsers = new();
        private List<BankAccount> _allBankAccounts = new();
        private bool _isLoadingData = false;
        private bool _hasUnsavedChanges = false;

        #endregion

        #region Constructor and Initialization

        public SettingsWindow()
        {
            InitializeComponent();
            
            // Get services from DI container
            _context = App.GetService<AppDbContext>();
            _companyService = App.GetService<CompanyService>();

            Loaded += SettingsWindow_Loaded;
            Closing += SettingsWindow_Closing;
        }

        private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadAllSettingsAsync();
                UpdateUI();
                txtStatus.Text = "Settings loaded successfully";
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error loading settings: {ex.Message}");
            }
        }

        private async Task LoadAllSettingsAsync()
        {
            SetLoadingState(true, "Loading settings...");

            try
            {
                // Load settings
                await LoadApplicationSettingsAsync();
                await LoadCompanySettingsAsync();
                await LoadPrintSettingsAsync();
                await LoadUsersAsync();
                await LoadBankAccountsAsync();
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        #endregion

        #region Application Settings

        private async Task LoadApplicationSettingsAsync()
        {
            try
            {
                _allSettings = await _context.Settings.ToListAsync();

                // Load current values or set defaults
                txtAutoSaveInterval.Text = GetSettingValue(Setting.Keys.AutoSaveInterval, "30");
                txtSearchDelay.Text = GetSettingValue(Setting.Keys.SearchDelay, "300");
                txtDefaultPageSize.Text = GetSettingValue(Setting.Keys.DefaultPageSize, "50");
                
                // Database info (read-only)
                txtDatabaseVersion.Text = GetSettingValue(Setting.Keys.DatabaseVersion, "1.0.0");
                var lastBackup = GetSettingValue(Setting.Keys.LastBackup, "Never");
                txtLastBackup.Text = lastBackup == "Never" ? "Never" : DateTime.Parse(lastBackup).ToString("dd/MM/yyyy HH:mm");
                
                chkMaintenanceMode.IsChecked = bool.Parse(GetSettingValue(Setting.Keys.MaintenanceMode, "false"));
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load application settings: {ex.Message}", ex);
            }
        }

        private async void BtnSaveAppSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateApplicationSettings()) return;

                SetLoadingState(true, "Saving application settings...");

                // Save settings
                await SaveSettingAsync(Setting.Keys.AutoSaveInterval, txtAutoSaveInterval.Text, Setting.SettingTypes.Integer);
                await SaveSettingAsync(Setting.Keys.SearchDelay, txtSearchDelay.Text, Setting.SettingTypes.Integer);
                await SaveSettingAsync(Setting.Keys.DefaultPageSize, txtDefaultPageSize.Text, Setting.SettingTypes.Integer);
                await SaveSettingAsync(Setting.Keys.MaintenanceMode, (chkMaintenanceMode.IsChecked == true).ToString(), Setting.SettingTypes.Boolean);

                await _context.SaveChangesAsync();

                MessageHelper.ShowSuccess("Application settings saved successfully!");
                txtStatus.Text = "Application settings saved";
                _hasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error saving application settings: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private bool ValidateApplicationSettings()
        {
            var errors = new List<string>();

            if (!int.TryParse(txtAutoSaveInterval.Text, out var autoSave) || autoSave < 10 || autoSave > 300)
                errors.Add("Auto-save interval must be between 10 and 300 seconds");

            if (!int.TryParse(txtSearchDelay.Text, out var searchDelay) || searchDelay < 100 || searchDelay > 2000)
                errors.Add("Search delay must be between 100 and 2000 milliseconds");

            if (!int.TryParse(txtDefaultPageSize.Text, out var pageSize) || pageSize < 10 || pageSize > 1000)
                errors.Add("Default page size must be between 10 and 1000 records");

            if (errors.Any())
            {
                MessageHelper.ShowValidationErrors(errors, "Application Settings Validation");
                return false;
            }

            return true;
        }

        #endregion

        #region Company Settings

        private async Task LoadCompanySettingsAsync()
        {
            try
            {
                txtCompanyName.Text = GetSettingValue(Setting.Keys.CompanyName, "PT. FORTUNA SADA NIOGA");
                txtCompanyTagline.Text = GetSettingValue(Setting.Keys.CompanyTagline, "Spirit of Services");
                txtCompanyAddress.Text = GetSettingValue(Setting.Keys.CompanyAddress, "Jakarta");
                txtCompanyPhone.Text = GetSettingValue(Setting.Keys.CompanyPhone, "");
                txtCompanyPhone2.Text = GetSettingValue(Setting.Keys.CompanyPhone2, "");
                txtDefaultVat.Text = GetSettingValue(Setting.Keys.DefaultVatPercentage, "11.00");
                txtInvoicePrefix.Text = GetSettingValue(Setting.Keys.InvoiceNumberPrefix, "FSN");
                txtInvoicePlace.Text = GetSettingValue(Setting.Keys.InvoicePlace, "Jakarta");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load company settings: {ex.Message}", ex);
            }
        }

        private async void BtnSaveCompanySettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateCompanySettings()) return;

                SetLoadingState(true, "Saving company settings...");

                // Save company settings
                await SaveSettingAsync(Setting.Keys.CompanyName, txtCompanyName.Text, Setting.SettingTypes.String);
                await SaveSettingAsync(Setting.Keys.CompanyTagline, txtCompanyTagline.Text, Setting.SettingTypes.String);
                await SaveSettingAsync(Setting.Keys.CompanyAddress, txtCompanyAddress.Text, Setting.SettingTypes.String);
                await SaveSettingAsync(Setting.Keys.CompanyPhone, txtCompanyPhone.Text, Setting.SettingTypes.String);
                await SaveSettingAsync(Setting.Keys.CompanyPhone2, txtCompanyPhone2.Text, Setting.SettingTypes.String);
                await SaveSettingAsync(Setting.Keys.DefaultVatPercentage, txtDefaultVat.Text, Setting.SettingTypes.Decimal);
                await SaveSettingAsync(Setting.Keys.InvoiceNumberPrefix, txtInvoicePrefix.Text, Setting.SettingTypes.String);
                await SaveSettingAsync(Setting.Keys.InvoicePlace, txtInvoicePlace.Text, Setting.SettingTypes.String);

                await _context.SaveChangesAsync();

                MessageHelper.ShowSuccess("Company settings saved successfully!");
                txtStatus.Text = "Company settings saved";
                _hasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error saving company settings: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private bool ValidateCompanySettings()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(txtCompanyName.Text))
                errors.Add("Company name is required");

            if (string.IsNullOrWhiteSpace(txtCompanyAddress.Text))
                errors.Add("Company address is required");

            if (!decimal.TryParse(txtDefaultVat.Text, out var vat) || vat < 0 || vat > 100)
                errors.Add("Default VAT percentage must be between 0 and 100");

            if (string.IsNullOrWhiteSpace(txtInvoicePrefix.Text))
                errors.Add("Invoice number prefix is required");

            if (string.IsNullOrWhiteSpace(txtInvoicePlace.Text))
                errors.Add("Invoice place is required");

            if (errors.Any())
            {
                MessageHelper.ShowValidationErrors(errors, "Company Settings Validation");
                return false;
            }

            return true;
        }

        #endregion

        #region Print Settings

        private async Task LoadPrintSettingsAsync()
        {
            try
            {
                txtPrintMarginTop.Text = GetSettingValue(Setting.Keys.PrintMarginTop, "20");
                txtPrintMarginBottom.Text = GetSettingValue(Setting.Keys.PrintMarginBottom, "20");
                txtPrintMarginLeft.Text = GetSettingValue(Setting.Keys.PrintMarginLeft, "20");
                txtPrintMarginRight.Text = GetSettingValue(Setting.Keys.PrintMarginRight, "20");
                chkShowBankLastPage.IsChecked = bool.Parse(GetSettingValue(Setting.Keys.ShowBankOnLastPageOnly, "true"));
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load print settings: {ex.Message}", ex);
            }
        }

        private async void BtnSavePrintSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidatePrintSettings()) return;

                SetLoadingState(true, "Saving print settings...");

                // Save print settings
                await SaveSettingAsync(Setting.Keys.PrintMarginTop, txtPrintMarginTop.Text, Setting.SettingTypes.Integer);
                await SaveSettingAsync(Setting.Keys.PrintMarginBottom, txtPrintMarginBottom.Text, Setting.SettingTypes.Integer);
                await SaveSettingAsync(Setting.Keys.PrintMarginLeft, txtPrintMarginLeft.Text, Setting.SettingTypes.Integer);
                await SaveSettingAsync(Setting.Keys.PrintMarginRight, txtPrintMarginRight.Text, Setting.SettingTypes.Integer);
                await SaveSettingAsync(Setting.Keys.ShowBankOnLastPageOnly, (chkShowBankLastPage.IsChecked == true).ToString(), Setting.SettingTypes.Boolean);

                await _context.SaveChangesAsync();

                MessageHelper.ShowSuccess("Print settings saved successfully!");
                txtStatus.Text = "Print settings saved";
                _hasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error saving print settings: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private bool ValidatePrintSettings()
        {
            var errors = new List<string>();

            if (!int.TryParse(txtPrintMarginTop.Text, out var marginTop) || marginTop < 0 || marginTop > 100)
                errors.Add("Print margin top must be between 0 and 100");

            if (!int.TryParse(txtPrintMarginBottom.Text, out var marginBottom) || marginBottom < 0 || marginBottom > 100)
                errors.Add("Print margin bottom must be between 0 and 100");

            if (!int.TryParse(txtPrintMarginLeft.Text, out var marginLeft) || marginLeft < 0 || marginLeft > 100)
                errors.Add("Print margin left must be between 0 and 100");

            if (!int.TryParse(txtPrintMarginRight.Text, out var marginRight) || marginRight < 0 || marginRight > 100)
                errors.Add("Print margin right must be between 0 and 100");

            if (errors.Any())
            {
                MessageHelper.ShowValidationErrors(errors, "Print Settings Validation");
                return false;
            }

            return true;
        }

        #endregion

        #region User Management

        private async Task LoadUsersAsync()
        {
            try
            {
                _allUsers = await _context.Users.OrderBy(u => u.FullName).ToListAsync();
                dgUsers.ItemsSource = _allUsers;
                txtUserCount.Text = $"{_allUsers.Count} users";
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load users: {ex.Message}", ex);
            }
        }

        private void BtnAddUser_Click(object sender, RoutedEventArgs e)
        {
            ShowUserDialog(null);
        }

        private void BtnEditUser_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is User user)
            {
                ShowUserDialog(user);
            }
        }

        private async void BtnDeleteUser_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is User user)
                {
                    // Check if user can be deleted
                    var invoiceCount = await _context.Invoices.CountAsync(i => i.CreatedBy == user.Id);
                    if (invoiceCount > 0)
                    {
                        MessageHelper.ShowWarning($"Cannot delete user '{user.FullName}' because they have created {invoiceCount} invoices.");
                        return;
                    }

                    var result = MessageHelper.ShowDeleteConfirmation(user.FullName);
                    if (!result) return;

                    SetLoadingState(true, "Deleting user...");

                    _context.Users.Remove(user);
                    await _context.SaveChangesAsync();

                    await LoadUsersAsync();
                    MessageHelper.ShowSuccess("User deleted successfully!");
                    txtStatus.Text = "User deleted";
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error deleting user: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private void ShowUserDialog(User? user)
        {
            var dialog = new UserDialog(user);
            dialog.Owner = this;
            
            if (dialog.ShowDialog() == true)
            {
                _ = Task.Run(async () =>
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        await LoadUsersAsync();
                        txtStatus.Text = user == null ? "User created successfully" : "User updated successfully";
                    });
                });
            }
        }

        #endregion

        #region Bank Account Management

        private async Task LoadBankAccountsAsync()
        {
            try
            {
                _allBankAccounts = await _context.BankAccounts.OrderBy(b => b.SortOrder).ThenBy(b => b.BankName).ToListAsync();
                dgBankAccounts.ItemsSource = _allBankAccounts;
                txtBankCount.Text = $"{_allBankAccounts.Count} bank accounts";
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load bank accounts: {ex.Message}", ex);
            }
        }

        private void BtnAddBank_Click(object sender, RoutedEventArgs e)
        {
            ShowBankDialog(null);
        }

        private void BtnEditBank_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is BankAccount bank)
            {
                ShowBankDialog(bank);
            }
        }

        private async void BtnDeleteBank_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is BankAccount bank)
                {
                    // Check if bank account is used in invoices
                    var invoiceCount = await _context.Invoices.CountAsync(i => i.BankAccountId == bank.Id);
                    if (invoiceCount > 0)
                    {
                        MessageHelper.ShowWarning($"Cannot delete bank account '{bank.BankName}' because it's used in {invoiceCount} invoices.");
                        return;
                    }

                    var result = MessageHelper.ShowDeleteConfirmation($"{bank.BankName} - {bank.AccountNumber}");
                    if (!result) return;

                    SetLoadingState(true, "Deleting bank account...");

                    _context.BankAccounts.Remove(bank);
                    await _context.SaveChangesAsync();

                    await LoadBankAccountsAsync();
                    MessageHelper.ShowSuccess("Bank account deleted successfully!");
                    txtStatus.Text = "Bank account deleted";
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error deleting bank account: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private async void BtnSetDefaultBank_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is BankAccount bank)
                {
                    SetLoadingState(true, "Setting default bank account...");

                    // Clear existing default
                    var currentDefault = await _context.BankAccounts.FirstOrDefaultAsync(b => b.IsDefault);
                    if (currentDefault != null)
                    {
                        currentDefault.IsDefault = false;
                    }

                    // Set new default
                    bank.IsDefault = true;
                    await _context.SaveChangesAsync();

                    await LoadBankAccountsAsync();
                    MessageHelper.ShowSuccess($"'{bank.BankName}' set as default bank account!");
                    txtStatus.Text = "Default bank account updated";
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error setting default bank account: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private void ShowBankDialog(BankAccount? bank)
        {
            var dialog = new BankAccountDialog(bank);
            dialog.Owner = this;
            
            if (dialog.ShowDialog() == true)
            {
                _ = Task.Run(async () =>
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        await LoadBankAccountsAsync();
                        txtStatus.Text = bank == null ? "Bank account created successfully" : "Bank account updated successfully";
                    });
                });
            }
        }

        #endregion

        #region Helper Methods

        private string GetSettingValue(string key, string defaultValue)
        {
            var setting = _allSettings.FirstOrDefault(s => s.SettingKey == key);
            return setting?.SettingValue ?? defaultValue;
        }

        private async Task SaveSettingAsync(string key, string value, string type)
        {
            var setting = _allSettings.FirstOrDefault(s => s.SettingKey == key);
            
            if (setting != null)
            {
                setting.SettingValue = value;
                setting.UpdateTimestamp();
            }
            else
            {
                setting = new Setting
                {
                    SettingKey = key,
                    SettingValue = value,
                    SettingType = type,
                    IsSystem = false
                };
                _context.Settings.Add(setting);
                _allSettings.Add(setting);
            }
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

        private void UpdateUI()
        {
            // Enable/disable controls based on current state
            // This can be expanded based on user permissions
        }

        private void MarkAsChanged()
        {
            _hasUnsavedChanges = true;
        }

        #endregion

        #region Text Changed Events

        private void Settings_TextChanged(object sender, TextChangedEventArgs e)
        {
            MarkAsChanged();
        }

        private void Settings_CheckChanged(object sender, RoutedEventArgs e)
        {
            MarkAsChanged();
        }

        #endregion

        #region Window Events

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void SettingsWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_hasUnsavedChanges)
            {
                var result = MessageHelper.ShowSaveConfirmation();
                if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
            }
        }

        #endregion
    }

    #region Dialog Classes

    /// <summary>
    /// Simple dialog for user management
    /// </summary>
    public partial class UserDialog : Window
    {
        private readonly AppDbContext _context;
        private readonly User? _user;
        private readonly bool _isEdit;

        public UserDialog(User? user)
        {
            _context = App.GetService<AppDbContext>();
            _user = user;
            _isEdit = user != null;
            
            InitializeComponent();
            LoadUserData();
        }

        private void InitializeComponent()
        {
            Width = 400;
            Height = 350;
            Title = _isEdit ? "Edit User" : "Add User";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            
            // TODO: Implement proper XAML layout for user dialog
            // For now, this is a placeholder that shows the concept
        }

        private void LoadUserData()
        {
            if (_user != null)
            {
                // Load existing user data to form
                // TODO: Populate form fields
            }
        }
    }

    /// <summary>
    /// Simple dialog for bank account management
    /// </summary>
    public partial class BankAccountDialog : Window
    {
        private readonly AppDbContext _context;
        private readonly BankAccount? _bank;
        private readonly bool _isEdit;

        public BankAccountDialog(BankAccount? bank)
        {
            _context = App.GetService<AppDbContext>();
            _bank = bank;
            _isEdit = bank != null;
            
            InitializeComponent();
            LoadBankData();
        }

        private void InitializeComponent()
        {
            Width = 450;
            Height = 300;
            Title = _isEdit ? "Edit Bank Account" : "Add Bank Account";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            
            // TODO: Implement proper XAML layout for bank account dialog
            // For now, this is a placeholder that shows the concept
        }

        private void LoadBankData()
        {
            if (_bank != null)
            {
                // Load existing bank data to form
                // TODO: Populate form fields
            }
        }
    }

    #endregion
}