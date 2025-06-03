using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.ComponentModel.DataAnnotations;

namespace InvoiceApp.Helpers
{
    /// <summary>
    /// Helper class untuk validasi input dan data
    /// </summary>
    public static class ValidationHelper
    {
        // Regex patterns
        private static readonly Regex EmailRegex = new(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", RegexOptions.Compiled);
        private static readonly Regex PhoneRegex = new(@"^[\d\s\-\+\(\)]{8,15}$", RegexOptions.Compiled);
        private static readonly Regex PassportRegex = new(@"^[A-Z0-9]{6,12}$", RegexOptions.Compiled);
        private static readonly Regex NpwpRegex = new(@"^[\d\.\-]{15,20}$", RegexOptions.Compiled);
        private static readonly Regex InvoiceNumberRegex = new(@"^[A-Z]{2,5}\/\d{2}\/\d{2}\/\d{3,4}$", RegexOptions.Compiled);
        private static readonly Regex UsernameRegex = new(@"^[a-zA-Z0-9_]{3,20}$", RegexOptions.Compiled);

        /// <summary>
        /// Validasi string kosong atau null
        /// </summary>
        /// <param name="value">Value to validate</param>
        /// <param name="fieldName">Nama field untuk error message</param>
        /// <returns>Error message atau null jika valid</returns>
        public static string? ValidateRequired(string? value, string fieldName)
        {
            return string.IsNullOrWhiteSpace(value) ? $"{fieldName} tidak boleh kosong" : null;
        }

        /// <summary>
        /// Validasi panjang string
        /// </summary>
        /// <param name="value">Value to validate</param>
        /// <param name="fieldName">Nama field</param>
        /// <param name="minLength">Minimum length</param>
        /// <param name="maxLength">Maximum length</param>
        /// <returns>Error message atau null jika valid</returns>
        public static string? ValidateLength(string? value, string fieldName, int minLength = 0, int maxLength = int.MaxValue)
        {
            if (string.IsNullOrEmpty(value))
                return null; // Use ValidateRequired for null checks

            var length = value.Length;
            
            if (length < minLength)
                return $"{fieldName} minimal {minLength} karakter";
                
            if (length > maxLength)
                return $"{fieldName} maksimal {maxLength} karakter";
                
            return null;
        }

        /// <summary>
        /// Validasi format email
        /// </summary>
        /// <param name="email">Email address</param>
        /// <returns>Error message atau null jika valid</returns>
        public static string? ValidateEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return null; // Use ValidateRequired for null checks

            return EmailRegex.IsMatch(email) ? null : "Format email tidak valid";
        }

        /// <summary>
        /// Validasi format nomor telepon
        /// </summary>
        /// <param name="phone">Phone number</param>
        /// <returns>Error message atau null jika valid</returns>
        public static string? ValidatePhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return null; // Use ValidateRequired for null checks

            return PhoneRegex.IsMatch(phone) ? null : "Format nomor telepon tidak valid";
        }

        /// <summary>
        /// Validasi format passport
        /// </summary>
        /// <param name="passport">Passport number</param>
        /// <returns>Error message atau null jika valid</returns>
        public static string? ValidatePassport(string? passport)
        {
            if (string.IsNullOrWhiteSpace(passport))
                return null; // Use ValidateRequired for null checks

            var cleaned = passport.Replace(" ", "").Replace("-", "").ToUpper();
            return PassportRegex.IsMatch(cleaned) ? null : "Format passport tidak valid (6-12 karakter, huruf dan angka)";
        }

        /// <summary>
        /// Validasi format NPWP
        /// </summary>
        /// <param name="npwp">NPWP number</param>
        /// <returns>Error message atau null jika valid</returns>
        public static string? ValidateNpwp(string? npwp)
        {
            if (string.IsNullOrWhiteSpace(npwp))
                return null; // Use ValidateRequired for null checks

            return NpwpRegex.IsMatch(npwp) ? null : "Format NPWP tidak valid";
        }

        /// <summary>
        /// Validasi format username
        /// </summary>
        /// <param name="username">Username</param>
        /// <returns>Error message atau null jika valid</returns>
        public static string? ValidateUsername(string? username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return null; // Use ValidateRequired for null checks

            if (!UsernameRegex.IsMatch(username))
                return "Username hanya boleh huruf, angka, dan underscore (3-20 karakter)";

            // Additional checks
            if (username.StartsWith("_") || username.EndsWith("_"))
                return "Username tidak boleh diawali atau diakhiri dengan underscore";

            return null;
        }

        /// <summary>
        /// Validasi kekuatan password
        /// </summary>
        /// <param name="password">Password</param>
        /// <param name="minLength">Minimum length (default 6)</param>
        /// <returns>Error message atau null jika valid</returns>
        public static string? ValidatePassword(string? password, int minLength = 6)
        {
            if (string.IsNullOrWhiteSpace(password))
                return null; // Use ValidateRequired for null checks

            if (password.Length < minLength)
                return $"Password minimal {minLength} karakter";

            // Check for at least one letter and one number (optional, bisa di-adjust)
            var hasLetter = password.Any(char.IsLetter);
            var hasDigit = password.Any(char.IsDigit);

            if (!hasLetter || !hasDigit)
                return "Password harus mengandung huruf dan angka";

            return null;
        }

        /// <summary>
        /// Validasi format nomor invoice
        /// </summary>
        /// <param name="invoiceNumber">Invoice number</param>
        /// <returns>Error message atau null jika valid</returns>
        public static string? ValidateInvoiceNumber(string? invoiceNumber)
        {
            if (string.IsNullOrWhiteSpace(invoiceNumber))
                return null; // Use ValidateRequired for null checks

            return InvoiceNumberRegex.IsMatch(invoiceNumber) ? null : "Format nomor invoice tidak valid (contoh: FSN/24/01/001)";
        }

        /// <summary>
        /// Validasi range angka
        /// </summary>
        /// <param name="value">Value to validate</param>
        /// <param name="fieldName">Field name</param>
        /// <param name="min">Minimum value</param>
        /// <param name="max">Maximum value</param>
        /// <returns>Error message atau null jika valid</returns>
        public static string? ValidateRange(decimal value, string fieldName, decimal min = decimal.MinValue, decimal max = decimal.MaxValue)
        {
            if (value < min)
                return $"{fieldName} tidak boleh kurang dari {min:N0}";
                
            if (value > max)
                return $"{fieldName} tidak boleh lebih dari {max:N0}";
                
            return null;
        }

        /// <summary>
        /// Validasi range angka integer
        /// </summary>
        /// <param name="value">Value to validate</param>
        /// <param name="fieldName">Field name</param>
        /// <param name="min">Minimum value</param>
        /// <param name="max">Maximum value</param>
        /// <returns>Error message atau null jika valid</returns>
        public static string? ValidateRange(int value, string fieldName, int min = int.MinValue, int max = int.MaxValue)
        {
            if (value < min)
                return $"{fieldName} tidak boleh kurang dari {min}";
                
            if (value > max)
                return $"{fieldName} tidak boleh lebih dari {max}";
                
            return null;
        }

        /// <summary>
        /// Validasi range tanggal
        /// </summary>
        /// <param name="date">Date to validate</param>
        /// <param name="fieldName">Field name</param>
        /// <param name="minDate">Minimum date</param>
        /// <param name="maxDate">Maximum date</param>
        /// <returns>Error message atau null jika valid</returns>
        public static string? ValidateDateRange(DateTime date, string fieldName, DateTime? minDate = null, DateTime? maxDate = null)
        {
            minDate ??= new DateTime(1900, 1, 1);
            maxDate ??= DateTime.Now.AddYears(10);

            if (date < minDate)
                return $"{fieldName} tidak boleh kurang dari {minDate:dd/MM/yyyy}";
                
            if (date > maxDate)
                return $"{fieldName} tidak boleh lebih dari {maxDate:dd/MM/yyyy}";
                
            return null;
        }

        /// <summary>
        /// Validasi menggunakan Data Annotations
        /// </summary>
        /// <param name="obj">Object to validate</param>
        /// <returns>List of validation errors</returns>
        public static List<string> ValidateObject(object obj)
        {
            var errors = new List<string>();
            var validationContext = new ValidationContext(obj);
            var results = new List<ValidationResult>();

            if (!Validator.TryValidateObject(obj, validationContext, results, true))
            {
                errors.AddRange(results.Select(r => r.ErrorMessage ?? "Validation error"));
            }

            return errors;
        }

        /// <summary>
        /// Validasi field khusus untuk TKA Worker
        /// </summary>
        /// <param name="nama">Nama TKA</param>
        /// <param name="passport">Nomor passport</param>
        /// <param name="divisi">Divisi (optional)</param>
        /// <returns>List of validation errors</returns>
        public static List<string> ValidateTkaWorker(string? nama, string? passport, string? divisi = null)
        {
            var errors = new List<string>();

            var namaError = ValidateRequired(nama, "Nama");
            if (namaError != null) errors.Add(namaError);

            var namaLengthError = ValidateLength(nama, "Nama", 2, 100);
            if (namaLengthError != null) errors.Add(namaLengthError);

            var passportError = ValidateRequired(passport, "Passport");
            if (passportError != null) errors.Add(passportError);

            var passportFormatError = ValidatePassport(passport);
            if (passportFormatError != null) errors.Add(passportFormatError);

            if (!string.IsNullOrWhiteSpace(divisi))
            {
                var divisiLengthError = ValidateLength(divisi, "Divisi", 1, 100);
                if (divisiLengthError != null) errors.Add(divisiLengthError);
            }

            return errors;
        }

        /// <summary>
        /// Validasi field khusus untuk Company
        /// </summary>
        /// <param name="companyName">Nama perusahaan</param>
        /// <param name="npwp">NPWP</param>
        /// <param name="idtku">IDTKU</param>
        /// <param name="address">Alamat</param>
        /// <returns>List of validation errors</returns>
        public static List<string> ValidateCompany(string? companyName, string? npwp, string? idtku, string? address)
        {
            var errors = new List<string>();

            var nameError = ValidateRequired(companyName, "Nama Perusahaan");
            if (nameError != null) errors.Add(nameError);

            var nameLengthError = ValidateLength(companyName, "Nama Perusahaan", 2, 200);
            if (nameLengthError != null) errors.Add(nameLengthError);

            var npwpError = ValidateRequired(npwp, "NPWP");
            if (npwpError != null) errors.Add(npwpError);

            var npwpFormatError = ValidateNpwp(npwp);
            if (npwpFormatError != null) errors.Add(npwpFormatError);

            var idtkuError = ValidateRequired(idtku, "IDTKU");
            if (idtkuError != null) errors.Add(idtkuError);

            var idtkuLengthError = ValidateLength(idtku, "IDTKU", 1, 20);
            if (idtkuLengthError != null) errors.Add(idtkuLengthError);

            var addressError = ValidateRequired(address, "Alamat");
            if (addressError != null) errors.Add(addressError);

            return errors;
        }

        /// <summary>
        /// Validasi field khusus untuk Job Description
        /// </summary>
        /// <param name="jobName">Nama job</param>
        /// <param name="jobDescription">Deskripsi job</param>
        /// <param name="price">Harga</param>
        /// <returns>List of validation errors</returns>
        public static List<string> ValidateJobDescription(string? jobName, string? jobDescription, decimal price)
        {
            var errors = new List<string>();

            var nameError = ValidateRequired(jobName, "Job Name");
            if (nameError != null) errors.Add(nameError);

            var nameLengthError = ValidateLength(jobName, "Job Name", 2, 200);
            if (nameLengthError != null) errors.Add(nameLengthError);

            var descError = ValidateRequired(jobDescription, "Job Description");
            if (descError != null) errors.Add(descError);

            var priceError = ValidateRange(price, "Harga", 0);
            if (priceError != null) errors.Add(priceError);

            return errors;
        }

        /// <summary>
        /// Validasi field khusus untuk User
        /// </summary>
        /// <param name="username">Username</param>
        /// <param name="fullName">Nama lengkap</param>
        /// <param name="password">Password (optional untuk update)</param>
        /// <param name="role">Role</param>
        /// <returns>List of validation errors</returns>
        public static List<string> ValidateUser(string? username, string? fullName, string? password, string? role)
        {
            var errors = new List<string>();

            var usernameError = ValidateRequired(username, "Username");
            if (usernameError != null) errors.Add(usernameError);

            var usernameFormatError = ValidateUsername(username);
            if (usernameFormatError != null) errors.Add(usernameFormatError);

            var nameError = ValidateRequired(fullName, "Nama Lengkap");
            if (nameError != null) errors.Add(nameError);

            var nameLengthError = ValidateLength(fullName, "Nama Lengkap", 2, 100);
            if (nameLengthError != null) errors.Add(nameLengthError);

            if (!string.IsNullOrWhiteSpace(password))
            {
                var passwordError = ValidatePassword(password);
                if (passwordError != null) errors.Add(passwordError);
            }

            var roleError = ValidateRequired(role, "Role");
            if (roleError != null) errors.Add(roleError);

            return errors;
        }

        /// <summary>
        /// Validasi field khusus untuk Bank Account
        /// </summary>
        /// <param name="bankName">Nama bank</param>
        /// <param name="accountNumber">Nomor rekening</param>
        /// <param name="accountName">Nama pemilik rekening</param>
        /// <returns>List of validation errors</returns>
        public static List<string> ValidateBankAccount(string? bankName, string? accountNumber, string? accountName)
        {
            var errors = new List<string>();

            var bankNameError = ValidateRequired(bankName, "Nama Bank");
            if (bankNameError != null) errors.Add(bankNameError);

            var bankNameLengthError = ValidateLength(bankName, "Nama Bank", 2, 100);
            if (bankNameLengthError != null) errors.Add(bankNameLengthError);

            var accountNumberError = ValidateRequired(accountNumber, "Nomor Rekening");
            if (accountNumberError != null) errors.Add(accountNumberError);

            var accountNumberLengthError = ValidateLength(accountNumber, "Nomor Rekening", 8, 50);
            if (accountNumberLengthError != null) errors.Add(accountNumberLengthError);

            var accountNameError = ValidateRequired(accountName, "Nama Pemilik Rekening");
            if (accountNameError != null) errors.Add(accountNameError);

            var accountNameLengthError = ValidateLength(accountName, "Nama Pemilik Rekening", 2, 100);
            if (accountNameLengthError != null) errors.Add(accountNameLengthError);

            return errors;
        }

        /// <summary>
        /// Helper method untuk membersihkan input
        /// </summary>
        /// <param name="input">Input string</param>
        /// <returns>Cleaned string</returns>
        public static string CleanInput(string? input)
        {
            return input?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// Helper method untuk kapitalisasi proper (Title Case)
        /// </summary>
        /// <param name="input">Input string</param>
        /// <returns>Title case string</returns>
        public static string ToProperCase(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var words = input.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    words[i] = char.ToUpper(words[i][0]) + words[i][1..];
                }
            }
            return string.Join(" ", words);
        }

        /// <summary>
        /// Validasi multiple fields sekaligus dan return hasil gabungan
        /// </summary>
        /// <param name="validations">Dictionary of field name and validation function</param>
        /// <returns>List of all validation errors</returns>
        public static List<string> ValidateMultiple(Dictionary<string, Func<string?>> validations)
        {
            var errors = new List<string>();
            
            foreach (var validation in validations)
            {
                var error = validation.Value();
                if (error != null)
                    errors.Add(error);
            }
            
            return errors;
        }
    }
}