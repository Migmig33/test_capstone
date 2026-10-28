using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace WebApplication1.Helper
{
    /// <summary>
    /// Server-side input rules. They mirror the rule table in Scripts/Scripts/InputRules.js, so a value
    /// the browser accepts is accepted here, and a value sent around the UI is rejected before it is saved.
    /// Each check returns null when the value is valid, or the message to show the user.
    /// </summary>
    public static class InputValidator
    {
        private static readonly Regex NameFormat = new Regex(@"^\p{L}+( \p{L}+)*$", RegexOptions.Compiled);
        private static readonly Regex PhoneFormat = new Regex(@"^09[0-9]{9}$", RegexOptions.Compiled);
        private static readonly Regex EmailFormat = new Regex(@"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}$", RegexOptions.Compiled);
        private static readonly Regex CodeChars = new Regex(@"^[\p{L}\p{N} .\-#/&]+$", RegexOptions.Compiled);
        private static readonly Regex AddressChars = new Regex(@"^[\p{L}\p{N} .,#/\-()'&]+$", RegexOptions.Compiled);
        private static readonly Regex OccupationChars = new Regex(@"^[\p{L} \-/&().']+$", RegexOptions.Compiled);
        private static readonly Regex WholeNumberFormat = new Regex(@"^[0-9]+$", RegexOptions.Compiled);
        private static readonly Regex DecimalFormat = new Regex(@"^[0-9]+(\.[0-9]+)?$", RegexOptions.Compiled);
        private static readonly Regex OtpFormat = new Regex(@"^[0-9]{6}$", RegexOptions.Compiled);
        private static readonly Regex ControlChars = new Regex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F]", RegexOptions.Compiled);

        // ---------- Cleaning (applied before saving) ----------

        /// <summary>Trims the ends and collapses runs of spaces to one.</summary>
        public static string CleanText(string value)
        {
            if (value == null) return null;
            return Regex.Replace(value.Trim(), " {2,}", " ");
        }

        /// <summary>Removes control characters and trims. Line breaks are kept.</summary>
        public static string CleanDescription(string value)
        {
            if (value == null) return null;
            return ControlChars.Replace(value, string.Empty).Trim();
        }

        // ---------- Checks ----------

        /// <summary>Letters (any language, including ñ) and single spaces. 2 to 50 characters.</summary>
        public static string Name(string value, string label = "Name", bool required = true)
        {
            var text = CleanText(value) ?? string.Empty;
            if (text.Length == 0) return required ? label + " is required." : null;
            if (!NameFormat.IsMatch(text)) return label + " can only contain letters and single spaces.";
            return LengthMessage(text, label, 2, 50);
        }

        /// <summary>Philippine mobile number: 09 followed by 9 digits.</summary>
        public static string Phone(string value, string label = "Phone number", bool required = false)
        {
            var text = CleanText(value) ?? string.Empty;
            if (text.Length == 0) return required ? label + " is required." : null;
            return PhoneFormat.IsMatch(text) ? null : label + " must be 11 digits and start with 09 (e.g. 09171234567).";
        }

        /// <summary>Email address, at most 100 characters.</summary>
        public static string Email(string value, string label = "Email", bool required = true)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length == 0) return required ? label + " is required." : null;
            if (!EmailFormat.IsMatch(text)) return label + " must be a valid email address (e.g. name@example.com).";
            return text.Length > 100 ? label + " can't exceed 100 characters." : null;
        }

        /// <summary>Unit names, floors, amenity names: letters, digits, spaces and . - # /.</summary>
        public static string Code(string value, string label = "Value", bool required = true, int max = 50)
        {
            var text = CleanText(value) ?? string.Empty;
            if (text.Length == 0) return required ? label + " is required." : null;
            if (!CodeChars.IsMatch(text)) return label + " can only contain letters, numbers, spaces and . - # / &.";
            return LengthMessage(text, label, 1, max);
        }

        /// <summary>Street, barangay, city, province: letters, digits and common address punctuation.</summary>
        public static string Address(string value, string label = "Address", bool required = false, int max = 150)
        {
            var text = CleanText(value) ?? string.Empty;
            if (text.Length == 0) return required ? label + " is required." : null;
            if (!AddressChars.IsMatch(text)) return label + " can only contain letters, numbers and . , # / - ( ) ' &.";
            return LengthMessage(text, label, 1, max);
        }

        /// <summary>Occupation titles: letters, spaces and - / &amp; ( ) . '.</summary>
        public static string Occupation(string value, string label = "Occupation", bool required = false, int max = 25)
        {
            var text = CleanText(value) ?? string.Empty;
            if (text.Length == 0) return required ? label + " is required." : null;
            if (!OccupationChars.IsMatch(text)) return label + " can only contain letters, spaces and - / & ( ) . '.";
            return LengthMessage(text, label, 1, max);
        }

        /// <summary>Free text such as notes, reasons and descriptions.</summary>
        public static string Description(string value, string label = "Description", bool required = false, int max = 1000)
        {
            var text = CleanDescription(value) ?? string.Empty;
            if (text.Length == 0) return required ? label + " is required." : null;
            return LengthMessage(text, label, 1, max);
        }

        /// <summary>Whole number inside [min, max].</summary>
        public static string Integer(string value, string label, long min, long max, bool required = true)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length == 0) return required ? label + " is required." : null;
            if (!WholeNumberFormat.IsMatch(text)) return label + " must be a whole number.";
            decimal number;
            if (!decimal.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number)) return label + " must be a whole number.";
            if (number < min) return label + " must be at least " + min + ".";
            if (number > max) return label + " cannot be more than " + max + ".";
            return null;
        }

        /// <summary>Number with at most <paramref name="decimals"/> decimal places, inside [min, max].</summary>
        public static string Decimal(string value, string label, decimal min, decimal max, int decimals = 2, bool required = true)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.EndsWith(".")) text = text.Substring(0, text.Length - 1);
            if (text.Length == 0) return required ? label + " is required." : null;
            if (!DecimalFormat.IsMatch(text)) return label + " must be a number (e.g. 1500 or 1500.50).";

            var dot = text.IndexOf('.');
            if (dot != -1 && text.Length - dot - 1 > decimals)
            {
                return label + " can have at most " + decimals + " decimal place" + (decimals == 1 ? "" : "s") + ".";
            }

            decimal number;
            if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number)) return label + " is too large.";
            if (number < min) return label + " must be at least " + min + ".";
            if (number > max) return label + " cannot be more than " + max + ".";
            return null;
        }

        /// <summary>Passwords may contain any character. Only the length is checked.</summary>
        public static string Password(string value, string label = "Password", bool required = true, int min = 8, int max = 64)
        {
            if (string.IsNullOrEmpty(value)) return required ? label + " is required." : null;
            return LengthMessage(value, label, min, max);
        }

        /// <summary>Six-digit verification code.</summary>
        public static string Otp(string value, string label = "Verification code")
        {
            return OtpFormat.IsMatch((value ?? string.Empty).Trim()) ? null : label + " must be 6 digits.";
        }

        /// <summary>Returns the first message from the list, or null when every entry is valid.</summary>
        public static string FirstError(params string[] messages)
        {
            foreach (var message in messages)
            {
                if (!string.IsNullOrEmpty(message)) return message;
            }
            return null;
        }

        private static string LengthMessage(string text, string label, int min, int max)
        {
            if (text.Length < min) return label + " must be at least " + min + " characters.";
            if (text.Length > max) return label + " can't exceed " + max + " characters.";
            return null;
        }
    }
}
