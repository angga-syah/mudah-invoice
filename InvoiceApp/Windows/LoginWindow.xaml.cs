using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using InvoiceApp.Database;
using InvoiceApp.Models;
using InvoiceApp.Helpers;

namespace InvoiceApp.Windows
{
    /// <summary>
    /// Login window for user authentication
    /// </summary>
    public partial class LoginWindow : Window
    {
        private readonly AppDbContext _context;
        private bool _isLoading = false;

        public LoginWindow()
        {
            InitializeComponent();
            
            // Get database context from DI container
            _context = App.GetService<AppDbContext>();

            // Set focus to username field
            Loaded += (s, e) => txtUsername.Focus();
        }

        #region Event Handlers

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            await PerformLoginAsync();
        }

        private void LoginWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !_isLoading)
            {
                _ = Task.Run(async () =>
                {
                    await Dispatcher.InvokeAsync(async () => await PerformLoginAsync());
                });
            }
        }

        #endregion

        #region Authentication Logic

        /// <summary>
        /// Perform user authentication
        /// </summary>
        private async Task PerformLoginAsync()
        {
            if (_isLoading) return;

            try
            {
                // Validate input
                if (!ValidateInput())
                    return;

                // Show loading state
                SetLoadingState(true);

                var username = txtUsername.Text.Trim();
                var password = txtPassword.Password;

                // Authenticate user
                var user = await AuthenticateUserAsync(username, password);

                if (user != null)
                {
                    // Login successful
                    await OnLoginSuccessAsync(user);
                }
                else
                {
                    // Login failed
                    ShowError("Invalid username or password. Please try again.");
                    txtPassword.Clear();
                    txtPassword.Focus();
                }
            }
            catch (Exception ex)
            {
                ShowError($"Login error: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        /// <summary>
        /// Validate login input
        /// </summary>
        /// <returns>True if valid</returns>
        private bool ValidateInput()
        {
            HideError();

            var username = txtUsername.Text.Trim();
            var password = txtPassword.Password;

            if (string.IsNullOrWhiteSpace(username))
            {
                ShowError("Please enter your username.");
                txtUsername.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                ShowError("Please enter your password.");
                txtPassword.Focus();
                return false;
            }

            if (username.Length < 3)
            {
                ShowError("Username must be at least 3 characters long.");
                txtUsername.Focus();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Authenticate user against database
        /// </summary>
        /// <param name="username">Username</param>
        /// <param name="password">Password</param>
        /// <returns>User if authenticated, null otherwise</returns>
        private async Task<User?> AuthenticateUserAsync(string username, string password)
        {
            try
            {
                // Find user by username
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

                if (user == null)
                    return null;

                // Check if user is active
                if (!user.IsActive)
                {
                    ShowError("Your account has been deactivated. Please contact administrator.");
                    return null;
                }

                // Verify password
                if (!user.VerifyPassword(password))
                    return null;

                // Update last login
                user.UpdateLastLogin();
                await _context.SaveChangesAsync();

                return user;
            }
            catch (Exception ex)
            {
                throw new Exception($"Authentication failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Handle successful login
        /// </summary>
        /// <param name="user">Authenticated user</param>
        private async Task OnLoginSuccessAsync(User user)
        {
            try
            {
                // Save login preferences if remember me is checked
                if (chkRememberMe.IsChecked == true)
                {
                    // TODO: Implement remember me functionality
                    // Could save username to app settings or registry
                }

                // Create and show main window
                var mainWindow = App.GetService<MainWindow>();
                mainWindow.SetCurrentUser(user);
                mainWindow.Show();

                // Close login window
                this.Close();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to open main window: {ex.Message}", ex);
            }
        }

        #endregion

        #region UI Helper Methods

        /// <summary>
        /// Set loading state
        /// </summary>
        /// <param name="isLoading">Loading state</param>
        private void SetLoadingState(bool isLoading)
        {
            _isLoading = isLoading;
            
            btnLogin.IsEnabled = !isLoading;
            txtUsername.IsEnabled = !isLoading;
            txtPassword.IsEnabled = !isLoading;
            chkRememberMe.IsEnabled = !isLoading;
            
            pnlLoading.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            
            if (isLoading)
            {
                HideError();
                Cursor = Cursors.Wait;
            }
            else
            {
                Cursor = Cursors.Arrow;
            }
        }

        /// <summary>
        /// Show error message
        /// </summary>
        /// <param name="message">Error message</param>
        private void ShowError(string message)
        {
            txtError.Text = message;
            txtError.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Hide error message
        /// </summary>
        private void HideError()
        {
            txtError.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Window Events

        protected override void OnClosed(EventArgs e)
        {
            // If login window is closed without successful login, exit application
            if (Application.Current.MainWindow == null || !Application.Current.MainWindow.IsVisible)
            {
                Application.Current.Shutdown();
            }
            
            base.OnClosed(e);
        }

        #endregion

        #region Development Helpers

        /// <summary>
        /// Load default credentials for development
        /// </summary>
        private void LoadDefaultCredentials()
        {
            #if DEBUG
            // Pre-fill with default admin credentials for development
            txtUsername.Text = "admin";
            txtPassword.Password = "admin123";
            #endif
        }

        /// <summary>
        /// Create default admin user if none exists
        /// </summary>
        public async Task EnsureDefaultUserExistsAsync()
        {
            try
            {
                var userExists = await _context.Users.AnyAsync();
                if (!userExists)
                {
                    var defaultAdmin = new User
                    {
                        Username = "admin",
                        FullName = "System Administrator",
                        Role = User.UserRole.Admin,
                        IsActive = true
                    };
                    defaultAdmin.SetPassword("admin123");

                    _context.Users.Add(defaultAdmin);
                    await _context.SaveChangesAsync();

                    ShowError("Default admin user created. Username: admin, Password: admin123");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Error creating default user: {ex.Message}");
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Show login window and ensure default user exists
        /// </summary>
        public static async Task<LoginWindow> ShowLoginWindowAsync()
        {
            var loginWindow = App.GetService<LoginWindow>();
            
            // Ensure default user exists for first run
            await loginWindow.EnsureDefaultUserExistsAsync();
            
            // Load default credentials in debug mode
            #if DEBUG
            loginWindow.LoadDefaultCredentials();
            #endif
            
            loginWindow.Show();
            return loginWindow;
        }

        /// <summary>
        /// Logout current user and show login window
        /// </summary>
        public static void ShowLoginForLogout()
        {
            // Close all other windows
            var windows = Application.Current.Windows;
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                var window = windows[i];
                if (window is not LoginWindow)
                {
                    window.Close();
                }
            }

            // Show login window
            var loginWindow = App.GetService<LoginWindow>();
            loginWindow.Show();
        }

        #endregion

        #region Input Validation

        /// <summary>
        /// Validate username format
        /// </summary>
        /// <param name="username">Username to validate</param>
        /// <returns>Validation error or null if valid</returns>
        private string? ValidateUsername(string username)
        {
            var error = ValidationHelper.ValidateRequired(username, "Username");
            if (error != null) return error;

            return ValidationHelper.ValidateUsername(username);
        }

        /// <summary>
        /// Validate password format
        /// </summary>
        /// <param name="password">Password to validate</param>
        /// <returns>Validation error or null if valid</returns>
        private string? ValidatePassword(string password)
        {
            var error = ValidationHelper.ValidateRequired(password, "Password");
            if (error != null) return error;

            return ValidationHelper.ValidatePassword(password, minLength: 3); // Relaxed for demo
        }

        #endregion

        #region Session Management

        /// <summary>
        /// Save user session information
        /// </summary>
        /// <param name="user">User to save session for</param>
        private void SaveUserSession(User user)
        {
            try
            {
                // TODO: Implement session management
                // Could use application properties or separate session store
                Application.Current.Properties["CurrentUser"] = user;
                Application.Current.Properties["LoginTime"] = DateTime.Now;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save user session: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear user session
        /// </summary>
        public static void ClearUserSession()
        {
            try
            {
                Application.Current.Properties.Remove("CurrentUser");
                Application.Current.Properties.Remove("LoginTime");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear user session: {ex.Message}");
            }
        }

        /// <summary>
        /// Get current user from session
        /// </summary>
        /// <returns>Current user or null if not logged in</returns>
        public static User? GetCurrentUser()
        {
            try
            {
                return Application.Current.Properties["CurrentUser"] as User;
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}