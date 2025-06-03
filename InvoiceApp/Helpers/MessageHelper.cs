using System;
using System.Windows;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace InvoiceApp.Helpers
{
    /// <summary>
    /// Helper class untuk menampilkan message dialog dengan styling yang konsisten
    /// </summary>
    public static class MessageHelper
    {
        /// <summary>
        /// Menampilkan pesan informasi
        /// </summary>
        /// <param name="message">Pesan yang akan ditampilkan</param>
        /// <param name="title">Judul dialog (optional)</param>
        /// <param name="owner">Parent window (optional)</param>
        public static void ShowInfo(string message, string title = "Informasi", Window? owner = null)
        {
            ShowMessage(message, title, MessageBoxImage.Information, owner);
        }

        /// <summary>
        /// Menampilkan pesan peringatan
        /// </summary>
        /// <param name="message">Pesan yang akan ditampilkan</param>
        /// <param name="title">Judul dialog (optional)</param>
        /// <param name="owner">Parent window (optional)</param>
        public static void ShowWarning(string message, string title = "Peringatan", Window? owner = null)
        {
            ShowMessage(message, title, MessageBoxImage.Warning, owner);
        }

        /// <summary>
        /// Menampilkan pesan error
        /// </summary>
        /// <param name="message">Pesan yang akan ditampilkan</param>
        /// <param name="title">Judul dialog (optional)</param>
        /// <param name="owner">Parent window (optional)</param>
        public static void ShowError(string message, string title = "Error", Window? owner = null)
        {
            ShowMessage(message, title, MessageBoxImage.Error, owner);
        }

        /// <summary>
        /// Menampilkan pesan sukses
        /// </summary>
        /// <param name="message">Pesan yang akan ditampilkan</param>
        /// <param name="title">Judul dialog (optional)</param>
        /// <param name="owner">Parent window (optional)</param>
        public static void ShowSuccess(string message, string title = "Sukses", Window? owner = null)
        {
            ShowMessage(message, title, MessageBoxImage.Information, owner);
        }

        /// <summary>
        /// Menampilkan dialog konfirmasi Yes/No
        /// </summary>
        /// <param name="message">Pesan konfirmasi</param>
        /// <param name="title">Judul dialog (optional)</param>
        /// <param name="owner">Parent window (optional)</param>
        /// <returns>True jika user memilih Yes, False jika No</returns>
        public static bool ShowConfirmation(string message, string title = "Konfirmasi", Window? owner = null)
        {
            var result = ShowMessageWithButtons(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, owner);
            return result == MessageBoxResult.Yes;
        }

        /// <summary>
        /// Menampilkan dialog konfirmasi dengan pilihan Yes/No/Cancel
        /// </summary>
        /// <param name="message">Pesan konfirmasi</param>
        /// <param name="title">Judul dialog (optional)</param>
        /// <param name="owner">Parent window (optional)</param>
        /// <returns>MessageBoxResult dari pilihan user</returns>
        public static MessageBoxResult ShowConfirmationWithCancel(string message, string title = "Konfirmasi", Window? owner = null)
        {
            return ShowMessageWithButtons(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question, owner);
        }

        /// <summary>
        /// Menampilkan dialog konfirmasi untuk delete
        /// </summary>
        /// <param name="itemName">Nama item yang akan dihapus</param>
        /// <param name="owner">Parent window (optional)</param>
        /// <returns>True jika user konfirmasi delete</returns>
        public static bool ShowDeleteConfirmation(string itemName, Window? owner = null)
        {
            var message = $"Apakah Anda yakin ingin menghapus '{itemName}'?\n\nTindakan ini tidak dapat dibatalkan.";
            return ShowConfirmation(message, "Konfirmasi Hapus", owner);
        }

        /// <summary>
        /// Menampilkan dialog konfirmasi untuk save changes
        /// </summary>
        /// <param name="owner">Parent window (optional)</param>
        /// <returns>MessageBoxResult dari pilihan user</returns>
        public static MessageBoxResult ShowSaveConfirmation(Window? owner = null)
        {
            var message = "Ada perubahan yang belum disimpan.\n\nApakah Anda ingin menyimpan perubahan?";
            return ShowConfirmationWithCancel(message, "Simpan Perubahan", owner);
        }

        /// <summary>
        /// Menampilkan pesan error dengan detail exception
        /// </summary>
        /// <param name="ex">Exception yang terjadi</param>
        /// <param name="userMessage">Pesan yang user-friendly (optional)</param>
        /// <param name="owner">Parent window (optional)</param>
        public static void ShowError(Exception ex, string? userMessage = null, Window? owner = null)
        {
            var message = userMessage ?? "Terjadi kesalahan yang tidak terduga.";
            
            // Add exception details in debug mode
            #if DEBUG
            message += $"\n\nDetail Error:\n{ex.Message}";
            if (ex.InnerException != null)
            {
                message += $"\n\nInner Exception:\n{ex.InnerException.Message}";
            }
            #endif

            ShowError(message, "Error", owner);
        }

        /// <summary>
        /// Menampilkan pesan loading/processing
        /// </summary>
        /// <param name="message">Pesan loading</param>
        /// <param name="title">Judul dialog (optional)</param>
        /// <returns>LoadingDialog instance untuk dismiss nanti</returns>
        public static LoadingDialog ShowLoading(string message = "Memproses...", string title = "Mohon Tunggu")
        {
            return new LoadingDialog(message, title);
        }

        /// <summary>
        /// Menampilkan pesan dengan multiple validasi errors
        /// </summary>
        /// <param name="errors">List of validation errors</param>
        /// <param name="title">Judul dialog (optional)</param>
        /// <param name="owner">Parent window (optional)</param>
        public static void ShowValidationErrors(System.Collections.Generic.List<string> errors, string title = "Validasi Error", Window? owner = null)
        {
            if (errors == null || errors.Count == 0)
                return;

            var message = "Terjadi kesalahan validasi:\n\n" + string.Join("\n• ", errors);
            ShowError(message, title, owner);
        }

        /// <summary>
        /// Base method untuk menampilkan message box
        /// </summary>
        private static void ShowMessage(string message, string title, MessageBoxImage icon, Window? owner)
        {
            if (Application.Current.Dispatcher.CheckAccess())
            {
                MessageBox.Show(owner ?? GetActiveWindow(), message, title, MessageBoxButton.OK, icon);
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(owner ?? GetActiveWindow(), message, title, MessageBoxButton.OK, icon);
                });
            }
        }

        /// <summary>
        /// Base method untuk menampilkan message box dengan custom buttons
        /// </summary>
        private static MessageBoxResult ShowMessageWithButtons(string message, string title, MessageBoxButton buttons, MessageBoxImage icon, Window? owner)
        {
            if (Application.Current.Dispatcher.CheckAccess())
            {
                return MessageBox.Show(owner ?? GetActiveWindow(), message, title, buttons, icon);
            }
            else
            {
                return Application.Current.Dispatcher.Invoke(() =>
                {
                    return MessageBox.Show(owner ?? GetActiveWindow(), message, title, buttons, icon);
                });
            }
        }

        /// <summary>
        /// Mendapatkan active window sebagai owner
        /// </summary>
        private static Window? GetActiveWindow()
        {
            return Application.Current?.Windows
                .Cast<Window>()
                .FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
        }

        /// <summary>
        /// Async version untuk menampilkan message (useful untuk async operations)
        /// </summary>
        public static async Task ShowInfoAsync(string message, string title = "Informasi", Window? owner = null)
        {
            await Task.Run(() => ShowInfo(message, title, owner));
        }

        /// <summary>
        /// Async version untuk menampilkan error message
        /// </summary>
        public static async Task ShowErrorAsync(string message, string title = "Error", Window? owner = null)
        {
            await Task.Run(() => ShowError(message, title, owner));
        }

        /// <summary>
        /// Async version untuk konfirmasi
        /// </summary>
        public static async Task<bool> ShowConfirmationAsync(string message, string title = "Konfirmasi", Window? owner = null)
        {
            return await Task.Run(() => ShowConfirmation(message, title, owner));
        }

        /// <summary>
        /// Helper method untuk format multiple messages
        /// </summary>
        public static string FormatMessages(System.Collections.Generic.IEnumerable<string> messages, string separator = "\n• ")
        {
            return string.Join(separator, messages);
        }

        /// <summary>
        /// Helper method untuk format exception message
        /// </summary>
        public static string FormatException(Exception ex, bool includeStackTrace = false)
        {
            var message = ex.Message;
            
            if (ex.InnerException != null)
            {
                message += $"\n\nInner Exception: {ex.InnerException.Message}";
            }
            
            if (includeStackTrace && !string.IsNullOrEmpty(ex.StackTrace))
            {
                message += $"\n\nStack Trace:\n{ex.StackTrace}";
            }
            
            return message;
        }
    }

    /// <summary>
    /// Simple loading dialog untuk menampilkan progress
    /// </summary>
    public class LoadingDialog : IDisposable
    {
        private Window? _loadingWindow;
        private bool _disposed;

        public LoadingDialog(string message, string title)
        {
            // Untuk implementasi sederhana, kita bisa menggunakan progress dialog
            // Atau bisa membuat custom window dengan ProgressBar
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Placeholder - implementasi loading dialog sederhana
                // Bisa diganti dengan custom window yang lebih menarik
                _loadingWindow = new Window
                {
                    Title = title,
                    Content = new System.Windows.Controls.TextBlock 
                    { 
                        Text = message,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(20)
                    },
                    Width = 300,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = MessageHelper.GetActiveWindow(),
                    ShowInTaskbar = false,
                    Topmost = true,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStyle = WindowStyle.ToolWindow
                };
                
                _loadingWindow.Show();
            });
        }

        public void UpdateMessage(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_loadingWindow?.Content is System.Windows.Controls.TextBlock textBlock)
                {
                    textBlock.Text = message;
                }
            });
        }

        public void Close()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _loadingWindow?.Close();
                    _loadingWindow = null;
                });
                _disposed = true;
            }
        }
    }
}