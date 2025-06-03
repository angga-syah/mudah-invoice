using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Windows;
using InvoiceApp.Database;
using InvoiceApp.Services;
using InvoiceApp.Windows;
using InvoiceApp.Helpers;

namespace InvoiceApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private IHost? _host;
        private IConfiguration? _configuration;

        /// <summary>
        /// Gets the current service provider instance
        /// </summary>
        public static IServiceProvider ServiceProvider { get; private set; } = null!;

        /// <summary>
        /// Application startup - setup dependency injection dan configuration
        /// </summary>
        protected override async void OnStartup(StartupEventArgs e)
        {
            try
            {
                // Setup configuration
                _configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .Build();

                // Setup host dengan dependency injection
                _host = Host.CreateDefaultBuilder()
                    .ConfigureServices((context, services) =>
                    {
                        ConfigureServices(services);
                    })
                    .ConfigureLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.AddConsole();
                        logging.AddDebug();
                    })
                    .Build();

                // Start the host
                await _host.StartAsync();

                // Set global service provider
                ServiceProvider = _host.Services;

                // Initialize database
                await InitializeDatabaseAsync();

                // Show login window or main window
                ShowStartupWindow();

                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Failed to start application: {ex.Message}", "Startup Error");
                Current.Shutdown(1);
            }
        }

        /// <summary>
        /// Configure dependency injection services
        /// </summary>
        /// <param name="services">Service collection</param>
        private void ConfigureServices(IServiceCollection services)
        {
            // Configuration
            services.AddSingleton(_configuration!);

            // Database context
            services.AddDbContext<AppDbContext>(options =>
            {
                var connectionString = _configuration!.GetConnectionString("DefaultConnection");
                options.UseNpgsql(connectionString);
            });

            // Services
            services.AddScoped<SearchService>();
            services.AddScoped<CompanyService>();
            services.AddScoped<TkaService>();
            services.AddScoped<InvoiceService>();
            services.AddScoped<ExcelService>();
            services.AddScoped<PdfService>();

            // Windows (as transient since they can be created multiple times)
            services.AddTransient<MainWindow>();
            services.AddTransient<LoginWindow>();
            services.AddTransient<CompanyWindow>();
            services.AddTransient<TkaWorkerWindow>();
            services.AddTransient<InvoiceWindow>();
            services.AddTransient<SettingsWindow>();
            services.AddTransient<ReportsWindow>();

            // Logging
            services.AddLogging();
        }

        /// <summary>
        /// Initialize database - create if not exists, run migrations, seed data
        /// </summary>
        private async Task InitializeDatabaseAsync()
        {
            try
            {
                using var scope = ServiceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                // Test connection
                var canConnect = await context.TestConnectionAsync();
                if (!canConnect)
                {
                    throw new InvalidOperationException("Cannot connect to database. Please check your connection string in appsettings.json");
                }

                // Initialize database (create tables, seed data)
                await context.InitializeDatabaseAsync();

                // Log successful initialization
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<App>>();
                logger.LogInformation("Database initialized successfully");
            }
            catch (Exception ex)
            {
                var logger = ServiceProvider.GetRequiredService<ILogger<App>>();
                logger.LogError(ex, "Failed to initialize database");
                
                MessageHelper.ShowError(
                    $"Database initialization failed:\n{ex.Message}\n\nPlease check your database connection and try again.", 
                    "Database Error");
                
                Current.Shutdown(1);
            }
        }

        /// <summary>
        /// Show appropriate startup window (login or main)
        /// </summary>
        private void ShowStartupWindow()
        {
            try
            {
                // Check if we need to show login window
                var requireLogin = _configuration?.GetValue<bool>("AppSettings:RequireLogin") ?? true;

                if (requireLogin)
                {
                    var loginWindow = ServiceProvider.GetRequiredService<LoginWindow>();
                    loginWindow.Show();
                }
                else
                {
                    // Skip login and go directly to main window
                    var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
                    mainWindow.Show();
                }
            }
            catch (Exception ex)
            {
                MessageHelper.ShowError($"Failed to show startup window: {ex.Message}", "Window Error");
                Current.Shutdown(1);
            }
        }

        /// <summary>
        /// Application exit - cleanup resources
        /// </summary>
        protected override async void OnExit(ExitEventArgs e)
        {
            try
            {
                if (_host != null)
                {
                    // Cleanup any background services
                    using var scope = ServiceProvider.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    
                    // Clear cache
                    context.ClearCache();
                    
                    // Stop host
                    await _host.StopAsync();
                    _host.Dispose();
                }
            }
            catch (Exception ex)
            {
                // Log error but don't prevent shutdown
                System.Diagnostics.Debug.WriteLine($"Error during application shutdown: {ex.Message}");
            }
            finally
            {
                base.OnExit(e);
            }
        }

        /// <summary>
        /// Global exception handler
        /// </summary>
        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                var logger = ServiceProvider?.GetRequiredService<ILogger<App>>();
                logger?.LogError(e.Exception, "Unhandled exception occurred");

                MessageHelper.ShowError(
                    $"An unexpected error occurred:\n{e.Exception.Message}\n\nThe application will continue running.", 
                    "Unexpected Error");

                e.Handled = true;
            }
            catch
            {
                // Last resort - show basic error dialog
                MessageBox.Show(
                    $"Critical error: {e.Exception.Message}", 
                    "Critical Error", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Helper method to get service from DI container
        /// </summary>
        /// <typeparam name="T">Service type</typeparam>
        /// <returns>Service instance</returns>
        public static T GetService<T>() where T : class
        {
            return ServiceProvider.GetRequiredService<T>();
        }

        /// <summary>
        /// Helper method to get optional service from DI container
        /// </summary>
        /// <typeparam name="T">Service type</typeparam>
        /// <returns>Service instance or null</returns>
        public static T? GetOptionalService<T>() where T : class
        {
            return ServiceProvider.GetService<T>();
        }
    }
}