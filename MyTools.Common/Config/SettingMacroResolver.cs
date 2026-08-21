using System.Globalization;
using System.Text;

namespace MyTools.Common.Config;

/// <summary>
/// Resolves setting macros. Default-value replacement currently supports
/// <c>${DateTime.Now}</c>. Configuration visibility uses a <c>visibility</c>
/// condition such as <c>${ChromeEnabled == true}</c>.
/// </summary>
public static class SettingMacroResolver
{
    public const string DateTimeNow = "${DateTime.Now}";

    public static string Resolve(string? value, DateTime? now = null)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains(DateTimeNow, StringComparison.Ordinal))
        {
            return value ?? string.Empty;
        }

        var timestamp = (now ?? DateTime.Now).ToString("O");
        return value.Replace(DateTimeNow, timestamp, StringComparison.Ordinal);
    }

    /// <summary>
    /// Evaluates a configuration <c>visibility</c> condition. Empty or missing
    /// macros are visible. The expression must be a single <c>${...}</c> block
    /// that can reference sibling setting keys of the current plugin.
    /// </summary>
    public static bool EvaluateVisibility(string? visibility, IReadOnlyDictionary<string, object?> values)
    {
        if (string.IsNullOrWhiteSpace(visibility))
        {
            return true;
        }

        if (!TryGetSingleMacro(visibility, out var expression) || string.IsNullOrWhiteSpace(expression))
        {
            return true;
        }

        try
        {
            return new VisibilityParser(expression, values).ParseExpression();
        }
        catch
        {
            return true;
        }
    }

    internal static bool TryGetSingleMacro(string value, out string expression)
    {
        expression = string.Empty;
        var trimmed = value.Trim();
        if (trimmed.Length < 3 || !trimmed.StartsWith("${", StringComparison.Ordinal) || !trimmed.EndsWith('}'))
        {
            return false;
        }

        var inner = trimmed[2..^1];
        if (inner.Contains("${", StringComparison.Ordinal))
        {
            return false;
        }

        expression = inner.Trim();
        return true;
    }

    private sealed class VisibilityParser
    {
        private readonly string _source;
        private readonly IReadOnlyDictionary<string, object?> _values;
        private int _index;

        public VisibilityParser(string source, IReadOnlyDictionary<string, object?> values)
        {
            _source = source;
            _values = values;
        }

        public bool ParseExpression()
        {
            var value = ParseOr();
            SkipWhitespace();
            if (!IsAtEnd)
            {
                throw new FormatException("Unexpected input after expression");
            }

            return IsTruthy(value);
        }

        private object? ParseOr()
        {
            var left = ParseAnd();
            while (Match("||"))
            {
                var right = ParseAnd();
                left = IsTruthy(left) || IsTruthy(right);
            }

            return left;
        }

        private object? ParseAnd()
        {
            var left = ParseEquality();
            while (Match("&&"))
            {
                var right = ParseEquality();
                left = IsTruthy(left) && IsTruthy(right);
            }

            return left;
        }

        private object? ParseEquality()
        {
            var left = ParsePrimary();
            if (Match("=="))
            {
                return ValuesEqual(left, ParsePrimary());
            }

            if (Match("!="))
            {
                return !ValuesEqual(left, ParsePrimary());
            }

            return left;
        }

        private object? ParsePrimary()
        {
            SkipWhitespace();
            if (Match("("))
            {
                var inner = ParseOr();
                if (!Match(")"))
                {
                    throw new FormatException("Missing ')'");
                }

                return inner;
            }

            if (MatchKeyword("true"))
            {
                return true;
            }

            if (MatchKeyword("false"))
            {
                return false;
            }

            if (TryReadString(out var text))
            {
                return text;
            }

            if (TryReadNumber(out var number))
            {
                return number;
            }

            if (TryReadIdentifier(out var name))
            {
                return Lookup(name);
            }

            throw new FormatException($"Unexpected token at {_index}");
        }

        private object? Lookup(string name)
        {
            foreach (var (key, value) in _values)
            {
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }

            return null;
        }

        private bool Match(string token)
        {
            SkipWhitespace();
            if (_index + token.Length > _source.Length)
            {
                return false;
            }

            if (!string.Equals(_source.Substring(_index, token.Length), token, StringComparison.Ordinal))
            {
                return false;
            }

            _index += token.Length;
            return true;
        }

        private bool MatchKeyword(string keyword)
        {
            SkipWhitespace();
            if (_index + keyword.Length > _source.Length)
            {
                return false;
            }

            if (!string.Equals(_source.Substring(_index, keyword.Length), keyword, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var next = _index + keyword.Length;
            if (next < _source.Length && (char.IsLetterOrDigit(_source[next]) || _source[next] == '_' || _source[next] == '.'))
            {
                return false;
            }

            _index = next;
            return true;
        }

        private bool TryReadIdentifier(out string name)
        {
            SkipWhitespace();
            if (IsAtEnd || !(char.IsLetter(_source[_index]) || _source[_index] == '_'))
            {
                name = string.Empty;
                return false;
            }

            var start = _index;
            _index++;
            while (!IsAtEnd && (char.IsLetterOrDigit(_source[_index]) || _source[_index] == '_' || _source[_index] == '.'))
            {
                _index++;
            }

            name = _source[start.._index];
            return true;
        }

        private bool TryReadString(out string text)
        {
            SkipWhitespace();
            if (IsAtEnd || (_source[_index] != '"' && _source[_index] != '\''))
            {
                text = string.Empty;
                return false;
            }

            var quote = _source[_index++];
            var builder = new StringBuilder();
            while (!IsAtEnd && _source[_index] != quote)
            {
                builder.Append(_source[_index++]);
            }

            if (IsAtEnd)
            {
                throw new FormatException("Unterminated string");
            }

            _index++;
            text = builder.ToString();
            return true;
        }

        private bool TryReadNumber(out double number)
        {
            SkipWhitespace();
            var start = _index;
            if (!IsAtEnd && _source[_index] == '-')
            {
                _index++;
            }

            var digits = 0;
            while (!IsAtEnd && char.IsDigit(_source[_index]))
            {
                digits++;
                _index++;
            }

            if (!IsAtEnd && _source[_index] == '.')
            {
                _index++;
                while (!IsAtEnd && char.IsDigit(_source[_index]))
                {
                    digits++;
                    _index++;
                }
            }

            if (digits == 0)
            {
                _index = start;
                number = 0;
                return false;
            }

            number = double.Parse(_source[start.._index], CultureInfo.InvariantCulture);
            return true;
        }

        private void SkipWhitespace()
        {
            while (!IsAtEnd && char.IsWhiteSpace(_source[_index]))
            {
                _index++;
            }
        }

        private bool IsAtEnd => _index >= _source.Length;
    }

    internal static bool ValuesEqual(object? left, object? right)
    {
        if (TryCoerceBool(left, out var leftBool) && TryCoerceBool(right, out var rightBool))
        {
            return leftBool == rightBool;
        }

        if (TryCoerceNumber(left, out var leftNumber) && TryCoerceNumber(right, out var rightNumber))
        {
            return Math.Abs(leftNumber - rightNumber) < 0.0000001;
        }

        return string.Equals(StringValue(left), StringValue(right), StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsTruthy(object? value)
    {
        if (TryCoerceBool(value, out var boolean))
        {
            return boolean;
        }

        if (TryCoerceNumber(value, out var number))
        {
            return Math.Abs(number) > 0.0000001;
        }

        var text = StringValue(value);
        return !string.IsNullOrWhiteSpace(text);
    }

    private static bool TryCoerceBool(object? value, out bool result)
    {
        switch (value)
        {
            case bool boolean:
                result = boolean;
                return true;
            case string text when bool.TryParse(text, out result):
                return true;
            default:
                result = false;
                return false;
        }
    }

    private static bool TryCoerceNumber(object? value, out double result)
    {
        switch (value)
        {
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result):
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static string StringValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }
}
