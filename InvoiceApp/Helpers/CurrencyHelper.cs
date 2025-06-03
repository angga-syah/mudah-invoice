using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace InvoiceApp.Helpers
{
    /// <summary>
    /// Helper class untuk formatting dan konversi mata uang, khususnya Rupiah
    /// </summary>
    public static class CurrencyHelper
    {
        private static readonly CultureInfo IndonesianCulture = new("id-ID");
        
        // Regex untuk parsing currency input
        private static readonly Regex CurrencyRegex = new(@"[^\d,.-]", RegexOptions.Compiled);
        private static readonly Regex DigitOnlyRegex = new(@"[^\d]", RegexOptions.Compiled);

        /// <summary>
        /// Format decimal ke format mata uang Rupiah dengan pemisah ribuan
        /// </summary>
        /// <param name="amount">Jumlah dalam decimal</param>
        /// <param name="includePrefix">Apakah menyertakan prefix "Rp"</param>
        /// <param name="includeDecimal">Apakah menyertakan desimal (biasanya false untuk Rupiah)</param>
        /// <returns>String format mata uang</returns>
        public static string FormatCurrency(decimal amount, bool includePrefix = true, bool includeDecimal = false)
        {
            // Aturan pembulatan khusus: 18.000,49 → 18.000 | 18.000,50 → 18.001
            var rounded = ApplyInvoiceRounding(amount);
            
            var formatString = includeDecimal ? "N2" : "N0";
            var formatted = rounded.ToString(formatString, IndonesianCulture);
            
            return includePrefix ? $"Rp {formatted}" : formatted;
        }

        /// <summary>
        /// Format untuk display dalam input/textbox (tanpa prefix Rp)
        /// </summary>
        /// <param name="amount">Jumlah dalam decimal</param>
        /// <returns>String format untuk input</returns>
        public static string FormatForInput(decimal amount)
        {
            return FormatCurrency(amount, includePrefix: false, includeDecimal: false);
        }

        /// <summary>
        /// Format untuk display dalam grid/laporan (dengan prefix Rp)
        /// </summary>
        /// <param name="amount">Jumlah dalam decimal</param>
        /// <returns>String format untuk display</returns>
        public static string FormatForDisplay(decimal amount)
        {
            return FormatCurrency(amount, includePrefix: true, includeDecimal: false);
        }

        /// <summary>
        /// Parse string currency input menjadi decimal
        /// Menangani berbagai format input: "1.000.000", "1,000,000", "Rp 1.000.000", dll
        /// </summary>
        /// <param name="input">String input dari user</param>
        /// <param name="result">Hasil parsing dalam decimal</param>
        /// <returns>True jika berhasil parse, false jika gagal</returns>
        public static bool TryParseCurrency(string input, out decimal result)
        {
            result = 0;
            
            if (string.IsNullOrWhiteSpace(input))
                return false;

            try
            {
                // Remove currency symbols (Rp, $, etc.)
                var cleaned = CurrencyRegex.Replace(input.Trim(), "");
                
                // Handle different decimal separators
                // Indonesia uses "." for thousands and "," for decimal
                // But users might input various formats
                
                // If there's both comma and dot, determine which is decimal separator
                if (cleaned.Contains(',') && cleaned.Contains('.'))
                {
                    // Find last separator - that's likely the decimal separator
                    var lastComma = cleaned.LastIndexOf(',');
                    var lastDot = cleaned.LastIndexOf('.');
                    
                    if (lastDot > lastComma)
                    {
                        // Dot is decimal separator: "1,000.50"
                        cleaned = cleaned.Replace(",", "").Replace(".", ",");
                    }
                    else
                    {
                        // Comma is decimal separator: "1.000,50"
                        cleaned = cleaned.Replace(".", "");
                    }
                }
                else if (cleaned.Contains('.'))
                {
                    // Only dots - check if it's decimal or thousands separator
                    var parts = cleaned.Split('.');
                    if (parts.Length == 2 && parts[1].Length <= 2)
                    {
                        // Likely decimal: "1000.50"
                        cleaned = cleaned.Replace(".", ",");
                    }
                    else
                    {
                        // Thousands separator: "1.000.000"
                        cleaned = cleaned.Replace(".", "");
                    }
                }
                // If only commas, treat as decimal separator for small amounts or thousands for large

                // Parse using Indonesian culture
                if (decimal.TryParse(cleaned, NumberStyles.Currency, IndonesianCulture, out result))
                    return true;
                
                // Fallback to invariant culture
                if (decimal.TryParse(cleaned, NumberStyles.Currency, CultureInfo.InvariantCulture, out result))
                    return true;
                
                // Last attempt: parse only digits
                var digitsOnly = DigitOnlyRegex.Replace(cleaned, "");
                if (decimal.TryParse(digitsOnly, out result))
                    return true;
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Parse string currency input menjadi decimal dengan exception jika gagal
        /// </summary>
        /// <param name="input">String input dari user</param>
        /// <returns>Decimal value</returns>
        /// <exception cref="FormatException">Jika format tidak valid</exception>
        public static decimal ParseCurrency(string input)
        {
            if (TryParseCurrency(input, out var result))
                return result;
                
            throw new FormatException($"Cannot parse '{input}' as currency value");
        }

        /// <summary>
        /// Terapkan aturan pembulatan khusus untuk invoice
        /// Aturan: 18.000,49 → 18.000 | 18.000,50 → 18.001
        /// </summary>
        /// <param name="amount">Jumlah asli</param>
        /// <returns>Jumlah setelah pembulatan</returns>
        public static decimal ApplyInvoiceRounding(decimal amount)
        {
            var rounded = Math.Round(amount, 0, MidpointRounding.AwayFromZero);
            var difference = amount - rounded;
            
            // Jika selisih >= 0.50, bulatkan ke atas
            if (difference >= 0.50m)
                return rounded + 1;
            else
                return rounded;
        }

        /// <summary>
        /// Konversi angka ke terbilang dalam Bahasa Indonesia
        /// </summary>
        /// <param name="amount">Jumlah dalam decimal</param>
        /// <returns>Terbilang dalam Bahasa Indonesia</returns>
        public static string ConvertToWords(decimal amount)
        {
            if (amount == 0)
                return "Nol";

            // Apply rounding first
            var rounded = ApplyInvoiceRounding(amount);
            var intAmount = (long)rounded;
            
            if (intAmount < 0)
                return "Minus " + ConvertToWords(Math.Abs(amount));

            return ConvertIntegerToWords(intAmount);
        }

        private static string ConvertIntegerToWords(long number)
        {
            if (number == 0)
                return "";

            // Array for number names
            string[] ones = { "", "Satu", "Dua", "Tiga", "Empat", "Lima", "Enam", "Tujuh", "Delapan", "Sembilan", 
                             "Sepuluh", "Sebelas", "Dua Belas", "Tiga Belas", "Empat Belas", "Lima Belas", 
                             "Enam Belas", "Tujuh Belas", "Delapan Belas", "Sembilan Belas" };

            string[] tens = { "", "", "Dua Puluh", "Tiga Puluh", "Empat Puluh", "Lima Puluh", 
                             "Enam Puluh", "Tujuh Puluh", "Delapan Puluh", "Sembilan Puluh" };

            var result = new StringBuilder();

            // Triliyun
            if (number >= 1000000000000)
            {
                var triliunPart = number / 1000000000000;
                if (triliunPart == 1)
                    result.Append("Satu Triliyun ");
                else
                    result.Append($"{ConvertIntegerToWords(triliunPart)} Triliyun ");
                number %= 1000000000000;
            }

            // Miliar
            if (number >= 1000000000)
            {
                var miliarPart = number / 1000000000;
                if (miliarPart == 1)
                    result.Append("Satu Miliar ");
                else
                    result.Append($"{ConvertIntegerToWords(miliarPart)} Miliar ");
                number %= 1000000000;
            }

            // Juta
            if (number >= 1000000)
            {
                var jutaPart = number / 1000000;
                if (jutaPart == 1)
                    result.Append("Satu Juta ");
                else
                    result.Append($"{ConvertIntegerToWords(jutaPart)} Juta ");
                number %= 1000000;
            }

            // Ribu
            if (number >= 1000)
            {
                var ribuPart = number / 1000;
                if (ribuPart == 1)
                    result.Append("Seribu ");
                else
                    result.Append($"{ConvertIntegerToWords(ribuPart)} Ribu ");
                number %= 1000;
            }

            // Ratus
            if (number >= 100)
            {
                var ratusPart = number / 100;
                if (ratusPart == 1)
                    result.Append("Seratus ");
                else
                    result.Append($"{ones[ratusPart]} Ratus ");
                number %= 100;
            }

            // Puluhan dan satuan
            if (number >= 20)
            {
                var tensPart = number / 10;
                result.Append($"{tens[tensPart]} ");
                number %= 10;
                if (number > 0)
                    result.Append($"{ones[number]} ");
            }
            else if (number > 0)
            {
                result.Append($"{ones[number]} ");
            }

            return result.ToString().Trim();
        }

        /// <summary>
        /// Format terbilang lengkap untuk invoice (dengan "Rupiah" di akhir)
        /// </summary>
        /// <param name="amount">Jumlah dalam decimal</param>
        /// <returns>Terbilang lengkap dengan "Rupiah"</returns>
        public static string ConvertToWordsForInvoice(decimal amount)
        {
            var words = ConvertToWords(amount);
            return string.IsNullOrEmpty(words) ? "Nol Rupiah" : $"{words} Rupiah";
        }

        /// <summary>
        /// Validasi apakah input adalah format currency yang valid
        /// </summary>
        /// <param name="input">String input</param>
        /// <returns>True jika valid</returns>
        public static bool IsValidCurrencyFormat(string input)
        {
            return TryParseCurrency(input, out _);
        }

        /// <summary>
        /// Membersihkan input currency dari karakter yang tidak diperlukan
        /// </summary>
        /// <param name="input">String input</param>
        /// <returns>String yang sudah dibersihkan</returns>
        public static string CleanCurrencyInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "0";

            if (TryParseCurrency(input, out var result))
                return FormatForInput(result);

            // Jika tidak bisa di-parse, kembalikan hanya angka
            var digitsOnly = DigitOnlyRegex.Replace(input, "");
            return string.IsNullOrEmpty(digitsOnly) ? "0" : digitsOnly;
        }

        /// <summary>
        /// Hitung PPN berdasarkan subtotal dan persentase
        /// </summary>
        /// <param name="subtotal">Subtotal sebelum PPN</param>
        /// <param name="vatPercentage">Persentase PPN (default 11%)</param>
        /// <returns>Jumlah PPN yang sudah dibulatkan</returns>
        public static decimal CalculateVAT(decimal subtotal, decimal vatPercentage = 11.00m)
        {
            var vatAmount = subtotal * vatPercentage / 100;
            return ApplyInvoiceRounding(vatAmount);
        }

        /// <summary>
        /// Hitung total dengan PPN
        /// </summary>
        /// <param name="subtotal">Subtotal sebelum PPN</param>
        /// <param name="vatPercentage">Persentase PPN</param>
        /// <returns>Total setelah PPN</returns>
        public static decimal CalculateTotal(decimal subtotal, decimal vatPercentage = 11.00m)
        {
            var vatAmount = CalculateVAT(subtotal, vatPercentage);
            return subtotal + vatAmount;
        }
    }
}