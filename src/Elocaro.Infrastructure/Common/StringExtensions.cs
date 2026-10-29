namespace Elocaro.Infrastructure.Common;

internal static class StringExtensions
{
    public static string Truncate(this string value, int length) => value.Length > length ? value[..length] : value;
}
