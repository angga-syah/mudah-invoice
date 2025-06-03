using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using InvoiceApp.Models;
using InvoiceApp.Services;
using InvoiceApp.Windows;
using InvoiceApp.Helpers;

namespace InvoiceApp
{
    /// <summary>
    /// Main dashboard window for Invoice Management System
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly InvoiceService _invoiceService;
        private readonly CompanyService _companyService;
        private readonly TkaService _tkaService;
        private readonly DispatcherTimer _refreshTimer;
        private User? _currentUser;

        public MainWindow()
        {
            InitializeComponent();
            
            // Get services from DI container
            _invoiceService = App.GetService<InvoiceService>();
            _companyService = App.GetService<CompanyService>();
            _tkaService = App.GetService<TkaService>();

            // Setup refresh timer (refresh stats every 5 minutes)
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;

            // Set current date
            DataContext = new MainWindowViewModel();

            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Show loading
                txtStatusMessage.Text = "Loading dashboard...";

                // Load initial data
                await LoadDashboardDataAsync();

                // Start refresh timer
                _refreshTimer.Start();

                txtStatusMessage.Text = "Ready";
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error loading dashboard: {ex.Message}");
                txtStatusMessage.Text = "Error loading dashboard";
            }
        }

        /// <summary>
        /// Load dashboard data (stats and recent invoices)
        /// </summary>
        private async Task LoadDashboardDataAsync()
        {
            try
            {
                // Load statistics
                await LoadStatisticsAsync();

                // Load recent invoices
                await LoadRecentInvoicesAsync();

                // Update current user display
                UpdateCurrentUserDisplay();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load dashboard data: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Load and display statistics
        /// </summary>
        private async Task LoadStatisticsAsync()
        {
            try
            {
                // Get current month date range
                var now = DateTime.Now;
                var monthStart = new DateTime(now.Year, now.Month, 1);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);

                // Load total invoices
                var (invoices, totalInvoiceCount) = await _invoiceService.GetInvoicesAsync(1, 1);
                txtTotalInvoices.Text = totalInvoiceCount.ToString("N0");

                // Load active companies
                var (companies, totalCompanyCount) = await _companyService.GetCompaniesAsync(1, 1);
                txtActiveCompanies.Text = totalCompanyCount.ToString("N0");

                // Load TKA workers
                var (tkaWorkers, totalTkaCount) = await _tkaService.GetTkaWorkersAsync(1, 1);
                txtTkaWorkers.Text = totalTkaCount.ToString("N0");

                // Load monthly revenue
                var monthlyStats = await _invoiceService.GetInvoiceStatsAsync(
                    dateFrom: monthStart, dateTo: monthEnd);
                
                if (monthlyStats.TryGetValue("TotalRevenue", out var revenue) && revenue is decimal revenueAmount)
                {
                    txtMonthlyRevenue.Text = CurrencyHelper.FormatForDisplay(revenueAmount);
                }
                else
                {
                    txtMonthlyRevenue.Text = "Rp 0";
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load statistics: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Load and display recent invoices
        /// </summary>
        private async Task LoadRecentInvoicesAsync()
        {
            try
            {
                // Load recent 10 invoices
                var (recentInvoices, _) = await _invoiceService.GetInvoicesAsync(1, 10);
                dgRecentInvoices.ItemsSource = recentInvoices;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load recent invoices: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Update current user display
        /// </summary>
        private void UpdateCurrentUserDisplay()
        {
            // TODO: Get current user from session/authentication
            // For now, show placeholder
            txtCurrentUser.Text = _currentUser?.FullName ?? "Admin User";
        }

        /// <summary>
        /// Set current user (called from login window)
        /// </summary>
        /// <param name="user">Logged in user</param>
        public void SetCurrentUser(User user)
        {
            _currentUser = user;
            UpdateCurrentUserDisplay();
        }

        #region Event Handlers

        private async void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                await LoadStatisticsAsync();
                txtStatusMessage.Text = $"Last updated: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                // Silent fail for background refresh
                System.Diagnostics.Debug.WriteLine($"Background refresh failed: {ex.Message}");
            }
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageHelper.ShowConfirmation(
                    "Are you sure you want to logout?", "Logout Confirmation");

                if (result)
                {
                    // Stop timer
                    _refreshTimer?.Stop();

                    // Show login window
                    var loginWindow = App.GetService<LoginWindow>();
                    loginWindow.Show();

                    // Close main window
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error during logout: {ex.Message}");
            }
        }

        private void BtnCreateInvoice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var invoiceWindow = App.GetService<InvoiceWindow>();
                invoiceWindow.SetMode(InvoiceWindow.WindowMode.Create);
                invoiceWindow.Show();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error opening invoice window: {ex.Message}");
            }
        }

        private void BtnManageCompanies_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var companyWindow = App.GetService<CompanyWindow>();
                companyWindow.Show();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error opening company window: {ex.Message}");
            }
        }

        private void BtnManageTka_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var tkaWindow = App.GetService<TkaWorkerWindow>();
                tkaWindow.Show();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error opening TKA window: {ex.Message}");
            }
        }

        private void BtnReports_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var reportsWindow = App.GetService<ReportsWindow>();
                reportsWindow.Show();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error opening reports window: {ex.Message}");
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settingsWindow = App.GetService<SettingsWindow>();
                settingsWindow.Show();
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error opening settings window: {ex.Message}");
            }
        }

        private async void BtnViewAllInvoices_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // TODO: Implement view all invoices window/dialog
                MessageHelper.ShowInfo("View All Invoices feature will be implemented soon.");
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error: {ex.Message}");
            }
        }

        private void BtnEditInvoice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is Invoice invoice)
                {
                    var invoiceWindow = App.GetService<InvoiceWindow>();
                    invoiceWindow.SetMode(InvoiceWindow.WindowMode.Edit, invoice.Id);
                    invoiceWindow.Show();
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error opening invoice for edit: {ex.Message}");
            }
        }

        private async void BtnPrintInvoice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is Invoice invoice)
                {
                    if (!invoice.CanPrint)
                    {
                        MessageHelper.ShowWarning("Only finalized invoices can be printed.");
                        return;
                    }

                    // Show loading
                    txtStatusMessage.Text = "Preparing invoice for printing...";

                    var printService = App.GetService<PrintService>();
                    var success = await printService.PrintInvoiceAsync(invoice.Id, showPreview: true);

                    if (success)
                    {
                        txtStatusMessage.Text = "Invoice sent to printer";
                        // Refresh recent invoices to update print count
                        await LoadRecentInvoicesAsync();
                    }
                    else
                    {
                        txtStatusMessage.Text = "Print cancelled or failed";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error printing invoice: {ex.Message}");
                txtStatusMessage.Text = "Print failed";
            }
        }

        #endregion

        #region Window Events

        protected override void OnClosed(EventArgs e)
        {
            // Stop refresh timer
            _refreshTimer?.Stop();
            
            base.OnClosed(e);
        }

        private async void Window_Activated(object sender, EventArgs e)
        {
            // Refresh data when window becomes active (user might have updated data in other windows)
            try
            {
                await LoadRecentInvoicesAsync();
            }
            catch
            {
                // Silent fail - don't interrupt user experience
            }
        }

        #endregion

        #region Keyboard Shortcuts

        private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                // Handle keyboard shortcuts
                if (e.Key == System.Windows.Input.Key.F5)
                {
                    // F5 - Refresh
                    _ = Task.Run(async () =>
                    {
                        await Dispatcher.InvokeAsync(async () =>
                        {
                            await LoadDashboardDataAsync();
                            txtStatusMessage.Text = "Dashboard refreshed";
                        });
                    });
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.N && 
                        (e.KeyboardDevice.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
                {
                    // Ctrl+N - New Invoice
                    BtnCreateInvoice_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Error handling keyboard shortcut: {ex.Message}");
            }
        }

        #endregion
    }

    /// <summary>
    /// ViewModel for MainWindow data binding
    /// </summary>
    public class MainWindowViewModel
    {
        public DateTime CurrentDate { get; set; } = DateTime.Now;
        public string WelcomeMessage => $"Welcome to Invoice Management System";
        public string AppVersion => "v1.0.0";
    }
}